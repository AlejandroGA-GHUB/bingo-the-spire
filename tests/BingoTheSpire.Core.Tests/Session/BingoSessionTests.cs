using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.Tests.Session;

public class BingoSessionTests
{
    private const ulong Alex = 1;
    private const ulong Sam = 2;

    private static GoalList Goals(int count) => new(1, Enumerable.Range(0, count)
        .Select(i => new GoalDefinition($"g{i}", "G", "CardCost", [new GoalTier(1, new Dictionary<string, int> { ["n"] = 100 + i })]))
        .ToList());

    private static BingoBoard Board(int size, BingoMode mode) => new(size, "TEST", 1, mode, BoardDifficulty.Medium, 0,
        Enumerable.Range(0, size * size)
            .Select(i => new BoardSquare(i, $"g{i}", 1, new Dictionary<string, int> { ["n"] = 100 + i }, "G"))
            .ToList());

    /// <summary>
    /// A 4×4 board where square i is "play a card costing 100+i energy". Playing a card that
    /// costs 100+k fills squares 0..k (row-major) for that player.
    /// </summary>
    private static BingoSession Session(BingoMode mode = BingoMode.Versus)
    {
        var session = new BingoSession(Board(4, mode), Goals(16));
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
        s.State.LineDone += lines.Add;

        // Thresholds fill every square up to i, so filling 3 fills squares 0-3: the top row.
        Fill(s, Alex, 3);
        var first = Assert.Single(lines);
        Assert.Equal((0, Alex, 1), (first.Line, first.PlayerId, first.Ordinal));
        Assert.Equal(new BingoWin(Alex, WinReason.Line, null), s.State.Winner);

        Fill(s, Alex, 7); // squares 4-7: the second row
        Assert.Equal(2, lines.Count);
        Assert.Equal((1, 2), (lines[1].Line, lines[1].Ordinal));
    }

    [Fact]
    public void Versus_PlayersHaveTheirOwnCopyOfTheBoard_AndArePlacedInOrder()
    {
        var s = Session();
        var lines = new List<LineCompleted>();
        s.State.LineDone += lines.Add;

        Fill(s, Alex, 2);
        Fill(s, Sam, 3);
        var line = Assert.Single(lines);
        Assert.Equal(Sam, line.PlayerId);
        Assert.Equal(3, s.State.FilledBy(Alex).Count);
        Assert.Equal(4, s.State.FilledBy(Sam).Count);

        Fill(s, Alex, 3);
        Assert.Equal([Sam, Alex], s.State.Placements);
        Assert.Equal(Sam, s.State.Winner?.PlayerId);
    }

    [Fact]
    public void OneCompletion_CanFinishSeveralLines()
    {
        var s = Session();
        var lines = new List<LineCompleted>();
        s.State.LineDone += lines.Add;
        Fill(s, Alex, 15); // everything: 4 rows, 4 columns, 2 diagonals
        Assert.Equal(10, lines.Count);
        Assert.Equal(Enumerable.Range(1, 10), lines.Select(l => l.Ordinal));
    }

    [Fact]
    public void Lockout_FirstClaimLocksTheSquare()
    {
        var s = Session(BingoMode.Lockout);
        var rejected = new List<ClaimResolved>();
        s.State.Rejected += rejected.Add;

        Fill(s, Alex, 1); // squares 0-1
        Fill(s, Sam, 2);  // 0-1 are Alex's; Sam only gets 2. Taken squares aren't even claimed.
        Assert.Equal([0, 1], s.State.FilledBy(Alex).Order());
        Assert.Equal([2], s.State.FilledBy(Sam));
        Assert.Equal(Alex, s.State.OwnerOf(0));
        Assert.Equal(Sam, s.State.OwnerOf(2));
        Assert.Empty(rejected);
    }

    [Fact]
    public void Team_AnyPlayerFillsTheSquareForEveryone()
    {
        var s = Session(BingoMode.Team);
        var lines = new List<LineCompleted>();
        s.State.LineDone += lines.Add;

        Fill(s, Alex, 1); // 0-1
        Fill(s, Sam, 3);  // 2-3, finishing the top row for the team
        Assert.Equal([0, 1, 2, 3], s.State.FilledFor(Alex).Order());
        Assert.Equal([0, 1, 2, 3], s.State.FilledFor(Sam).Order());
        Assert.Equal(Alex, s.State.OwnerOf(0));
        Assert.Equal(Sam, s.State.OwnerOf(3));
        var line = Assert.Single(lines);
        Assert.Equal((0, Sam, 1), (line.Line, line.PlayerId, line.Ordinal));
        Assert.Null(s.State.Winner);
    }

    [Fact]
    public void Client_SendsClaimsForItsOwnPlayerOnly_AndAppliesTheHostsResults()
    {
        var session = new BingoSession(Board(4, BingoMode.Lockout), Goals(16), isAuthority: false, claimsFor: p => p == Sam);
        session.Handle(new RunAttached("TEST", "Standard", false, 0, [new PlayerInfo(Alex, "IRONCLAD"), new PlayerInfo(Sam, "SILENT")], Sam));
        var sent = new List<Claim>();
        session.Claimed += sent.Add;

        Fill(session, Alex, 0); // the host claims this one for Alex itself
        Fill(session, Sam, 1);
        Assert.Equal([(0, Sam), (1, Sam)], sent.Select(c => (c.Square, c.PlayerId)));
        Assert.Empty(session.State.FilledBy(Sam)); // nothing until the host answers

        session.Handle(new ClaimResolved(1, 0, "g0", Alex, true, "e"));
        session.Handle(new ClaimResolved(2, 0, "g0", Sam, false, "e"));
        session.Handle(new ClaimResolved(3, 1, "g1", Sam, true, "e"));
        session.Handle(new ClaimResolved(3, 1, "g1", Sam, true, "e")); // the same result twice changes nothing
        Assert.Equal([1], session.State.FilledBy(Sam));
        Assert.Equal(Alex, session.State.OwnerOf(0));
        Assert.Equal(4, session.State.NextOrder);
    }

    [Fact]
    public void Host_DecidesClientClaims_InArrivalOrder()
    {
        var host = Session(BingoMode.Lockout);
        var resolved = new List<ClaimResolved>();
        host.Resolved += resolved.Add;

        Assert.True(host.Decide(new Claim(5, "g5", Sam, "e", null))!.Accepted);
        Assert.False(host.Decide(new Claim(5, "g5", Alex, "e", null))!.Accepted);
        Assert.Null(host.Decide(new Claim(5, "g5", Sam, "e", null)));  // Sam already has it: no result at all
        Assert.Null(host.Decide(new Claim(5, "g4", Alex, "e", null))); // wrong goal for the square: a different board
        Assert.Equal([1L, 2L], resolved.Select(r => r.Order));
    }

    [Fact]
    public void Snapshot_ReplacesWhateverTheClientHad()
    {
        var host = Session(BingoMode.Lockout);
        Fill(host, Alex, 1);
        Fill(host, Sam, 3);

        // The client's results drifted (e.g. kept one the host later dropped on Continue).
        var client = new BingoSession(Board(4, BingoMode.Lockout), Goals(16), isAuthority: false);
        client.Handle(new RunAttached("TEST", "Standard", false, 0, [new PlayerInfo(Alex, "IRONCLAD"), new PlayerInfo(Sam, "SILENT")], Sam));
        client.Handle(new ClaimResolved(1, 9, "g9", Sam, true, "stale"));

        var cleared = 0;
        client.State.Cleared += () => cleared++;
        client.State.Reset();
        foreach (var result in host.State.Results)
        {
            client.Handle(result);
        }
        Assert.Equal(1, cleared);
        Assert.Null(client.State.OwnerOf(9));
        Assert.Equal(host.State.Results, client.State.Results);
        Assert.Equal(host.State.NextOrder, client.State.NextOrder);
    }

    // ---- Lockout clinch (spec 4.3.1) ----------------------------------------------------------

    private static BoardState Lockout(int size, params ulong[] players)
    {
        var state = new BoardState(Board(size, BingoMode.Lockout));
        state.SetPlayers(players);
        return state;
    }

    private static BoardState Players(BingoMode mode, params ulong[] players)
    {
        var state = new BoardState(Board(5, mode));
        state.SetPlayers(players);
        return state;
    }

    private static void Give(BoardState state, ulong player, params int[] squares)
    {
        foreach (var square in squares)
        {
            state.Apply(state.Decide(new Claim(square, $"g{square}", player, "e", null))!);
        }
    }

    // The layouts below were checked by a script: nobody makes a line, and only the lines named stay open.

    [Fact]
    public void Clinch_TwoPlayers_ThirteenOfTwentyFive()
    {
        var state = Lockout(5, Alex, Sam);
        Give(state, Sam, 0, 6, 7, 12, 13, 15, 17, 19, 20, 22, 23, 24);
        Give(state, Alex, 1, 2, 3, 4, 5, 8, 9, 10, 11, 14, 16, 18);
        Assert.Null(state.Winner); // 12 each, one square left

        Give(state, Alex, 21); // Alex's 13th: Sam can reach 12 at most, and every line is blocked
        Assert.Equal(new BingoWin(Alex, WinReason.Clinch, null), state.Winner);
    }

    [Fact]
    public void Clinch_NotWhileAnOpponentStillHasAnOpenLine()
    {
        var state = Lockout(5, Alex, Sam);
        // Row 5 (squares 20-24) stays open for Sam; every other line has one of Alex's squares.
        Give(state, Sam, 1, 9, 13, 14, 15, 16);
        Give(state, Alex, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 17, 18, 19);
        // 13 vs 6 with 6 unclaimed: Sam can reach 12 at most...
        Assert.Null(state.Winner); // ...but row 5 is still live for Sam

        Give(state, Alex, 0); // not in row 5: still no clinch
        Assert.Null(state.Winner);
        Give(state, Alex, 20); // blocks row 5
        Assert.Equal(new BingoWin(Alex, WinReason.Clinch, null), state.Winner);
    }

    [Fact]
    public void Clinch_Blitz_EightOfSixteen_ReachedFirst()
    {
        var state = Lockout(4, Alex, Sam);
        Give(state, Alex, 0, 3, 6, 7, 8, 11, 12);
        Give(state, Sam, 2, 4, 5, 9, 10, 14, 15);
        Assert.Null(state.Winner);
        Give(state, Alex, 13); // Alex reaches 8 first; with one square left Sam can only tie at 8
        Assert.Equal(new BingoWin(Alex, WinReason.Clinch, null), state.Winner);
    }

    [Fact]
    public void Lockout_RunEndsWithoutAWinner_MostSquaresWins_TiesToWhoGotThereFirst()
    {
        var state = Lockout(5, Alex, Sam);
        Give(state, Sam, 0, 6);
        Give(state, Alex, 1, 7);
        state.Finish(null);
        Assert.Equal(new BingoWin(Sam, WinReason.MostSquares, null), state.Winner);
    }

    [Fact]
    public void Versus_RunEndsWithoutALine_MostSquaresWins_TiesToWhoGotThereFirst()
    {
        var state = Players(BingoMode.Versus, Alex, Sam);
        Give(state, Alex, 0, 6, 12);
        Give(state, Sam, 0, 6);
        Give(state, Sam, 12); // ties Alex at 3, but later
        Give(state, Sam, 18);
        Give(state, Alex, 18); // ties Sam at 4, but later
        state.Finish(null);
        Assert.Equal(new BingoWin(Sam, WinReason.MostSquares, null), state.Winner);
    }

    [Fact]
    public void Versus_FirstLineStillBeatsMostSquares()
    {
        var state = Players(BingoMode.Versus, Alex, Sam);
        Give(state, Sam, 0, 6, 12, 18, 2, 8, 14);
        Give(state, Alex, 0, 1, 2, 3, 4);
        state.Finish(null);
        Assert.Equal(new BingoWin(Alex, WinReason.Line, null), state.Winner);
    }

    [Theory]
    [InlineData(BingoMode.Versus)]
    [InlineData(BingoMode.Lockout)]
    public void Solo_RunEndsWithoutALine_NobodyWins(BingoMode mode)
    {
        var state = Players(mode, Alex);
        Give(state, Alex, 0, 6, 12);
        state.Finish(null);
        Assert.Null(state.Winner);
    }

    [Fact]
    public void Team_RunEnds_NobodyWins()
    {
        var state = Players(BingoMode.Team, Alex, Sam);
        Give(state, Alex, 0, 6);
        Give(state, Sam, 12);
        state.Finish(null);
        Assert.Null(state.Winner);
    }

    [Fact]
    public void Lockout_FirstLineWins()
    {
        var state = Lockout(5, Alex, Sam);
        Give(state, Sam, 5, 6, 7, 8);
        Give(state, Alex, 0, 1, 2, 3, 4);
        Assert.Equal(new BingoWin(Alex, WinReason.Line, null), state.Winner);
    }
}
