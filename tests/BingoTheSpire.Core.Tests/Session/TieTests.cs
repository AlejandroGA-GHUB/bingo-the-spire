using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.Tests.Session;

/// <summary>Lockout ties (spec 4.3.2): claims for one square from the same moment all get it.</summary>
public class TieTests
{
    private const ulong Alex = 1;
    private const ulong Sam = 2;
    private const ulong Kim = 3;
    private const ulong Lee = 4;

    private static BoardState State(BingoMode mode, int size, params ulong[] players)
    {
        var board = new BingoBoard(size, "TEST", 1, mode, BoardDifficulty.Medium, 0,
            Enumerable.Range(0, size * size)
                .Select(i => new BoardSquare(i, $"g{i}", 1, new Dictionary<string, int> { ["n"] = i }, "G"))
                .ToList());
        var state = new BoardState(board);
        state.SetPlayers(players);
        return state;
    }

    /// <summary>The authority decides and applies a claim, like the host does. Returns the result.</summary>
    private static ClaimResolved? Claim(BoardState state, ulong player, int square, string? moment = null)
    {
        var result = state.Decide(new Claim(square, $"g{square}", player, "e", null, moment));
        if (result != null)
        {
            state.Apply(result);
        }
        return result;
    }

    private static void Give(BoardState state, ulong player, params int[] squares)
    {
        foreach (var square in squares)
        {
            Assert.True(Claim(state, player, square)!.Accepted);
        }
    }

    [Fact]
    public void SameMoment_BothPlayersGetTheSquare()
    {
        var state = State(BingoMode.Lockout, 5, Alex, Sam);
        var filled = new List<SquareFilled>();
        state.Filled += filled.Add;

        Assert.True(Claim(state, Alex, 12, "F3:A40")!.Accepted);
        Assert.True(Claim(state, Sam, 12, "F3:A40")!.Accepted);

        Assert.Equal([Alex, Sam], state.OwnersOf(12));
        Assert.Equal(Alex, state.OwnerOf(12));
        Assert.Contains(12, state.FilledBy(Alex));
        Assert.Contains(12, state.FilledBy(Sam));
        Assert.Equal([false, true], filled.Select(f => f.Tie));
    }

    [Fact]
    public void DifferentMoment_OrNone_StaysLocked()
    {
        var state = State(BingoMode.Lockout, 5, Alex, Sam, Kim);
        Claim(state, Alex, 12, "F3:A40");

        Assert.False(Claim(state, Sam, 12, "F3:A41")!.Accepted);
        Assert.False(Claim(state, Kim, 12)!.Accepted);
        Assert.Equal([Alex], state.OwnersOf(12));

        // A square taken without a moment can't be tied either.
        Claim(state, Alex, 0);
        Assert.False(Claim(state, Sam, 0)!.Accepted);
    }

    [Fact]
    public void ClientSendsATiedClaim_EvenThoughTheSquareIsTaken()
    {
        var state = State(BingoMode.Lockout, 5, Alex, Sam);
        Claim(state, Alex, 12, "F3:A40");

        Assert.True(state.CouldAccept(new Claim(12, "g12", Sam, "e", null, "F3:A40")));
        Assert.False(state.CouldAccept(new Claim(12, "g12", Sam, "e", null, "F3:A41")));
        Assert.False(state.CouldAccept(new Claim(12, "g12", Alex, "e", null, "F3:A40")));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ThreeOrFourPlayers_CanAllTie(int players)
    {
        ulong[] everyone = [Alex, Sam, Kim, Lee];
        var state = State(BingoMode.Lockout, 5, everyone.Take(players).ToArray());
        foreach (var player in everyone.Take(players))
        {
            Assert.True(Claim(state, player, 7, "F9:won")!.Accepted);
        }
        Assert.Equal(everyone.Take(players), state.OwnersOf(7));
    }

    [Fact]
    public void TeamAndVersus_HaveNoTies()
    {
        var team = State(BingoMode.Team, 5, Alex, Sam);
        Claim(team, Alex, 12, "F3:A40");
        Assert.False(Claim(team, Sam, 12, "F3:A40")!.Accepted);
        Assert.Equal([Alex], team.OwnersOf(12));

        // In Versus everyone has their own copy anyway.
        var versus = State(BingoMode.Versus, 5, Alex, Sam);
        Claim(versus, Alex, 12, "F3:A40");
        Assert.True(Claim(versus, Sam, 12, "F3:A41")!.Accepted);
        Assert.Empty(versus.OwnersOf(12));
    }

    [Fact]
    public void TiedMiddleSquare_FinishesARowForOneAndAColumnForTheOther_AndBothWin()
    {
        var state = State(BingoMode.Lockout, 5, Alex, Sam);
        var lines = new List<LineCompleted>();
        var wins = new List<BingoWin>();
        state.LineDone += lines.Add;
        state.Won += wins.Add;

        Give(state, Alex, 10, 11, 13, 14); // row 3, all but the middle
        Give(state, Sam, 2, 7, 17, 22);    // column 3, all but the middle
        Claim(state, Alex, 12, "F5:A77");
        Claim(state, Sam, 12, "F5:A77");

        var size = 5;
        Assert.Equal([(Alex, BoardRules.Lines(size).ToList().FindIndex(l => l.SequenceEqual(new[] { 10, 11, 12, 13, 14 }))),
                      (Sam, BoardRules.Lines(size).ToList().FindIndex(l => l.SequenceEqual(new[] { 2, 7, 12, 17, 22 })))],
            lines.Select(l => (l.PlayerId, l.Line)));
        Assert.Equal([new BingoWin(Alex, WinReason.Line, null), new BingoWin(Sam, WinReason.Line, null, Draw: true)], wins);
        Assert.Equal(new BingoWin(Alex, WinReason.Line, null), state.Winner);
        Assert.Equal(2, state.Winners.Count);
        Assert.Equal((1, 1), (state.PlaceOf(Alex), state.PlaceOf(Sam)));
        // The shared square counts for both.
        Assert.Equal((5, 5), (state.FilledBy(Alex).Count, state.FilledBy(Sam).Count));
    }

    [Fact]
    public void LinesAtDifferentMoments_AreNotADraw()
    {
        var state = State(BingoMode.Lockout, 5, Alex, Sam, Kim);
        Give(state, Alex, 10, 11, 13, 14);
        Give(state, Sam, 2, 7, 17, 22);
        Claim(state, Alex, 12, "F5:A77");
        Assert.False(Claim(state, Sam, 12, "F5:A78")!.Accepted);
        Assert.Single(state.Winners);
        Assert.Null(state.PlaceOf(Sam));
    }

    [Fact]
    public void TiedSquare_KeepsTheOtherPlayersLineAlive_SoNoWrongClinch()
    {
        // 4×4, found by a script: if the tied square 3 counted only for Alex, Sam's last live line
        // (column 4: 3, 7, 11, 15) would be dead and Alex would clinch on 8 squares.
        var state = State(BingoMode.Lockout, 4, Alex, Sam, Kim);
        Claim(state, Alex, 3, "F2:A12");
        Claim(state, Sam, 3, "F2:A12");
        Give(state, Sam, 2, 6);
        Give(state, Kim, 10, 13);
        Give(state, Alex, 0, 1, 4, 5, 8, 9, 14);

        Assert.Null(state.Winner);
        Assert.Equal((8, 3), (state.FilledBy(Alex).Count, state.FilledBy(Sam).Count));
    }

    [Fact]
    public void Snapshot_ReplaysTies()
    {
        var host = State(BingoMode.Lockout, 5, Alex, Sam);
        Claim(host, Alex, 12, "F3:A40");
        Claim(host, Sam, 12, "F3:A40");

        var client = State(BingoMode.Lockout, 5, Alex, Sam);
        foreach (var result in host.Results)
        {
            client.Apply(result);
        }
        Assert.Equal([Alex, Sam], client.OwnersOf(12));
    }
}
