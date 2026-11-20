using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.Tests.Session;

public class BingoSessionTests
{
    private const ulong Alex = 1;
    private const ulong Sam = 2;

    /// <summary>
    /// A 4×4 board where square i is "play a card costing 100+i energy". Playing a card that
    /// costs 100+k fills squares 0..k (row-major) for that player.
    /// </summary>
    private static BingoSession Session()
    {
        var goals = new GoalList(1, Enumerable.Range(0, 16)
            .Select(i => new GoalDefinition($"g{i}", "G", "CardCost", [new GoalTier(1, new Dictionary<string, int> { ["n"] = 100 + i })]))
            .ToList());
        var squares = Enumerable.Range(0, 16)
            .Select(i => new BoardSquare(i, $"g{i}", 1, new Dictionary<string, int> { ["n"] = 100 + i }, "G"))
            .ToList();
        var board = new BingoBoard(4, "TEST", 1, BingoMode.Versus, BoardDifficulty.Medium, 0, squares);
        var session = new BingoSession(board, goals);
        session.Handle(new RunAttached("TEST", "Standard", false, 0, [new PlayerInfo(Alex, "IRONCLAD"), new PlayerInfo(Sam, "SILENT")], Alex));
        return session;
    }

    /// <summary>Fills squares 0 to <paramref name="i"/> for the player.</summary>
    private static void Fill(BingoSession s, ulong player, int i) =>
        s.Handle(new CardPlayed(player, "C", "Attack", 100 + i, 0, false, CreatureKind.Enemy));

    [Fact]
    public void FirstLine_IsTheBingo_AndLaterLinesAreNumbered()
    {
        var s = Session();
        var lines = new List<LineCompleted>();
        s.LineDone += lines.Add;

        // Thresholds fill every square up to i, so filling 3 fills squares 0-3: the top row.
        Fill(s, Alex, 3);
        var first = Assert.Single(lines);
        Assert.Equal((0, Alex, 1), (first.Line, first.PlayerId, first.Ordinal));

        Fill(s, Alex, 7); // squares 4-7: the second row
        Assert.Equal(2, lines.Count);
        Assert.Equal((1, 2), (lines[1].Line, lines[1].Ordinal));
    }

    [Fact]
    public void PlayersHaveTheirOwnCopyOfTheBoard()
    {
        var s = Session();
        var lines = new List<LineCompleted>();
        s.LineDone += lines.Add;

        Fill(s, Alex, 2);
        Fill(s, Sam, 3);
        var line = Assert.Single(lines);
        Assert.Equal(Sam, line.PlayerId);
        Assert.Equal(3, s.FilledBy(Alex).Count);
        Assert.Equal(4, s.FilledBy(Sam).Count);
    }

    [Fact]
    public void OneCompletion_CanFinishSeveralLines()
    {
        var s = Session();
        var lines = new List<LineCompleted>();
        s.LineDone += lines.Add;
        Fill(s, Alex, 15); // everything: 4 rows, 4 columns, 2 diagonals
        Assert.Equal(10, lines.Count);
        Assert.Equal(Enumerable.Range(1, 10), lines.Select(l => l.Ordinal));
    }
}
