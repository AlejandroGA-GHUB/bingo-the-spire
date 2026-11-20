using System.Globalization;
using System.Text;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Recording;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.GameAdapter;

namespace BingoTheSpire.Mod;

/// <summary>
/// Builds the board when a run is attached and runs the bingo session over the run's events.
/// Every event is also written to the run's journal, so Continue can bring progress back
/// (spec section 8). Anything going wrong turns bingo off for that run only.
/// </summary>
internal sealed class BingoRuntime
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private RunJournal? _journal;
    private string? _journalPath;
    private long _seqOffset;
    private long _msOffset;

    public BingoSession? Session { get; private set; }

    /// <summary>The player on this machine, whose board the UI shows.</summary>
    public ulong LocalPlayerId { get; private set; }

    /// <summary>Raised after a board is built (and, on Continue, its progress restored).</summary>
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
        // After a Continue, numbering carries on from the restored events.
        evt = evt with { Seq = evt.Seq + _seqOffset, Ms = evt.Ms + _msOffset };
        WriteJournal(evt);
        session.Handle(evt);
        if (evt is RunEnded ended)
        {
            LogResult(session, ended);
            CloseJournal(delete: true);
        }
    }

    private void Start(RunAttached run)
    {
        Session = null;
        CloseJournal(delete: false);
        _seqOffset = 0;
        _msOffset = 0;
        LocalPlayerId = run.LocalPlayerId ?? run.Players.FirstOrDefault()?.PlayerId ?? 0;
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
            var local = LocalPlayerId;
            var session = new BingoSession(board, BuiltInGoals.List,
                isAuthority: true,
                claimsFor: player => player == local,
                onDetectorError: (goal, ex) => BingoLog.Error($"detector for '{goal}' failed and is off for this run: {ex}"));
            session.Resolved += WriteJournal;

            // A Continue: replay the journal. It already holds the results, so nothing is claimed again.
            var kept = LoadJournal(run, board);
            session.Replaying = true;
            foreach (var evt in kept)
            {
                session.Handle(evt);
            }
            session.Replaying = false;
            if (kept.Count > 0)
            {
                _seqOffset = kept[^1].Seq;
                _msOffset = kept[^1].Ms;
            }
            StartJournal(run, board, kept);

            session.State.Filled += f => BingoLog.Info(
                $"square {Cell(board, f.Square)} done by player {f.PlayerId}: \"{board.Squares[f.Square].Text}\" ({f.Claim.Evidence}{Where(f.Claim.At)})");
            session.State.Rejected += r => BingoLog.Info(
                $"square {Cell(board, r.Square)} was already taken when player {r.PlayerId} completed it");
            session.State.Won += w => BingoLog.Info($"player {w.PlayerId} wins the bingo ({w.Reason}){Where(w.At)}");
            session.State.LineDone += l => BingoLog.Info(l.Ordinal == 1
                ? $"BINGO! player {l.PlayerId} completed {BoardRules.LineName(board.Size, l.Line).ToLowerInvariant()}{Where(l.At)}"
                : $"line #{l.Ordinal} for player {l.PlayerId}: {BoardRules.LineName(board.Size, l.Line).ToLowerInvariant()}{Where(l.At)}");

            Session = session;
            BingoLog.Info(Describe(board));
            if (kept.Count > 0)
            {
                BingoLog.Info($"continued run: restored {kept.Count} events, "
                    + string.Join(", ", session.State.Players.Select(p => $"player {p} has {session.State.FilledBy(p).Count} squares")));
            }
            SessionStarted?.Invoke(session);
        }
        catch (Exception ex)
        {
            CloseJournal(delete: false);
            BingoLog.Error($"couldn't build a board, bingo is off for this run: {ex.Message}");
        }
    }

    // ---- Journal -------------------------------------------------------------------------

    /// <summary>
    /// The events to restore when this run is a Continue: the journal's events up to the game's
    /// last save. Empty for a new run, or if the journal is for a different board.
    /// </summary>
    private static IReadOnlyList<GameEvent> LoadJournal(RunAttached run, BingoBoard board)
    {
        if (run.StartTime == 0)
        {
            BingoLog.Warn("the game didn't report when this run started, so its bingo progress can't be saved for Continue");
            return [];
        }
        var path = JournalPath(run);
        if (!File.Exists(path))
        {
            PruneOldJournals();
            return [];
        }
        try
        {
            using var reader = new StreamReader(path, Utf8);
            var (header, events) = RunJournal.Read(reader);
            if (header == null || header.BoardHash != BoardHash(board))
            {
                BingoLog.Warn($"saved bingo progress is for a different board ({header?.BoardCode ?? "unreadable"}), starting this board fresh");
                return [];
            }
            return RunJournal.UpToLastSave(events);
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't read saved bingo progress, starting this board fresh: {ex.Message}");
            return [];
        }
    }

    /// <summary>Rewrites the journal with the kept events, then keeps it open for new ones.</summary>
    private void StartJournal(RunAttached run, BingoBoard board, IReadOnlyList<GameEvent> kept)
    {
        if (run.StartTime == 0)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(DataPaths.Runs);
            var path = JournalPath(run);
            var header = new JournalHeader(JournalHeader.CurrentFormat, run.Seed, run.StartTime, board.Code, BoardHash(board));

            // Write to a temporary file first, so quitting mid-write can't lose the old journal.
            var temp = path + ".tmp";
            RunJournal.Start(new StreamWriter(temp, append: false, Utf8), header, kept).Dispose();
            File.Move(temp, path, overwrite: true);

            _journal = RunJournal.Reopen(new StreamWriter(path, append: true, Utf8));
            _journalPath = path;
        }
        catch (Exception ex)
        {
            CloseJournal(delete: false);
            BingoLog.Error($"couldn't save bingo progress for this run; Continue will start the board fresh: {ex.Message}");
        }
    }

    private void WriteJournal(GameEvent evt)
    {
        try
        {
            _journal?.Append(evt);
        }
        catch (Exception ex)
        {
            CloseJournal(delete: false);
            BingoLog.Error($"saving bingo progress failed and is off for this run: {ex.Message}");
        }
    }

    private void CloseJournal(bool delete)
    {
        try
        {
            _journal?.Dispose();
            if (delete && _journalPath != null && File.Exists(_journalPath))
            {
                File.Delete(_journalPath);
            }
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't close the bingo journal: {ex.Message}");
        }
        _journal = null;
        _journalPath = null;
    }

    /// <summary>
    /// Journals of runs that ended without the mod seeing it (e.g. abandoned from the main menu)
    /// are removed once they haven't been touched for a month.
    /// </summary>
    private static void PruneOldJournals()
    {
        try
        {
            if (!Directory.Exists(DataPaths.Runs))
            {
                return;
            }
            var cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(DataPaths.Runs, "*.jsonl*"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't clean up old bingo journals: {ex.Message}");
        }
    }

    private static string JournalPath(RunAttached run) => Path.Combine(DataPaths.Runs, RunJournal.FileName(run.Seed, run.StartTime));

    private static string BoardHash(BingoBoard board) => board.ContentHash().ToString("X16", CultureInfo.InvariantCulture);

    // ---- Log -----------------------------------------------------------------------------

    private static void LogResult(BingoSession session, RunEnded ended)
    {
        var sb = new StringBuilder($"run ended ({(ended.Victory ? "won" : ended.Abandoned ? "abandoned" : "lost")}). Bingo results:");
        foreach (var player in session.State.Players)
        {
            sb.Append($"\n  player {player}: {session.State.FilledBy(player).Count}/{session.Board.Squares.Count} squares, {session.State.LinesFor(player).Count} line(s)");
        }
        if (session.State.Winner is { } winner)
        {
            sb.Append($"\n  winner: player {winner.PlayerId} ({winner.Reason})");
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

    private static string Where(RunPosition? at) =>
        at == null ? "" : $", act {at.Act + 1} floor {at.Floor}" + (at.Round is { } r ? $" turn {r}" : "");
}
