using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.History;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.Tests.History;

public class RunHistoryTests
{
    private const ulong Alex = 1;
    private const ulong Sam = 2;
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static GoalList Goals(int count) => new(1, Enumerable.Range(0, count)
        .Select(i => new GoalDefinition($"g{i}", "G", "CardCost", [new GoalTier(1, new Dictionary<string, int> { ["n"] = 100 + i })]))
        .ToList());

    private static (BingoSession Session, RunAttached Run) Start(BingoMode mode, params ulong[] players)
    {
        var board = new BingoBoard(4, "TEST", 1, mode, BoardDifficulty.Medium, 0,
            Enumerable.Range(0, 16).Select(i => new BoardSquare(i, $"g{i}", 1, new Dictionary<string, int> { ["n"] = 100 + i }, "G")).ToList());
        var session = new BingoSession(board, Goals(16));
        var run = new RunAttached("TEST", "Standard", false, 0, players.Select(p => new PlayerInfo(p, p == Alex ? "IRONCLAD" : "SILENT")).ToList(), Alex);
        session.Handle(run);
        return (session, run);
    }

    /// <summary>Fills squares 0..i for the player at a given play time and floor.</summary>
    private static void Fill(BingoSession s, ulong player, int i, long ms, int floor)
    {
        s.Handle(new RoomEntered("Monster", null) { Ms = ms, At = new RunPosition(0, floor, floor, null) });
        s.Handle(new CardPlayed(player, "C", "Attack", 100 + i, 0, false, CreatureKind.Enemy) { Ms = ms, At = new RunPosition(0, floor, floor, 1) });
    }

    private static string Name(ulong id) => id == Alex ? "Alex" : "Sam";

    [Fact]
    public void Solo_RecordsLinesWithFloorAndTime_AndSurvivesARoundTrip()
    {
        var (s, run) = Start(BingoMode.Versus, Alex);
        Fill(s, Alex, 3, ms: 600_000, floor: 6);   // top row
        Fill(s, Alex, 7, ms: 900_000, floor: 9);   // second row
        s.Handle(new RunEnded(false, false) { Ms = 1_200_000 });

        var entry = RunHistory.Record(s, run, new RunEnded(false, false), Alex, Name, Now);
        Assert.True(entry.Solo);
        Assert.Equal("lost", entry.Result);
        Assert.Equal(1_200_000, entry.PlayMs);
        Assert.Equal(8, entry.Me!.Squares);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(new HistoryLine(Alex, 0, 1, 0, 6, 600_000), entry.FirstLine);
        Assert.Equal((2, 9), (entry.Lines[1].Ordinal, entry.Lines[1].Floor));

        Assert.Equal(9, entry.EndFloor);
        Assert.Equal(16, entry.DifficultyTotals!.Sum());
        Assert.Equal(8, entry.DifficultyDone!.Sum());

        var back = Assert.Single(RunHistory.Parse([RunHistory.Serialize(entry), "not json", ""]));
        Assert.Equal(entry.FirstLine, back.FirstLine);
        Assert.Equal(entry.BoardCode, back.BoardCode);
        Assert.Equal(entry.DifficultyDone, back.DifficultyDone);

        // Entries saved before these fields existed (nulls aren't written) still read, with them empty.
        var old = Assert.Single(RunHistory.Parse([RunHistory.Serialize(entry with { EndAct = null, EndFloor = null, DifficultyTotals = null, DifficultyDone = null })]));
        Assert.Null(old.DifficultyDone);
    }

    [Fact]
    public void Versus_RecordsPlacements_AndYourFirstLineIsYours()
    {
        var (s, run) = Start(BingoMode.Versus, Alex, Sam);
        Fill(s, Sam, 3, ms: 100_000, floor: 4);
        Fill(s, Alex, 3, ms: 200_000, floor: 7);
        var entry = RunHistory.Record(s, run, new RunEnded(true, false), Alex, Name, Now);

        Assert.Equal(Sam, entry.Winner);
        Assert.Equal(2, entry.Players.Single(p => p.Id == Alex).Place);
        Assert.Equal(1, entry.Players.Single(p => p.Id == Sam).Place);
        Assert.Equal(7, entry.FirstLine!.Floor);
    }

    [Fact]
    public void Bests_PickTheEarliestFirstLine_ByFloorAndByTime()
    {
        HistoryEntry Entry(int? floor, long ms) => new(1, 0, "Versus", true, "S", "CODE", "Medium", 5, 25, "lost", ms, Alex,
            [new HistoryPlayer(Alex, "Alex", "IRONCLAD", 5, floor == null ? 0 : 1, null)],
            floor == null ? [] : [new HistoryLine(Alex, 0, 1, 0, floor, ms)], null, null);
        var low = Entry(5, 900_000);
        var quick = Entry(8, 300_000);
        var none = Entry(null, 0);

        var bests = RunHistory.Bests([low, quick, none]);
        Assert.Same(low, bests.FewestFloors);
        Assert.Same(quick, bests.FastestTime);
        Assert.Equal((3, 2), (bests.Runs, bests.Bingos));
    }

    [Theory]
    [InlineData(65_000, "1:05")]
    [InlineData(3_725_000, "1:02:05")]
    public void FormatTime(long ms, string text) => Assert.Equal(text, RunHistory.FormatTime(ms));
}
