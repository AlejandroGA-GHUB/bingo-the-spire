using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Core.Session;

/// <summary>A player's game detected a goal, and asks the authority (solo game or co-op host) for the square.</summary>
/// <param name="Moment">The shared moment it happened (see <see cref="GameEvent.Moment"/>), for Lockout ties.</param>
public sealed record Claim(int Square, string GoalId, ulong PlayerId, string Evidence, RunPosition? At, string? Moment = null);

/// <param name="Tie">Lockout: someone already had the square, and this player tied with them (spec 4.3.2).</param>
public sealed record SquareFilled(int Square, ulong PlayerId, ClaimResolved Claim, bool Tie = false);

/// <param name="Line">Index into <see cref="BoardRules.Lines"/>: rows, then columns, then the two diagonals.</param>
/// <param name="PlayerId">Who made the line. In Team, the player whose square finished it.</param>
/// <param name="Ordinal">1 for the first line (the bingo), 2 for the second, and so on: per player, or for the team in Team mode.</param>
public sealed record LineCompleted(int Line, ulong PlayerId, int Ordinal, RunPosition? At, long Order);

public enum WinReason
{
    /// <summary>Made the first line (Versus and Lockout).</summary>
    Line,

    /// <summary>Lockout: no other player can still win (spec 4.3.1).</summary>
    Clinch,

    /// <summary>Lockout: the run ended without a line or clinch, and this player had the most squares.</summary>
    MostSquares,
}

/// <param name="Draw">This player made their first line at the same moment as the winner, through a tied square, so they win too.</param>
public sealed record BingoWin(ulong PlayerId, WinReason Reason, RunPosition? At, bool Draw = false);

/// <summary>
/// The board as everyone sees it: the authority's results applied in order, with each mode's rules
/// for who has which square, lines and wins (spec section 4.3). Every player's game keeps one
/// and applies the same results in the same order, so they all agree.
/// </summary>
public sealed class BoardState
{
    private readonly IReadOnlyList<int[]> _lines;
    private readonly SortedSet<ulong> _players = [];
    private readonly HashSet<long> _applied = [];
    private readonly Dictionary<ulong, HashSet<int>> _filled = new();
    // Lockout and Team: who has each square, first claim first. Lockout ties add more players.
    private readonly Dictionary<int, List<ulong>> _owners = new();
    private readonly Dictionary<(int Square, ulong Player), ClaimResolved> _claims = new();
    private readonly Dictionary<ulong, List<int>> _playerLines = new();
    private readonly List<int> _teamLines = [];
    private readonly List<ulong> _placements = [];
    private readonly Dictionary<ulong, int> _place = new();
    private readonly Dictionary<ulong, (int Square, string? Moment)> _placedBy = new();
    private readonly List<BingoWin> _winners = [];

    // Lockout tie-breaks: the order at which each player reached each square count.
    private readonly Dictionary<ulong, List<long>> _reachedAt = new();

    public BoardState(BingoBoard board)
    {
        Board = board;
        _lines = BoardRules.Lines(board.Size);
    }

    public BingoBoard Board { get; private set; }

    /// <summary>For testing only: the board with one square's goal swapped (see <see cref="BingoSession.SwapGoal"/>).</summary>
    internal void ReplaceBoard(BingoBoard board) => Board = board;

    public BingoMode Mode => Board.Mode;

    /// <summary>Order number the authority gives its next result.</summary>
    public long NextOrder { get; private set; } = 1;

    /// <summary>Who won the bingo, once someone has (Versus and Lockout; Team has no winner).</summary>
    public BingoWin? Winner { get; private set; }

    /// <summary>The winner, plus anyone who drew with them (a Lockout tie that made both their first lines).</summary>
    public IReadOnlyList<BingoWin> Winners => _winners;

    /// <summary>Players in the order they made their first line.</summary>
    public IReadOnlyList<ulong> Placements => _placements;

    /// <summary>
    /// A player's place by first line: 1 for the first, and the same place for players who drew
    /// (then the next place is skipped, e.g. 1, 1, 3). Null if they made no line.
    /// </summary>
    public int? PlaceOf(ulong playerId) => _place.TryGetValue(playerId, out var place) ? place : null;

    public IReadOnlyCollection<ulong> Players => _players;

    public event Action<SquareFilled>? Filled;

    public event Action<ClaimResolved>? Rejected;

    public event Action<LineCompleted>? LineDone;

    public event Action<BingoWin>? Won;

    /// <summary>Every player in the run. Players with no squares yet still matter for Lockout's clinch rule.</summary>
    public void SetPlayers(IEnumerable<ulong> players)
    {
        foreach (var player in players)
        {
            _players.Add(player);
        }
    }

    /// <summary>Squares this player got themselves.</summary>
    public IReadOnlySet<int> FilledBy(ulong playerId) =>
        _filled.TryGetValue(playerId, out var set) ? set : new HashSet<int>();

    /// <summary>Squares shown as done on this player's board: their own, or the whole team's in Team mode.</summary>
    public IReadOnlySet<int> FilledFor(ulong playerId) =>
        Mode == BingoMode.Team ? _owners.Keys.ToHashSet() : FilledBy(playerId);

    /// <summary>Lockout and Team: who got the square first. Null in Versus, where every player has their own.</summary>
    public ulong? OwnerOf(int square) =>
        Mode != BingoMode.Versus && _owners.TryGetValue(square, out var owners) ? owners[0] : null;

    /// <summary>Lockout and Team: everyone who has the square, first claim first. More than one is a Lockout tie.</summary>
    public IReadOnlyList<ulong> OwnersOf(int square) =>
        Mode != BingoMode.Versus && _owners.TryGetValue(square, out var owners) ? owners : [];

    /// <summary>Lines shown on this player's board: their own, or the team's in Team mode.</summary>
    public IReadOnlyList<int> LinesFor(ulong playerId) =>
        Mode == BingoMode.Team ? _teamLines
        : _playerLines.TryGetValue(playerId, out var lines) ? lines : [];

    /// <summary>The result that gave this player the square, if they have it.</summary>
    public ClaimResolved? ClaimOf(int square, ulong playerId) => _claims.GetValueOrDefault((square, playerId));

    /// <summary>Whether a claim could still be accepted. Used to skip claims that can't win, before sending them.</summary>
    public bool CouldAccept(Claim claim) => Mode switch
    {
        BingoMode.Versus => !FilledBy(claim.PlayerId).Contains(claim.Square),
        BingoMode.Lockout => !_owners.ContainsKey(claim.Square) || IsTie(claim),
        _ => !_owners.ContainsKey(claim.Square),
    };

    /// <summary>
    /// Lockout (spec 4.3.2): the square is taken, but this claim comes from the same moment as the
    /// claim that took it, so the player tied and gets the square too.
    /// </summary>
    private bool IsTie(Claim claim) =>
        Mode == BingoMode.Lockout
        && claim.Moment != null
        && _owners.TryGetValue(claim.Square, out var owners)
        && !owners.Contains(claim.PlayerId)
        && _claims.TryGetValue((claim.Square, owners[0]), out var first)
        && first.Moment == claim.Moment;

    /// <summary>
    /// The authority's decision on a claim (not applied yet). Null when the player already has
    /// the square, e.g. a claim sent again after a reconnect, so nobody sees a pointless "taken".
    /// </summary>
    public ClaimResolved? Decide(Claim claim, long seq = 0)
    {
        if (claim.Square < 0 || claim.Square >= Board.Squares.Count
            || Board.Squares[claim.Square].GoalId != claim.GoalId
            || FilledBy(claim.PlayerId).Contains(claim.Square))
        {
            return null;
        }
        return new ClaimResolved(NextOrder, claim.Square, claim.GoalId, claim.PlayerId, CouldAccept(claim), claim.Evidence)
        {
            At = claim.At,
            Seq = seq,
            Moment = claim.Moment,
        };
    }

    /// <summary>Applies one of the authority's results. Applying the same result twice does nothing.</summary>
    public void Apply(ClaimResolved result)
    {
        if (!_applied.Add(result.Order))
        {
            return;
        }
        NextOrder = Math.Max(NextOrder, result.Order + 1);
        _players.Add(result.PlayerId);
        if (!result.Accepted)
        {
            Rejected?.Invoke(result);
            return;
        }

        var player = result.PlayerId;
        if (!_filled.TryGetValue(player, out var filled))
        {
            _filled[player] = filled = [];
        }
        if (!filled.Add(result.Square))
        {
            return;
        }
        var tie = false;
        if (Mode != BingoMode.Versus)
        {
            if (!_owners.TryGetValue(result.Square, out var owners))
            {
                _owners[result.Square] = owners = [];
            }
            tie = Mode == BingoMode.Lockout && owners.Count > 0;
            owners.Add(player);
        }
        _claims[(result.Square, player)] = result;
        if (!_reachedAt.TryGetValue(player, out var reached))
        {
            _reachedAt[player] = reached = [];
        }
        reached.Add(result.Order);
        Filled?.Invoke(new SquareFilled(result.Square, player, result, tie));

        CheckLines(result);
        if (Mode == BingoMode.Lockout && Winner == null)
        {
            CheckClinch(result);
        }
    }

    /// <summary>Raised by <see cref="Reset"/>: anything drawn from this state should be redrawn.</summary>
    public event Action? Cleared;

    /// <summary>
    /// Forgets every result (keeping the players), so a co-op client can apply the host's full
    /// list of results and end up exactly like the host.
    /// </summary>
    public void Reset()
    {
        _applied.Clear();
        _filled.Clear();
        _owners.Clear();
        _claims.Clear();
        _playerLines.Clear();
        _teamLines.Clear();
        _placements.Clear();
        _place.Clear();
        _placedBy.Clear();
        _winners.Clear();
        _reachedAt.Clear();
        NextOrder = 1;
        Winner = null;
        Cleared?.Invoke();
    }

    /// <summary>Every accepted result, in order, e.g. for a snapshot.</summary>
    public IReadOnlyList<ClaimResolved> Results =>
        _claims.Values.OrderBy(r => r.Order).ToList();

    /// <summary>
    /// The run ended. In Versus and Lockout, a co-op game with no winner yet (no line, no clinch)
    /// goes to the most squares. Team has no winner, and solo has nobody to beat.
    /// </summary>
    public void Finish(RunPosition? at)
    {
        if (Mode == BingoMode.Team || _players.Count < 2 || Winner != null || _filled.Count == 0)
        {
            return;
        }
        var most = _filled.Values.Max(s => s.Count);
        if (most == 0)
        {
            return;
        }
        // Ties go to whoever reached that count first.
        var winner = _filled.Where(kv => kv.Value.Count == most)
            .OrderBy(kv => _reachedAt[kv.Key][most - 1])
            .First().Key;
        Win(new BingoWin(winner, WinReason.MostSquares, at));
    }

    private void CheckLines(ClaimResolved result)
    {
        var player = result.PlayerId;
        IReadOnlySet<int> done;
        List<int> lines;
        if (Mode == BingoMode.Team)
        {
            done = _owners.Keys.ToHashSet();
            lines = _teamLines;
        }
        else
        {
            done = _filled[player];
            if (!_playerLines.TryGetValue(player, out lines!))
            {
                _playerLines[player] = lines = [];
            }
        }

        for (var i = 0; i < _lines.Count; i++)
        {
            if (lines.Contains(i) || !_lines[i].Contains(result.Square) || !_lines[i].All(done.Contains))
            {
                continue;
            }
            lines.Add(i);
            LineDone?.Invoke(new LineCompleted(i, player, lines.Count, result.At, result.Order));
            if (lines.Count == 1 && Mode != BingoMode.Team)
            {
                Place(player, result);
            }
        }
    }

    /// <summary>
    /// Spec 4.3.1: P clinches when, for every other player Q, Q can't pass P on squares
    /// (Q's squares + unclaimed ≤ P's) and Q has no line left that's only Q's or unclaimed.
    /// </summary>
    private void CheckClinch(ClaimResolved result)
    {
        if (_players.Count < 2)
        {
            return;
        }
        var unclaimed = Board.Squares.Count - _owners.Count;
        var candidates = _players
            .Where(p => Count(p) > 0 && _players.Where(q => q != p).All(q => Count(q) + unclaimed <= Count(p) && !HasLiveLine(q)))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }
        // Two players can only both qualify on a full board with equal counts: first to that count wins.
        var winner = candidates.OrderBy(p => _reachedAt[p][Count(p) - 1]).First();
        Win(new BingoWin(winner, WinReason.Clinch, result.At));
    }

    /// <summary>
    /// A player made their first line. If it came from a tied square, at the same moment as
    /// another player's first line, they share that player's place, and a win too (spec 4.3.2).
    /// </summary>
    private void Place(ulong player, ClaimResolved result)
    {
        ulong? drewWith = null;
        if (result.Moment != null)
        {
            foreach (var other in _placements)
            {
                if (_placedBy[other] == (result.Square, result.Moment))
                {
                    drewWith = other;
                    break;
                }
            }
        }
        var place = drewWith is { } d ? _place[d] : _placements.Count + 1;
        _placements.Add(player);
        _place[player] = place;
        _placedBy[player] = (result.Square, result.Moment);

        if (Winner == null)
        {
            Win(new BingoWin(player, WinReason.Line, result.At));
        }
        else if (drewWith is { } rival && _winners.Any(w => w.PlayerId == rival && w.Reason == WinReason.Line))
        {
            Win(new BingoWin(player, WinReason.Line, result.At, Draw: true));
        }
    }

    private int Count(ulong player) => _filled.TryGetValue(player, out var set) ? set.Count : 0;

    /// <summary>A line only this player's or unclaimed squares are in. A tied square counts for each player who has it.</summary>
    private bool HasLiveLine(ulong player) =>
        _lines.Any(line => line.All(square => !_owners.TryGetValue(square, out var owners) || owners.Contains(player)));

    private void Win(BingoWin win)
    {
        Winner ??= win;
        _winners.Add(win);
        Won?.Invoke(win);
    }
}
