using System.Text;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.GameAdapter;

namespace BingoTheSpire.Mod;

/// <summary>
/// Builds the board when a run is attached and runs the bingo session over the run's events.
/// For now results go to the game log; the board UI comes next (spec milestone 5).
/// Anything going wrong turns bingo off for that run only.
/// </summary>
internal sealed class BingoRuntime
{
    public BingoSession? Session { get; private set; }

    /// <summary>Raised after a new board is built, before the run's first event reaches it.</summary>
    public event Action<BingoSession>? SessionStarted;

    public void OnEvent(GameEvent evt)
    {
        if (evt is RunAttached run)
        {
            Start(run);
        }
        if (Session is not { } session)
        {
            return;
        }
        session.Handle(evt);
        if (evt is RunEnded ended)
        {
            LogResult(session, ended);
        }
    }

    private void Start(RunAttached run)
    {
        Session = null;
        try
        {
            var available = Adapter.Sources.Where(s => s.Connected).SelectMany(s => s.Events).ToHashSet(StringComparer.Ordinal);
            var capabilities = new DetectionCapabilities(available, DetectorCatalog.Available(available));
            var request = new BoardRequest(
                SeedText: run.Seed,
                Mode: BingoMode.Versus,
                GameMode: run.GameMode,
                CharacterIds: run.Players.Select(p => p.CharacterId).ToList());
            var board = BoardBuilder.Build(BuiltInGoals.List, request, capabilities);

            var session = new BingoSession(board, BuiltInGoals.List,
                (goal, ex) => BingoLog.Error($"detector for '{goal}' failed and is off for this run: {ex}"));
            session.Filled += f => BingoLog.Info(
                $"square {Cell(board, f.Square)} done by player {f.PlayerId}: \"{board.Squares[f.Square].Text}\" ({f.Completion.Evidence}{Where(f.Completion.At)})");
            session.LineDone += l => BingoLog.Info(l.Ordinal == 1
                ? $"BINGO! player {l.PlayerId} completed {LineName(board.Size, l.Line)}{Where(l.At)}"
                : $"line #{l.Ordinal} for player {l.PlayerId}: {LineName(board.Size, l.Line)}{Where(l.At)}");

            Session = session;
            BingoLog.Info(Describe(board));
            SessionStarted?.Invoke(session);
        }
        catch (Exception ex)
        {
            BingoLog.Error($"couldn't build a board, bingo is off for this run: {ex.Message}");
        }
    }

    private static void LogResult(BingoSession session, RunEnded ended)
    {
        var sb = new StringBuilder($"run ended ({(ended.Victory ? "won" : ended.Abandoned ? "abandoned" : "lost")}). Bingo results:");
        foreach (var player in session.Tracker.Context.Players)
        {
            sb.Append($"\n  player {player}: {session.FilledBy(player).Count}/{session.Board.Squares.Count} squares, {session.LinesBy(player).Count} line(s)");
        }
        BingoLog.Info(sb.ToString());
    }

    private static string Describe(BingoBoard board)
    {
        var sb = new StringBuilder($"board {board.Code} ({board.Mode}, {board.Size}x{board.Size}, {board.Difficulty}):");
        for (var r = 0; r < board.Size; r++)
        {
            sb.Append("\n  ");
            for (var c = 0; c < board.Size; c++)
            {
                var square = board[r, c];
                sb.Append($"[{square.Difficulty}] {square.Text}");
                if (c < board.Size - 1)
                {
                    sb.Append("  |  ");
                }
            }
        }
        return sb.ToString();
    }

    private static string Cell(BingoBoard board, int index) => $"r{index / board.Size + 1}c{index % board.Size + 1}";

    private static string LineName(int size, int line) => line switch
    {
        _ when line < size => $"row {line + 1}",
        _ when line < size * 2 => $"column {line - size + 1}",
        _ when line == size * 2 => "the top-left diagonal",
        _ => "the top-right diagonal",
    };

    private static string Where(RunPosition? at) =>
        at == null ? "" : $", act {at.Act + 1} floor {at.Floor}" + (at.Round is { } r ? $" turn {r}" : "");
}
