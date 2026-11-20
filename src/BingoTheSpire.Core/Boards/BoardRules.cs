namespace BingoTheSpire.Core.Boards;

public enum BingoMode
{
    Versus,
    Lockout,
    Team,
}

public enum BoardDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>How the game mode and difficulty shape a board (spec section 6.2).</summary>
public static class BoardRules
{
    public const string StandardClass = "Standard";
    public const string BlitzClass = "Blitz";

    /// <summary>
    /// The only thing about the game mode that matters to the board. Standard, Custom, Daily and
    /// Dread all count as Standard, so replaying a run's seed in a Custom run gives the same board.
    /// <paramref name="gameMode"/> is the game's mode name (never its number, which differs by branch).
    /// </summary>
    public static string GameModeClass(string gameMode) =>
        string.Equals(gameMode, "Blitz", StringComparison.OrdinalIgnoreCase) ? BlitzClass : StandardClass;

    public static int DefaultSize(string gameModeClass) => gameModeClass == BlitzClass ? 4 : 5;

    public static bool IsSupportedSize(int size) => size is 4 or 5;

    /// <summary>
    /// Target difficulty for each band of the layout. Every line has one cell of every band, so
    /// every line adds up to the same total.
    /// </summary>
    public static IReadOnlyList<int> DifficultyBands(int size, BoardDifficulty difficulty) => (size, difficulty) switch
    {
        (5, BoardDifficulty.Easy) => [1, 1, 2, 2, 3],
        (5, BoardDifficulty.Medium) => [1, 2, 3, 3, 4],
        (5, BoardDifficulty.Hard) => [2, 3, 4, 4, 5],
        (4, BoardDifficulty.Easy) => [1, 1, 2, 3],
        (4, BoardDifficulty.Medium) => [1, 2, 3, 3],
        (4, BoardDifficulty.Hard) => [2, 3, 4, 5],
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "board size must be 4 or 5"),
    };

    /// <summary>Every row, column and both diagonals, as lists of cell indexes (row-major).</summary>
    public static IReadOnlyList<int[]> Lines(int size)
    {
        var lines = new List<int[]>();
        for (var r = 0; r < size; r++)
        {
            lines.Add(Enumerable.Range(0, size).Select(c => r * size + c).ToArray());
        }
        for (var c = 0; c < size; c++)
        {
            lines.Add(Enumerable.Range(0, size).Select(r => r * size + c).ToArray());
        }
        lines.Add(Enumerable.Range(0, size).Select(i => i * size + i).ToArray());
        lines.Add(Enumerable.Range(0, size).Select(i => i * size + (size - 1 - i)).ToArray());
        return lines;
    }
}
