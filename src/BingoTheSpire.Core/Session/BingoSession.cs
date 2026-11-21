using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;

namespace BingoTheSpire.Core.Session;

/// <summary>
/// One run's bingo on one player's game (spec sections 4 and 7):
/// <list type="number">
/// <item>The goal tracker turns game events into detected goals.</item>
/// <item>Detections for the players this game speaks for become <see cref="Claim"/>s.</item>
/// <item>The authority (the solo game, or the co-op host) decides each claim, giving a
///   <see cref="ClaimResolved"/> that every game applies to its <see cref="BoardState"/> in order.</item>
/// </list>
/// Results are fed back through <see cref="Handle"/> like game events, so a journal of everything
/// handled can be replayed to restore the session exactly.
/// </summary>
public sealed class BingoSession
{
    private readonly Func<ulong, bool> _claimsFor;
    private long _lastSeq;

    /// <param name="isAuthority">True for solo and the co-op host: decides claims itself.</param>
    /// <param name="claimsFor">Whose detections this game claims. In co-op each game claims only for its own player; null claims for everyone (solo, tests).</param>
    public BingoSession(BingoBoard board, GoalList goals, bool isAuthority = true, Func<ulong, bool>? claimsFor = null,
        Action<string, Exception>? onDetectorError = null)
    {
        Board = board;
        IsAuthority = isAuthority;
        _claimsFor = claimsFor ?? (_ => true);
        Tracker = new GoalTracker(board, goals, onDetectorError);
        State = new BoardState(board);
        State.LineDone += OnLine;
        Tracker.Completed += OnCompleted;
    }

    public BingoBoard Board { get; }

    public GoalTracker Tracker { get; }

    public BoardState State { get; }

    public bool IsAuthority { get; }

    /// <summary>
    /// True while a journal is replayed. Detections then make no claims: the journal already has
    /// the results they led to.
    /// </summary>
    public bool Replaying { get; set; }

    /// <summary>Co-op client: a claim to send to the host.</summary>
    public event Action<Claim>? Claimed;

    /// <summary>Authority: a decision made. Record it (journal) and, in co-op, send it to everyone. It's applied right after.</summary>
    public event Action<ClaimResolved>? Resolved;

    /// <summary>Play time (ms since the run started, across Continues) of the last game event handled.</summary>
    public long PlayMs { get; private set; }

    /// <summary>Play time when each line was made, first time only (a host snapshot replays lines).</summary>
    public IReadOnlyDictionary<(ulong Player, int Line), long> LineTimes => _lineTimes;

    /// <summary>Where each line was made, from the claim that finished it.</summary>
    public IReadOnlyDictionary<(ulong Player, int Line), RunPosition?> LinePositions => _linePositions;

    private readonly Dictionary<(ulong, int), long> _lineTimes = new();
    private readonly Dictionary<(ulong, int), RunPosition?> _linePositions = new();

    private void OnLine(LineCompleted line)
    {
        _lineTimes.TryAdd((line.PlayerId, line.Line), PlayMs);
        _linePositions.TryAdd((line.PlayerId, line.Line), line.At);
    }

    public void Handle(GameEvent evt)
    {
        _lastSeq = Math.Max(_lastSeq, evt.Seq);
        if (evt is not ClaimResolved)
        {
            PlayMs = Math.Max(PlayMs, evt.Ms);
        }
        switch (evt)
        {
            case ClaimResolved result:
                State.Apply(result);
                return;
            case RunAttached run:
                State.SetPlayers(run.Players.Select(p => p.PlayerId));
                break;
        }
        Tracker.Handle(evt);
        if (evt is RunEnded)
        {
            State.Finish(evt.At);
        }
    }

    /// <summary>Authority: decides a claim (its own player's, or one a co-op client sent) and applies the result.</summary>
    public ClaimResolved? Decide(Claim claim)
    {
        if (!IsAuthority)
        {
            throw new InvalidOperationException("Only the authority decides claims.");
        }
        var result = State.Decide(claim, _lastSeq);
        if (result != null)
        {
            Resolved?.Invoke(result);
            State.Apply(result);
        }
        return result;
    }

    private void OnCompleted(GoalCompletion completion)
    {
        if (Replaying || !_claimsFor(completion.PlayerId))
        {
            return;
        }
        Submit(new Claim(completion.Square, completion.GoalId, completion.PlayerId, completion.Evidence, completion.At));
    }

    /// <summary>
    /// Claims a square for a player as if they had completed its goal, e.g. from the dev console.
    /// Goes through the normal rules: decided here by the authority, or sent to the host.
    /// </summary>
    /// <returns>False if the square can't be claimed (already the player's, or taken).</returns>
    public bool ClaimSquare(int square, ulong playerId, string evidence, RunPosition? at = null)
    {
        if (square < 0 || square >= Board.Squares.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(square));
        }
        return Submit(new Claim(square, Board.Squares[square].GoalId, playerId, evidence, at));
    }

    private bool Submit(Claim claim)
    {
        if (!State.CouldAccept(claim))
        {
            return false; // already ours, or someone else's in Lockout and Team
        }
        if (IsAuthority)
        {
            Decide(claim);
        }
        else
        {
            Claimed?.Invoke(claim);
        }
        return true;
    }
}
