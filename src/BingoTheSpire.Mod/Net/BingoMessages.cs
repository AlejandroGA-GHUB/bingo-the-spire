using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Mod.Net;

// Bingo's own messages on the game's co-op connection (spec section 7). The game finds them by
// looking for INetMessage types in loaded mods and numbers all message types by name, so every
// player with the same mods agrees on the numbers. The names start with "BingoTheSpire" so they
// can't clash with the game's or another mod's.

/// <summary>Game enum values these messages return, looked up by name (the numbers can differ between branches).</summary>
internal static class NetEnums
{
    public static readonly NetTransferMode Reliable = Enum.Parse<NetTransferMode>("Reliable");
    public static readonly LogLevel Info = Enum.Parse<LogLevel>("Info");
    public static readonly LogLevel Debug = Enum.Parse<LogLevel>("Debug");
}

/// <summary>Host → everyone, in the lobby: the bingo settings the host chose.</summary>
public struct BingoTheSpireSettingsMessage : INetMessage
{
    public bool Enabled;
    public string SeedEntry;
    public int BingoMode;
    public string ModVersion;

    public readonly bool ShouldBroadcast => false;
    readonly NetTransferMode INetMessage.Mode => NetEnums.Reliable;
    public readonly LogLevel LogLevel => NetEnums.Info;
    public readonly bool ShouldBuffer => true;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Enabled);
        writer.WriteString(SeedEntry ?? "");
        writer.WriteInt(BingoMode);
        writer.WriteString(ModVersion ?? "");
    }

    public void Deserialize(PacketReader reader)
    {
        Enabled = reader.ReadBool();
        SeedEntry = reader.ReadString();
        BingoMode = reader.ReadInt();
        ModVersion = reader.ReadString();
    }
}

/// <summary>
/// Client → host. In the lobby: "send me your settings". When the client's run starts (new run,
/// Continue, rejoin): "send me the board".
/// </summary>
public struct BingoTheSpireHelloMessage : INetMessage
{
    public bool InLobby;
    public string BoardHash;
    public string ModVersion;

    public readonly bool ShouldBroadcast => false;
    readonly NetTransferMode INetMessage.Mode => NetEnums.Reliable;
    public readonly LogLevel LogLevel => NetEnums.Info;
    public readonly bool ShouldBuffer => true;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteBool(InLobby);
        writer.WriteString(BoardHash ?? "");
        writer.WriteString(ModVersion ?? "");
    }

    public void Deserialize(PacketReader reader)
    {
        InLobby = reader.ReadBool();
        BoardHash = reader.ReadString();
        ModVersion = reader.ReadString();
    }
}

/// <summary>Host → one client: the whole board as the host has it. The client's board becomes exactly this.</summary>
public struct BingoTheSpireSnapshotMessage : INetMessage
{
    public bool Enabled;
    public string BoardCode;
    public string BoardHash;
    public List<ClaimResolved> Results;

    public readonly bool ShouldBroadcast => false;
    readonly NetTransferMode INetMessage.Mode => NetEnums.Reliable;
    public readonly LogLevel LogLevel => NetEnums.Info;
    public readonly bool ShouldBuffer => true;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Enabled);
        writer.WriteString(BoardCode ?? "");
        writer.WriteString(BoardHash ?? "");
        var results = Results ?? [];
        writer.WriteInt(results.Count);
        foreach (var result in results)
        {
            NetCodec.WriteResult(writer, result);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        Enabled = reader.ReadBool();
        BoardCode = reader.ReadString();
        BoardHash = reader.ReadString();
        var count = reader.ReadInt();
        Results = new List<ClaimResolved>(Math.Clamp(count, 0, 4096));
        for (var i = 0; i < count; i++)
        {
            Results.Add(NetCodec.ReadResult(reader));
        }
    }
}

/// <summary>Client → host: the client's player completed a goal.</summary>
public struct BingoTheSpireClaimMessage : INetMessage
{
    public int Square;
    public string GoalId;
    public ulong PlayerId;
    public string Evidence;
    public RunPosition? At;
    public string? Moment;

    public readonly bool ShouldBroadcast => false;
    readonly NetTransferMode INetMessage.Mode => NetEnums.Reliable;
    public readonly LogLevel LogLevel => NetEnums.Debug;
    public readonly bool ShouldBuffer => true;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteInt(Square);
        writer.WriteString(GoalId ?? "");
        writer.WriteULong(PlayerId);
        writer.WriteString(Evidence ?? "");
        NetCodec.WritePosition(writer, At);
        writer.WriteString(Moment ?? "");
    }

    public void Deserialize(PacketReader reader)
    {
        Square = reader.ReadInt();
        GoalId = reader.ReadString();
        PlayerId = reader.ReadULong();
        Evidence = reader.ReadString();
        At = NetCodec.ReadPosition(reader);
        Moment = NetCodec.Optional(reader.ReadString());
    }
}

/// <summary>Host → everyone: the host's decision on a claim. Everyone applies results in the host's order.</summary>
public struct BingoTheSpireResultMessage : INetMessage
{
    public ClaimResolved Result;

    public readonly bool ShouldBroadcast => false;
    readonly NetTransferMode INetMessage.Mode => NetEnums.Reliable;
    public readonly LogLevel LogLevel => NetEnums.Debug;
    public readonly bool ShouldBuffer => true;

    public readonly void Serialize(PacketWriter writer) => NetCodec.WriteResult(writer, Result);

    public void Deserialize(PacketReader reader) => Result = NetCodec.ReadResult(reader);
}

internal static class NetCodec
{
    public static void WriteResult(PacketWriter writer, ClaimResolved result)
    {
        writer.WriteLong(result.Order);
        writer.WriteInt(result.Square);
        writer.WriteString(result.GoalId ?? "");
        writer.WriteULong(result.PlayerId);
        writer.WriteBool(result.Accepted);
        writer.WriteString(result.Evidence ?? "");
        WritePosition(writer, result.At);
        writer.WriteString(result.Moment ?? "");
    }

    /// <summary>Empty strings stand for null on the wire.</summary>
    public static string? Optional(string text) => text.Length == 0 ? null : text;

    public static ClaimResolved ReadResult(PacketReader reader)
    {
        var order = reader.ReadLong();
        var square = reader.ReadInt();
        var goalId = reader.ReadString();
        var playerId = reader.ReadULong();
        var accepted = reader.ReadBool();
        var evidence = reader.ReadString();
        var at = ReadPosition(reader);
        var moment = Optional(reader.ReadString());
        return new ClaimResolved(order, square, goalId, playerId, accepted, evidence) { At = at, Moment = moment };
    }

    public static void WritePosition(PacketWriter writer, RunPosition? at)
    {
        writer.WriteBool(at != null);
        if (at == null)
        {
            return;
        }
        writer.WriteInt(at.Act);
        writer.WriteInt(at.ActFloor);
        writer.WriteInt(at.Floor);
        writer.WriteBool(at.Round != null);
        writer.WriteInt(at.Round ?? 0);
    }

    public static RunPosition? ReadPosition(PacketReader reader)
    {
        if (!reader.ReadBool())
        {
            return null;
        }
        var act = reader.ReadInt();
        var actFloor = reader.ReadInt();
        var floor = reader.ReadInt();
        var hasRound = reader.ReadBool();
        var round = reader.ReadInt();
        return new RunPosition(act, actFloor, floor, hasRound ? round : null);
    }
}
