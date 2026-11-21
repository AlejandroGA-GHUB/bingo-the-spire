using System.Globalization;
using System.Text;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.History;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Recording;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.GameAdapter;
using BingoTheSpire.Mod.Net;

namespace BingoTheSpire.Mod;

/// <summary>
/// Runs one run's bingo on this game:
/// <list type="bullet">
/// <item>Builds the board when a run is attached, from the player's (or co-op host's) setup, or
///   on Continue from the run's journal so it's the same board as before.</item>
/// <item>Feeds the run's events to the session and writes everything to the journal.</item>
/// <item>In co-op, sends claims to the host (client) or decides everyone's claims and sends the
///   results (host). A client asks the host for a snapshot whenever its run starts, and its board
///   becomes exactly the host's (spec section 7).</item>
/// </list>
/// Anything going wrong turns bingo off for that run only.
/// </summary>
internal sealed class BingoRuntime
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly BingoSettings _settings;
    private readonly List<(ulong Player, string Hash)> _pendingHellos = [];
    private readonly List<GameEvent> _liveEvents = [];

    private RunAttached? _run;
    private NetRole _role;
    private bool _disabled;
    private RunJournal? _journal;
    private string? _journalPath;
    private long _seqOffset;
    private long _msOffset;

    public BingoRuntime(BingoSettings settings)
    {
        _settings = settings;
    }

    public BingoSession? Session { get; private set; }

    /// <summary>The player on this machine, whose board the UI shows.</summary>
    public ulong LocalPlayerId { get; private set; }

    /// <summary>Raised after a board is built (and, on Continue, its progress restored).</summary>
    public event Action<BingoSession>? SessionStarted;

    /// <summary>Raised when bingo turns off partway, e.g. the co-op host has it off.</summary>
    public event Action? SessionStopped;

    /// <summary>A bingo run ended and was written to the history: show the results.</summary>
    public event Action<HistoryEntry>? RunFinished;

    public void OnEvent(GameEvent evt)
    {
        if (evt is RunAttached run)
        {
            Start(run);
        }
        // Kept even while bingo is off, in case the co-op host's snapshot turns it on.
        _liveEvents.Add(evt);
        if (Session is not { } session)
        {
            if (evt is RunEnded)
            {
                CloseJournal(delete: true);
            }
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
            SaveHistory(session, ended);
        }
    }

    // ---- Starting a run ------------------------------------------------------------------

    private void Start(RunAttached run)
    {
        StopSession(notify: false);
        _run = run;
        _disabled = false;
        _liveEvents.Clear();
        LocalPlayerId = run.LocalPlayerId ?? run.Players.FirstOrDefault()?.PlayerId ?? 0;
        BingoNet.Use(Adapter.CurrentNetService());
        _role = BingoNet.Role;

        try
        {
            var (header, events) = ReadJournal(run);
            BoardRequest? request;
            if (header != null)
            {
                // Continue: the board this run was started with, whatever the lobby says now.
                request = header.Enabled && BoardCode.TryParse(header.BoardCode, out var code) && code != null
                    ? RequestFromCode(run, code)
                    : null;
            }
            else
            {
                var setup = _role == NetRole.Client ? BingoNet.HostSetup ?? BingoSetup.Default : _settings.Setup;
                request = setup.Enabled
                    ? setup.RequestFor(run.Seed, run.GameMode, Characters(run), coop: _role != NetRole.Solo, BuiltInGoals.List.Version)
                    : null;
                if (_role != NetRole.Client)
                {
                    _settings.ClearSeedEntry();
                }
            }

            if (request == null)
            {
                _disabled = true;
                StartJournal(run, null, []);
                BingoLog.Info("bingo is off for this run");
            }
            else
            {
                var board = BoardBuilder.Build(BuiltInGoals.List, request, Capabilities());
                var kept = header != null && header.BoardHash == BoardHash(board) ? RunJournal.UpToLastSave(events) : [];
                if (header != null && kept.Count == 0 && events.Count > 0)
                {
                    BingoLog.Warn("saved bingo progress doesn't match this board; starting it fresh");
                }
                StartSession(run, board, kept);
            }
        }
        catch (Exception ex)
        {
            _disabled = true;
            CloseJournal(delete: false);
            BingoLog.Error($"couldn't build a board, bingo is off for this run: {ex.Message}");
        }

        if (_role == NetRole.Client)
        {
            BingoNet.SendHello(Session is { } s ? BoardHash(s.Board) : "");
        }
        AnswerHellos();
    }

    private void StartSession(RunAttached run, BingoBoard board, IReadOnlyList<GameEvent> kept)
    {
        var local = LocalPlayerId;
        var session = new BingoSession(board, BuiltInGoals.List,
            isAuthority: _role != NetRole.Client,
            claimsFor: player => player == local,
            onDetectorError: (goal, ex) => BingoLog.Error($"detector for '{goal}' failed and is off for this run: {ex}"));
        session.Resolved += result =>
        {
            WriteJournal(result);
            BingoNet.SendResult(result);
        };
        session.Claimed += BingoNet.SendClaim;

        // A Continue: replay the journal. It already holds the results, so nothing is claimed again.
        session.Replaying = true;
        foreach (var evt in kept)
        {
            session.Handle(evt);
        }
        session.Replaying = false;
        _seqOffset = kept.Count > 0 ? kept[^1].Seq : 0;
        _msOffset = kept.Count > 0 ? kept[^1].Ms : 0;
        StartJournal(run, board, kept);

        LogBoardEvents(session);
        Session = session;
        BingoLog.Info(Describe(board) + $"\n  playing as {_role}");
        if (kept.Count > 0)
        {
            BingoLog.Info($"continued run: restored {kept.Count} events, "
                + string.Join(", ", session.State.Players.Select(p => $"player {p} has {session.State.FilledBy(p).Count} squares")));
        }
        SessionStarted?.Invoke(session);
    }

    private void StopSession(bool notify)
    {
        CloseJournal(delete: false);
        Session = null;
        _seqOffset = 0;
        _msOffset = 0;
        if (notify)
        {
            SessionStopped?.Invoke();
        }
    }

    // ---- Co-op ---------------------------------------------------------------------------

    /// <summary>Host: a client's run started and it wants the board.</summary>
    public void OnHello(ulong player, string hash, string modVersion)
    {
        if (_role != NetRole.Host && BingoNet.Role != NetRole.Host)
        {
            return;
        }
        if (modVersion != ModEntry.Version)
        {
            BingoLog.Warn($"player {player} has Bingo the Spire v{modVersion}, the host has v{ModEntry.Version}");
        }
        _pendingHellos.Add((player, hash));
        AnswerHellos();
    }

    /// <summary>Host: answers hellos once this game's own board for the run exists.</summary>
    private void AnswerHellos()
    {
        if (_role != NetRole.Host || (Session == null && !_disabled))
        {
            return;
        }
        foreach (var (player, hash) in _pendingHellos)
        {
            if (Session is { } session)
            {
                var ours = BoardHash(session.Board);
                if (hash.Length > 0 && hash != ours)
                {
                    BingoLog.Info($"player {player} built a different board; sending them ours ({session.Board.Code})");
                }
                BingoNet.SendSnapshot(player, true, session.Board.Code, ours, session.State.Results.ToList());
            }
            else
            {
                BingoNet.SendSnapshot(player, false, "", "", []);
            }
        }
        _pendingHellos.Clear();
    }

    /// <summary>Client: the host's whole board. This game's board becomes exactly the host's.</summary>
    public void OnSnapshot(bool enabled, string boardCode, string boardHash, List<ClaimResolved> results)
    {
        if (_role != NetRole.Client || _run is not { } run)
        {
            return;
        }
        if (!enabled)
        {
            if (Session != null || !_disabled)
            {
                BingoLog.Info("the host has bingo off for this run");
                StopSession(notify: true);
                _disabled = true;
                StartJournal(run, null, []);
            }
            return;
        }

        if (Session == null || BoardHash(Session.Board) != boardHash)
        {
            if (!BoardCode.TryParse(boardCode, out var code) || code == null)
            {
                BingoLog.Error($"the host sent a board code this game can't read ({boardCode}); bingo is off for this run");
                return;
            }
            var board = BoardBuilder.Build(BuiltInGoals.List, RequestFromCode(run, code), Capabilities());
            if (BoardHash(board) != boardHash)
            {
                BingoLog.Error($"this game can't build the host's board {boardCode} (different goals available?); bingo is off for this run");
                StopSession(notify: true);
                _disabled = true;
                return;
            }
            BingoLog.Info($"switching to the host's board {boardCode}");
            StopSession(notify: false);
            _disabled = false;
            StartSession(run, board, []);
            // Replay this run's events on the host's board: what's done so far gets claimed now,
            // and the host ignores claims for squares the player already has.
            foreach (var evt in _liveEvents.Where(e => e is not ClaimResolved and not RunAttached).Prepend(run))
            {
                WriteJournal(evt);
                Session!.Handle(evt);
            }
        }

        var session = Session!;
        session.State.Reset();
        foreach (var result in results.OrderBy(r => r.Order))
        {
            WriteJournal(result);
            session.Handle(result);
        }
        BingoLog.Info($"board synced with the host: {results.Count} squares taken");
    }

    /// <summary>Host: a client's player completed a goal.</summary>
    public void OnRemoteClaim(ulong sender, Claim claim)
    {
        if (_role != NetRole.Host || Session is not { } session)
        {
            return;
        }
        if (claim.PlayerId != sender)
        {
            BingoLog.Warn($"ignored a claim from player {sender} for player {claim.PlayerId}");
            return;
        }
        session.Decide(claim);
    }

    /// <summary>Client: the host decided a claim.</summary>
    public void OnRemoteResult(ClaimResolved result)
    {
        if (_role != NetRole.Client || Session is not { } session)
        {
            return;
        }
        WriteJournal(result);
        session.Handle(result);
    }

    // ---- Journal -------------------------------------------------------------------------

    /// <summary>This run's journal, if one exists (a Continue or a rejoin).</summary>
    private (JournalHeader? Header, List<GameEvent> Events) ReadJournal(RunAttached run)
    {
        if (run.StartTime == 0)
        {
            BingoLog.Warn("the game didn't report when this run started, so its bingo progress can't be saved for Continue");
            return (null, []);
        }
        var path = JournalPath(run);
        if (!File.Exists(path))
        {
            PruneOldJournals();
            return (null, []);
        }
        try
        {
            using var reader = new StreamReader(path, Utf8);
            return RunJournal.Read(reader);
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't read saved bingo progress, starting this board fresh: {ex.Message}");
            return (null, []);
        }
    }

    /// <summary>Rewrites the journal with the kept events, then keeps it open for new ones. A null board records "bingo off".</summary>
    private void StartJournal(RunAttached run, BingoBoard? board, IReadOnlyList<GameEvent> kept)
    {
        CloseJournal(delete: false);
        if (run.StartTime == 0)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(DataPaths.Runs);
            var path = JournalPath(run);
            var header = new JournalHeader(JournalHeader.CurrentFormat, run.Seed, run.StartTime,
                board?.Code ?? "", board != null ? BoardHash(board) : "", Enabled: board != null);

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

    /// <summary>One journal per run and player, so two games sharing a data folder never collide.</summary>
    private string JournalPath(RunAttached run) =>
        Path.Combine(DataPaths.Runs, RunJournal.FileName($"{run.Seed}_{LocalPlayerId}", run.StartTime));

    // ---- Helpers -------------------------------------------------------------------------

    private static BoardRequest RequestFromCode(RunAttached run, BoardCode.Parsed code) =>
        new(code.SeedText, code.Mode, run.GameMode, Characters(run), code.Difficulty, code.Size);

    private static List<string> Characters(RunAttached run) => run.Players.Select(p => p.CharacterId).ToList();

    private static DetectionCapabilities Capabilities()
    {
        var available = Adapter.Sources.Where(s => s.Connected).SelectMany(s => s.Events).ToHashSet(StringComparer.Ordinal);
        return new DetectionCapabilities(available, DetectorCatalog.Available(available));
    }

    private static string BoardHash(BingoBoard board) => board.ContentHash().ToString("X16", CultureInfo.InvariantCulture);

    private static void LogBoardEvents(BingoSession session)
    {
        var board = session.Board;
        session.State.Filled += f => BingoLog.Info(
            $"square {Cell(board, f.Square)} done by player {f.PlayerId}: \"{board.Squares[f.Square].Text}\" ({f.Claim.Evidence}{Where(f.Claim.At)})");
        session.State.Rejected += r => BingoLog.Info(
            $"square {Cell(board, r.Square)} was already taken when player {r.PlayerId} completed it");
        session.State.Won += w => BingoLog.Info($"player {w.PlayerId} wins the bingo ({w.Reason}){Where(w.At)}");
        session.State.LineDone += l => BingoLog.Info(l.Ordinal == 1
            ? $"BINGO! player {l.PlayerId} completed {BoardRules.LineName(board.Size, l.Line).ToLowerInvariant()}{Where(l.At)}"
            : $"line #{l.Ordinal} for player {l.PlayerId}: {BoardRules.LineName(board.Size, l.Line).ToLowerInvariant()}{Where(l.At)}");
    }

    private void SaveHistory(BingoSession session, RunEnded ended)
    {
        if (_run is not { } run)
        {
            return;
        }
        try
        {
            var entry = RunHistory.Record(session, run, ended, LocalPlayerId, Adapter.PlayerName, DateTimeOffset.Now);
            Directory.CreateDirectory(DataPaths.Root);
            File.AppendAllText(DataPaths.History, RunHistory.Serialize(entry) + Environment.NewLine);
            RunFinished?.Invoke(entry);
        }
        catch (Exception ex)
        {
            BingoLog.Error($"couldn't save the run to the bingo history: {ex.Message}");
        }
    }

    /// <summary>The results as they'd be recorded if the run ended now (dev console preview; not saved).</summary>
    public HistoryEntry? PreviewResult() =>
        Session is { } session && _run is { } run
            ? RunHistory.Record(session, run, new RunEnded(false, false), LocalPlayerId, Adapter.PlayerName, DateTimeOffset.Now)
            : null;

    /// <summary>Every finished run in the history file, oldest first. Empty if there's none yet.</summary>
    public static List<HistoryEntry> ReadHistory()
    {
        try
        {
            return File.Exists(DataPaths.History) ? RunHistory.Parse(File.ReadLines(DataPaths.History)) : [];
        }
        catch (Exception ex)
        {
            BingoLog.Error($"couldn't read the bingo history: {ex.Message}");
            return [];
        }
    }

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
