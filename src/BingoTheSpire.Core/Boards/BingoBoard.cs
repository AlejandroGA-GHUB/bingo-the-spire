using System.Globalization;
using System.Text;
using BingoTheSpire.Core.Hashing;

namespace BingoTheSpire.Core.Boards;

/// <summary>Everything that decides which board a run gets (spec section 5).</summary>
/// <param name="SeedText">Canonical bingo seed if one was entered, otherwise the run seed.</param>
/// <param name="GameMode">The game's mode name, e.g. "Standard", "Custom", "Blitz".</param>
/// <param name="CharacterIds">Character id of every player in the run.</param>
/// <param name="Size">Board size. Null uses the game mode's default (5, or 4 in Blitz).</param>
public sealed record BoardRequest(
    string SeedText,
    BingoMode Mode,
    string GameMode,
    IReadOnlyList<string> CharacterIds,
    BoardDifficulty Difficulty = BoardDifficulty.Medium,
    int? Size = null);

/// <summary>What this game version can detect. Goals needing anything else are left off the board.</summary>
/// <param name="Events">Available event type names. Null means all.</param>
/// <param name="Detectors">Implemented detector names. Null means all.</param>
public sealed record DetectionCapabilities(IReadOnlySet<string>? Events, IReadOnlySet<string>? Detectors)
{
    public static readonly DetectionCapabilities All = new(null, null);
}

/// <param name="Index">Cell index, row-major from the top-left.</param>
public sealed record BoardSquare(int Index, string GoalId, int Difficulty, IReadOnlyDictionary<string, int> Params, string Text);

public sealed record BingoBoard(
    int Size,
    string SeedText,
    int GoalListVersion,
    BingoMode Mode,
    BoardDifficulty Difficulty,
    ulong BoardSeed,
    IReadOnlyList<BoardSquare> Squares)
{
    /// <summary>Code that rebuilds this board when pasted into the bingo-seed box (with the same characters).</summary>
    public string Code => BoardCode.Encode(this);

    /// <summary>
    /// Fingerprint of the board's content. In co-op, every player compares theirs with the host's
    /// before the run starts.
    /// </summary>
    public ulong ContentHash()
    {
        var sb = new StringBuilder()
            .Append(Size.ToString(CultureInfo.InvariantCulture)).Append('|')
            .Append(GoalListVersion.ToString(CultureInfo.InvariantCulture));
        foreach (var square in Squares)
        {
            sb.Append('|').Append(square.GoalId);
            foreach (var (name, value) in square.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append(',').Append(name).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
            }
        }
        return StableHash.Fnv1a64(sb.ToString());
    }

    public BoardSquare this[int row, int column] => Squares[row * Size + column];
}
