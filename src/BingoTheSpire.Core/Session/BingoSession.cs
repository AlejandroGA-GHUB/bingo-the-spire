using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;

namespace BingoTheSpire.Core.Session;

public sealed record SquareFilled(int Square, ulong PlayerId, GoalCompletion Completion);

/// <param name="Line">Index into <see cref="BoardRules.Lines"/>: rows, then columns, then the two diagonals.</param>
/// <param name="Ordinal">1 for the player's first line (their bingo), 2 for the second, and so on.</param>
public sealed record LineCompleted(int Line, ulong PlayerId, int Ordinal, RunPosition? At, long Seq);

/// <summary>
/// One run's bingo, solo or Versus: every player fills their own copy of the board, and each
/// player's lines are tracked in the order they're made (spec sections 4.2 and 4.3).
/// Lockout and Team, where the host decides who gets a square, build on this later.
/// </summary>
public sealed class BingoSession
{
    private readonly IReadOnlyList<int[]> _lines;
    private readonly Dictionary<ulong, HashSet<int>> _filled = new();
    private readonly Dictionary<ulong, List<int>> _completedLines = new();

    public BingoSession(BingoBoard board, GoalList goals, Action<string, Exception>? onDetectorError = null)
    {
        Board = board;
        Tracker = new GoalTracker(board, goals, onDetectorError);
        _lines = BoardRules.Lines(board.Size);
        Tracker.Completed += OnCompleted;
    }

    public BingoBoard Board { get; }

    public GoalTracker Tracker { get; }

    public event Action<SquareFilled>? Filled;

    public event Action<LineCompleted>? LineDone;

    public IReadOnlySet<int> FilledBy(ulong playerId) =>
        _filled.TryGetValue(playerId, out var set) ? set : new HashSet<int>();

    public IReadOnlyList<int> LinesBy(ulong playerId) =>
        _completedLines.TryGetValue(playerId, out var lines) ? lines : [];

    public void Handle(GameEvent evt) => Tracker.Handle(evt);

    private void OnCompleted(GoalCompletion completion)
    {
        var player = completion.PlayerId;
        if (!_filled.TryGetValue(player, out var filled))
        {
            _filled[player] = filled = [];
        }
        if (!filled.Add(completion.Square))
        {
            return;
        }
        Filled?.Invoke(new SquareFilled(completion.Square, player, completion));

        if (!_completedLines.TryGetValue(player, out var done))
        {
            _completedLines[player] = done = [];
        }
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!done.Contains(i) && _lines[i].Contains(completion.Square) && _lines[i].All(filled.Contains))
            {
                done.Add(i);
                LineDone?.Invoke(new LineCompleted(i, player, done.Count, completion.At, completion.Seq));
            }
        }
    }
}
