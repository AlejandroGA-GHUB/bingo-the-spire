using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;

namespace BingoTheSpire.Core.Detection;

/// <summary>
/// Runs one detector per board square over the event stream and reports completions and
/// progress per player. It only detects: what a completion means for the board (lockout, team,
/// wins) is decided by the mode rules.
/// </summary>
public sealed class GoalTracker
{
    private readonly Square[] _squares;
    private readonly DetectionContext _context = new();

    public GoalTracker(BingoBoard board, GoalList goals, Action<string, Exception>? onDetectorError = null)
    {
        var byId = goals.Goals.ToDictionary(g => g.Id, StringComparer.Ordinal);
        _squares = board.Squares.Select(square =>
        {
            if (!byId.TryGetValue(square.GoalId, out var goal))
            {
                throw new InvalidOperationException($"goal '{square.GoalId}' is not in goal list v{goals.Version}");
            }
            var detector = DetectorCatalog.Create(goal.Detector, square.Params, team: board.Mode == BingoMode.Team);
            return new Square(this, square.Index, goal.Id, detector, DetectorCatalog.Lasting(goal.Detector));
        }).ToArray();
        OnDetectorError = onDetectorError;
    }

    public event Action<GoalCompletion>? Completed;

    public event Action<GoalProgress>? Progressed;

    public DetectionContext Context => _context;

    private Action<string, Exception>? OnDetectorError { get; }

    public bool IsCompleted(int square, ulong playerId) => _squares[square].Done.Contains(playerId);

    /// <summary>The latest progress on every unfinished square for a player, e.g. to draw a restored board.</summary>
    public IReadOnlyList<GoalProgress> ProgressOf(ulong playerId) =>
        _squares.Select((s, i) => (s, i))
            .Where(x => !x.s.Done.Contains(playerId) && x.s.LastProgress.ContainsKey(playerId))
            .Select(x => new GoalProgress(x.i, playerId, x.s.LastProgress[playerId].Current, x.s.LastProgress[playerId].Target))
            .ToList();

    public void Handle(GameEvent evt)
    {
        _context.Before(evt);
        foreach (var square in _squares)
        {
            if (square.Broken)
            {
                continue;
            }
            square.Current = evt;
            try
            {
                square.Detector.Handle(evt, _context, square);
            }
            catch (Exception ex)
            {
                // One broken detector must not stop the others, or the game.
                square.Broken = true;
                OnDetectorError?.Invoke(square.GoalId, ex);
            }
        }
        _context.After(evt);

        // Turn, fight and one-moment goals only show progress while it can still count.
        if (evt is CombatEnded or RoomEntered)
        {
            foreach (var square in _squares.Where(s => !s.Lasting))
            {
                foreach (var (player, progress) in square.LastProgress.ToList())
                {
                    if (progress.Current > 0)
                    {
                        square.Progress(player, 0, progress.Target);
                    }
                }
            }
        }
    }

    private sealed class Square(GoalTracker tracker, int index, string goalId, IDetector detector, bool lasting) : IDetectionSink
    {
        /// <summary>Progress builds up over the run, so it stays shown between fights.</summary>
        public bool Lasting { get; } = lasting;

        public Dictionary<ulong, (int Current, int Target)> LastProgress { get; } = new();

        public string GoalId { get; } = goalId;

        public IDetector Detector { get; } = detector;

        public HashSet<ulong> Done { get; } = [];

        public bool Broken { get; set; }

        public GameEvent? Current { get; set; }

        public void Complete(ulong playerId, string evidence)
        {
            if (!Done.Add(playerId))
            {
                return;
            }
            tracker.Completed?.Invoke(new GoalCompletion(index, GoalId, playerId, evidence, Current?.At, Current?.Seq ?? 0));
        }

        public void Progress(ulong playerId, int current, int target)
        {
            if (Done.Contains(playerId)
                || (LastProgress.TryGetValue(playerId, out var last) && last == (current, target)))
            {
                return;
            }
            LastProgress[playerId] = (current, target);
            tracker.Progressed?.Invoke(new GoalProgress(index, playerId, current, target));
        }
    }
}
