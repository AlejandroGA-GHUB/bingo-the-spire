using System.Globalization;

namespace BingoTheSpire.Core.Boards;

/// <summary>
/// Short codes like <c>7K2M9QX4ZB1C-1V5M</c>: seed, then goal-list version, mode letter, size and
/// difficulty letter. Pasted into the bingo-seed box, a code rebuilds the same board as long as
/// the characters match. A bare seed is also accepted and uses the run's own settings.
/// </summary>
public static class BoardCode
{
    public sealed record Parsed(string SeedText, int GoalListVersion, BingoMode Mode, int Size, BoardDifficulty Difficulty);

    public static string Encode(BingoBoard board) =>
        $"{board.SeedText}-{board.GoalListVersion.ToString(CultureInfo.InvariantCulture)}"
        + $"{ModeLetter(board.Mode)}{board.Size}{DifficultyLetter(board.Difficulty)}";

    /// <summary>Parses a typed board code. Accepts lower case and the game's O→0 / I→1 cleanup.</summary>
    public static bool TryParse(string text, out Parsed? code)
    {
        code = null;
        var canonical = BingoSeed.Canonicalize(text);
        var dash = canonical.LastIndexOf('-');
        if (dash <= 0)
        {
            return false;
        }
        var seed = canonical[..dash];
        var tail = canonical[(dash + 1)..];
        if (!BingoSeed.IsValid(seed) || tail.Length < 4)
        {
            return false;
        }
        var versionText = tail[..^3];
        if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || !TryMode(tail[^3], out var mode)
            || !int.TryParse(tail.AsSpan(tail.Length - 2, 1), NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            || !BoardRules.IsSupportedSize(size)
            || !TryDifficulty(tail[^1], out var difficulty))
        {
            return false;
        }
        code = new Parsed(seed, version, mode, size, difficulty);
        return true;
    }

    private static char ModeLetter(BingoMode mode) => mode switch
    {
        BingoMode.Versus => 'V',
        BingoMode.Lockout => 'L',
        BingoMode.Team => 'T',
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static bool TryMode(char c, out BingoMode mode)
    {
        mode = c switch { 'V' => BingoMode.Versus, 'L' => BingoMode.Lockout, 'T' => BingoMode.Team, _ => (BingoMode)(-1) };
        return Enum.IsDefined(mode);
    }

    private static char DifficultyLetter(BoardDifficulty difficulty) => difficulty switch
    {
        BoardDifficulty.Easy => 'E',
        BoardDifficulty.Medium => 'M',
        BoardDifficulty.Hard => 'H',
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    private static bool TryDifficulty(char c, out BoardDifficulty difficulty)
    {
        difficulty = c switch { 'E' => BoardDifficulty.Easy, 'M' => BoardDifficulty.Medium, 'H' => BoardDifficulty.Hard, _ => (BoardDifficulty)(-1) };
        return Enum.IsDefined(difficulty);
    }
}
