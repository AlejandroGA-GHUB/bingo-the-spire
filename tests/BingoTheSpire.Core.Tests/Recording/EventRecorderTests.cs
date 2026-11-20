using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Recording;

namespace BingoTheSpire.Core.Tests.Recording;

public class EventRecorderTests
{
    private static RecordingHeader Header() => new(
        RecordingHeader.CurrentFormat, "0.1.0", "v0.111.1", "Main", "2026-11-20T12:00:00Z",
        new[]
        {
            new SourceStatus("AfterCardPlayed", true, null, new[] { nameof(CardPlayed) }),
            new SourceStatus("AfterDeath", false, "signature differs", Array.Empty<string>()),
        });

    [Fact]
    public void WritesOneLinePerEvent_WithTypeTag()
    {
        var sw = new StringWriter();
        using var recorder = new EventRecorder(sw);

        recorder.WriteHeader(Header());
        recorder.Record(new CardPlayed(1, "STRIKE_IRONCLAD", "Attack", 1, 0, false, CreatureKind.Enemy) { Seq = 1 });
        recorder.Record(new BlockGained(1, CreatureKind.Player, null, 5, "DEFEND_IRONCLAD") { Seq = 2 });

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"format\":\"bingothespire-events/1\"", lines[0]);
        Assert.StartsWith("{\"t\":\"CardPlayed\"", lines[1]);
        Assert.StartsWith("{\"t\":\"BlockGained\"", lines[2]);
    }

    [Fact]
    public void RoundTrips_HeaderAndEvents()
    {
        var sw = new StringWriter();
        var original = new GameEvent[]
        {
            new RunAttached("7K2M9QX4ZB1C", "Standard", false, 0, new[] { new PlayerInfo(1, "IRONCLAD") }, 1) { Seq = 1 },
            new RoomEntered("Elite", "GREMLIN_LEADER") { Seq = 2, At = new RunPosition(0, 6, 6, null) },
            new DamageDealt(1, CreatureKind.Player, CreatureKind.Enemy, null, "GREMLIN_LEADER", 3u, 12, 3, 0, true, "BASH") { Seq = 3, At = new RunPosition(0, 6, 6, 4) },
        };
        using (var recorder = new EventRecorder(sw))
        {
            recorder.WriteHeader(Header());
            foreach (var evt in original)
            {
                recorder.Record(evt);
            }
        }

        var (header, events) = EventRecorder.Read(new StringReader(sw.ToString()));

        Assert.NotNull(header);
        Assert.Equal(2, header!.Sources.Count);
        Assert.False(header.Sources[1].Connected);
        Assert.Equal(original.Length, events.Count);
        Assert.Equal(original[0], events[0], new RunAttachedComparer());
        Assert.Equal(original[1], events[1]);
        Assert.Equal(original[2], events[2]);
    }

    [Fact]
    public void NullFields_AreOmitted()
    {
        var sw = new StringWriter();
        using var recorder = new EventRecorder(sw);

        recorder.Record(new BlockGained(null, CreatureKind.Enemy, 2u, 8, null));

        Assert.DoesNotContain("cardId", sw.ToString());
        Assert.DoesNotContain("playerId", sw.ToString());
    }

    /// <summary>Records compare lists by reference, so compare RunAttached field by field.</summary>
    private sealed class RunAttachedComparer : IEqualityComparer<GameEvent>
    {
        public bool Equals(GameEvent? x, GameEvent? y) =>
            x is RunAttached a && y is RunAttached b
            && a.Seed == b.Seed && a.GameMode == b.GameMode && a.IsCustom == b.IsCustom
            && a.Ascension == b.Ascension && a.LocalPlayerId == b.LocalPlayerId && a.Seq == b.Seq
            && a.Players.SequenceEqual(b.Players);

        public int GetHashCode(GameEvent obj) => 0;
    }
}
