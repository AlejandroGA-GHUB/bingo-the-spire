using BingoTheSpire.Core.Boards;

namespace BingoTheSpire.Core.Session;

/// <summary>
/// What the player chose before the run (spec 4.1). In co-op, the host's choice applies to everyone.
/// </summary>
/// <param name="Enabled">False plays the run without bingo.</param>
/// <param name="SeedEntry">What's in the bingo-seed box: empty (use the run seed), a bingo seed, or a board code.</param>
/// <param name="Mode">Co-op mode. Solo always plays Versus, unless a board code says otherwise.</param>
/// <param name="Difficulty">How many difficulty 5 squares the board gets. A board code brings its own.</param>
public sealed record BingoSetup(bool Enabled, string? SeedEntry, BingoMode Mode, BoardDifficulty Difficulty = BoardDifficulty.Easy)
{
    public static BingoSetup Default { get; } = new(true, null, BingoMode.Versus);

    /// <summary>The board request for a run with this setup. Throws <see cref="BoardBuildException"/> for a bad seed entry.</summary>
    public BoardRequest RequestFor(string runSeed, string gameMode, IReadOnlyList<string> characterIds, bool coop, int goalListVersion)
    {
        var entry = SeedEntryParser.Parse(SeedEntry);
        switch (entry.Kind)
        {
            case SeedEntryKind.Code:
                var code = entry.Code!;
                if (code.GoalListVersion != goalListVersion)
                {
                    throw new BoardBuildException(
                        $"board seed {SeedEntry} is from a different version of Bingo the Spire (goal list v{code.GoalListVersion}; this one uses v{goalListVersion})");
                }
                // A code reproduces its board exactly, mode and all.
                return new BoardRequest(code.SeedText, code.Mode, gameMode, characterIds, code.Difficulty, code.Size);
            case SeedEntryKind.Invalid:
                throw new BoardBuildException(entry.Error!);
            default:
                var seed = entry.Kind == SeedEntryKind.Seed ? entry.Seed! : runSeed;
                return new BoardRequest(seed, coop ? Mode : BingoMode.Versus, gameMode, characterIds, Difficulty);
        }
    }
}

public enum SeedEntryKind
{
    Empty,
    Seed,
    Code,
    Invalid,
}

public sealed record SeedEntry(SeedEntryKind Kind, string? Seed = null, BoardCode.Parsed? Code = null, string? Error = null);

/// <summary>Reads the bingo-seed box: empty, a bingo seed, or a full board code like <c>7K2M9QX4ZB1C-1V5M</c>.</summary>
public static class SeedEntryParser
{
    public static SeedEntry Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SeedEntry(SeedEntryKind.Empty);
        }
        if (text.Contains('-'))
        {
            return BoardCode.TryParse(text.Trim(), out var code) && code != null
                ? new SeedEntry(SeedEntryKind.Code, Code: code)
                : new SeedEntry(SeedEntryKind.Invalid, Error: $"'{text.Trim()}' isn't a board seed. Check it was pasted in full");
        }
        var seed = BingoSeed.Canonicalize(text);
        return BingoSeed.IsValid(seed)
            ? new SeedEntry(SeedEntryKind.Seed, Seed: seed)
            : new SeedEntry(SeedEntryKind.Invalid,
                Error: $"'{text.Trim()}' isn't a valid seed: use up to {BingoSeed.MaxLength} letters and digits");
    }
}
