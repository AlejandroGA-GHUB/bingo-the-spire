using BingoTheSpire.Core.Randomness;

namespace BingoTheSpire.Core.Boards;

/// <summary>
/// Picks which difficulty band goes in each cell. The result is a diagonal Latin square: every
/// row, column and both diagonals hold each band exactly once, so every line is equally hard.
/// </summary>
public static class DifficultyLayout
{
    // 5×5: L[r,c] = (a·r + b·c) mod 5 is a diagonal Latin square whenever a, b, a+b and a−b are
    // all non-zero mod 5. These are all such (a, b) pairs.
    private static readonly (int A, int B)[] Order5Coefficients =
        [(1, 2), (1, 3), (2, 1), (2, 4), (3, 1), (3, 4), (4, 2), (4, 3)];

    // 4×4 has no linear construction; this one is checked by the tests.
    private static readonly int[,] Order4Square =
    {
        { 0, 1, 2, 3 },
        { 2, 3, 0, 1 },
        { 3, 2, 1, 0 },
        { 1, 0, 3, 2 },
    };

    /// <summary>Band index (0..size-1) for each cell, row-major.</summary>
    public static int[] Create(int size, SeededRandom rng)
    {
        var square = new int[size, size];
        switch (size)
        {
            case 5:
                var (a, b) = Order5Coefficients[rng.Next(Order5Coefficients.Length)];
                for (var r = 0; r < 5; r++)
                {
                    for (var c = 0; c < 5; c++)
                    {
                        square[r, c] = (a * r + b * c) % 5;
                    }
                }
                break;
            case 4:
                Array.Copy(Order4Square, square, Order4Square.Length);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, "board size must be 4 or 5");
        }

        // Relabel the bands, then apply one of the 8 rotations/reflections of the square. Both keep
        // every row, column and diagonal holding each band once.
        var relabel = Enumerable.Range(0, size).ToArray();
        rng.Shuffle(relabel);
        var symmetry = rng.Next(8);

        var cells = new int[size * size];
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                var (sr, sc) = Transform(r, c, size, symmetry);
                cells[r * size + c] = relabel[square[sr, sc]];
            }
        }
        return cells;
    }

    private static (int R, int C) Transform(int r, int c, int size, int symmetry)
    {
        var m = size - 1;
        return symmetry switch
        {
            0 => (r, c),
            1 => (c, m - r),
            2 => (m - r, m - c),
            3 => (m - c, r),
            4 => (r, m - c),
            5 => (m - r, c),
            6 => (c, r),
            _ => (m - c, m - r),
        };
    }
}
