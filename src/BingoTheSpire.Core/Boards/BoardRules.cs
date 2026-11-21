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
    /// every line adds up to the same total. The last band is the one difficulty 5 squares replace.
    /// </summary>
    public static IReadOnlyList<int> DifficultyBands(int size) => size switch
    {
        5 => [1, 2, 3, 3, 4],
        4 => [1, 2, 3, 3],
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "board size must be 4 or 5"),
    };

    /// <summary>
    /// How many squares are raised to difficulty 5: none on Easy, the middle one on Medium, and on
    /// Hard the middle one plus two more (one more on 4×4, which has fewer lines).
    /// </summary>
    public static int HardSquares(int size, BoardDifficulty difficulty) => difficulty switch
    {
        BoardDifficulty.Easy => 0,
        BoardDifficulty.Medium => 1,
        _ => size == 5 ? 3 : 2,
    };

    /// <summary>Readable name of a line from <see cref="Lines"/>, e.g. "Row 2", "Column 5", "Diagonal ↘".</summary>
    public static string LineName(int size, int line) => line switch
    {
        _ when line < size => $"Row {line + 1}",
        _ when line < size * 2 => $"Column {line - size + 1}",
        _ when line == size * 2 => "Diagonal ↘",
        _ => "Diagonal ↙",
    };

    /// <summary>
    /// "One away" squares (spec 4.5): empty squares that would finish a line for a player who has
    /// <paramref name="filled"/>. A line with a <paramref name="blocked"/> square (Lockout: taken by
    /// someone else) can't be finished, so it never counts.
    /// </summary>
    public static IReadOnlySet<int> OneAway(int size, IReadOnlySet<int> filled, IReadOnlySet<int>? blocked = null)
    {
        var result = new HashSet<int>();
        foreach (var line in Lines(size))
        {
            if (blocked != null && line.Any(blocked.Contains))
            {
                continue;
            }
            var missing = line.Where(cell => !filled.Contains(cell)).ToList();
            if (missing.Count == 1)
            {
                result.Add(missing[0]);
            }
        }
        return result;
    }

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
