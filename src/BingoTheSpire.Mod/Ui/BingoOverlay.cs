using BingoTheSpire.Core.History;
using Godot;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.GameAdapter;
using BingoTheSpire.Mod.Net;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The in-run bingo UI: the mini-board icon, the full board and the toasts. Built only from
/// Godot's built-in controls (no custom node classes, so nothing needs registering with Godot)
/// and added under the run's scene, so the game frees it when the run ends.
/// </summary>
internal sealed class BingoOverlay
{
    private readonly CanvasLayer _layer;
    private readonly Control _root;
    private readonly BingoIcon _icon;
    private readonly BoardPanel _board;
    private readonly Toasts _toasts;

    private BingoSession? _session;
    /// <summary>Lines made this session, for the feed.</summary>
    private readonly List<LineCompleted> _lines = new();
    private ulong _localPlayer;
    /// <summary>Whose board is open: yours, or (Versus) a player picked from the previews.</summary>
    private ulong _viewing;

    // True while the host's snapshot is applied: its squares are drawn, not announced.
    private bool _muted;

    private BingoOverlay(Node runNode, BingoSettings settings)
    {
        _layer = new CanvasLayer { Name = "BingoTheSpireOverlay", Layer = 90 };
        runNode.AddChild(_layer);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);
        _root = root;

        _board = new BoardPanel(root);
        _toasts = new Toasts(root);
        _icon = new BingoIcon(root, settings, Toggle);
        _icon.Root.Visible = false; // until there's a board: bingo can be off for the run

        // The top bar finishes its layout over the first frames; place the icon once it has.
        _layer.GetTree().CreateTimer(0.3).Timeout += PlaceIcon;
        var viewport = root.GetViewport();
        viewport.SizeChanged += PlaceIcon;
        _layer.TreeExiting += () =>
        {
            viewport.SizeChanged -= PlaceIcon;
            Unbind();
            // The icon may live in the game's top bar rather than our layer.
            if (GodotObject.IsInstanceValid(_icon.Root) && !_layer.IsAncestorOf(_icon.Root))
            {
                _icon.Root.QueueFree();
            }
        };
    }

    public static BingoOverlay? Current { get; private set; }

    /// <summary>Warnings for the player, held until a run's UI exists to show them.</summary>
    private static readonly List<(string Title, string Text)> PendingNotices = new();

    private static readonly Color Warning = Color.FromHtml("#f0a030");

    /// <summary>Tells the player something went wrong (or changed), as a toast in the run.</summary>
    public static void Notify(string title, string text)
    {
        if (PendingNotices.Contains((title, text)))
        {
            return;
        }
        PendingNotices.Add((title, text));
        if (Current is { } overlay && GodotObject.IsInstanceValid(overlay._layer) && overlay._layer.IsInsideTree())
        {
            overlay.FlushNotices(0.2);
        }
    }

    /// <summary>Shows waiting notices once the run screen has settled.</summary>
    private void FlushNotices(double delay)
    {
        _layer.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(_layer))
            {
                return;
            }
            foreach (var (title, text) in PendingNotices)
            {
                _toasts.Notice(title, text, Warning);
            }
            PendingNotices.Clear();
        };
    }

    /// <summary>Creates the UI for a run that just appeared on screen.</summary>
    public static void Attach(Node runNode, BingoRuntime runtime, BingoSettings settings)
    {
        Current?.Unbind();
        var overlay = new BingoOverlay(runNode, settings);
        Current = overlay;

        try
        {
            BingoHotkey.Configure(settings.Hotkey);
            BingoHotkey.Bind(overlay.Toggle);
        }
        catch (Exception ex)
        {
            // The icon still opens the board.
            BingoLog.Error($"couldn't set up the board hotkey: {ex.Message}");
        }
        var key = BingoHotkey.ActiveKey?.ToString();
        overlay._icon.SetTooltip(key);
        var pad = Gamepad.BoardButtonName;
        overlay._board.SetFooter(key != null ? $"{key}{(pad != null ? $" or {pad}" : "")} to show or hide"
            : pad != null ? $"{pad} to show or hide" : "Hotkey off (Settings in the lobby)");
        overlay.FlushNotices(1.5);

        if (runtime.Session != null)
        {
            overlay.Bind(runtime.Session, runtime.LocalPlayerId);
        }
    }

    /// <summary>Shows a new session's board (a new run, or the same run after Continue).</summary>
    public void Bind(BingoSession session, ulong localPlayer)
    {
        if (!GodotObject.IsInstanceValid(_layer))
        {
            return;
        }
        Unbind();
        _session = session;
        _localPlayer = localPlayer;
        _viewing = localPlayer;
        _lines.Clear();
        session.State.Filled += OnFilled;
        session.State.LineDone += OnLine;
        session.State.Cleared += OnCleared;
        session.State.Rejected += OnRejected;
        session.State.Won += OnWon;
        session.Tracker.Progressed += OnProgress;
        _icon.Root.Visible = true;
        Redraw();
    }

    /// <summary>Stops showing a board: the run ended, or bingo was turned off partway.</summary>
    public void Unbind()
    {
        if (GodotObject.IsInstanceValid(_layer))
        {
            _icon.Root.Visible = false;
            _board.Hide();
        }
        if (_session == null)
        {
            return;
        }
        _session.State.Filled -= OnFilled;
        _session.State.LineDone -= OnLine;
        _session.State.Cleared -= OnCleared;
        _session.State.Rejected -= OnRejected;
        _session.State.Won -= OnWon;
        _session.Tracker.Progressed -= OnProgress;
        _session = null;
    }

    /// <summary>
    /// Draws the board as it stands, without toasts: a new board, a continued run with its progress
    /// restored, or a co-op client's board replaced by the host's snapshot.
    /// </summary>
    private void Redraw()
    {
        if (_session is not { } session)
        {
            return;
        }
        var local = _viewing;
        // Solo boards are Versus under the hood; call them what they are.
        var solo = BingoNet.Role == NetRole.Solo || session.State.Players.Count < 2;
        _board.Build(session.Board, solo ? "Solo" : session.Board.Mode.ToString());
        foreach (var square in Enumerable.Range(0, session.Board.Squares.Count))
        {
            var by = session.State.OwnerOf(square) ?? local;
            if (session.State.ClaimOf(square, by) is { } c)
            {
                _board.SetEvidence(square, Evidence(c));
            }
        }
        foreach (var progress in session.Tracker.ProgressOf(local))
        {
            _board.SetProgress(progress.Square, progress.Current, progress.Target);
        }
        Refresh();
    }

    private void OnCleared()
    {
        _muted = true;
        Deferred(() =>
        {
            _muted = false;
            Redraw();
        });
    }

    private void Toggle()
    {
        if (GodotObject.IsInstanceValid(_layer) && _session != null)
        {
            if (!_board.Visible && _viewing != _localPlayer)
            {
                _viewing = _localPlayer; // the board always opens on your own
                Redraw();
            }
            _board.Toggle();
        }
    }

    /// <summary>The run ended: show its results, a moment after the game's own end screen appears.</summary>
    public void ShowResults(HistoryEntry entry, double delay = 2.5)
    {
        if (!GodotObject.IsInstanceValid(_layer))
        {
            return;
        }
        _layer.GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(_root))
            {
                _board.Hide();
                HistoryBook.ShowResults(_root, entry);
            }
        };
    }

    public void ShowHistory()
    {
        if (GodotObject.IsInstanceValid(_root))
        {
            HistoryBook.ShowHistory(_root);
        }
    }

    /// <summary>Versus: show another player's board (or your own again).</summary>
    private void View(ulong player) => Deferred(() =>
    {
        if (_session != null && player != _viewing)
        {
            _viewing = player;
            Redraw();
        }
    });

    private void PlaceIcon()
    {
        if (GodotObject.IsInstanceValid(_layer) && GodotObject.IsInstanceValid(_icon.Root))
        {
            MoveIconIntoTopBar();
            _icon.Place();
        }
    }

    /// <summary>
    /// Puts the icon in the game's top bar, so it sits on the same layer as the game's own top bar
    /// buttons: screens and menus the game opens over the top bar cover it too. It stays in our
    /// layer if the top bar can't be found.
    /// </summary>
    private void MoveIconIntoTopBar()
    {
        try
        {
            if (MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi?.TopBar is { } topBar
                && GodotObject.IsInstanceValid(topBar) && _icon.Root.GetParent() != topBar)
            {
                _icon.Root.Reparent(topBar, keepGlobalTransform: false);
            }
        }
        catch (Exception ex)
        {
            BingoLog.Error($"couldn't put the bingo icon in the top bar, keeping it on top: {ex.Message}");
        }
    }

    private void Refresh()
    {
        if (_session == null)
        {
            return;
        }
        var size = _session.Board.Size;
        var filled = _session.State.FilledFor(_viewing);
        var lines = _session.State.LinesFor(_viewing);
        var inLines = lines.SelectMany(line => BoardRules.Lines(size)[line]).ToHashSet();
        // Only Lockout marks other players' squares; in Team every filled square is just DONE.
        var others = new Dictionary<int, string>();
        for (var square = 0; square < _session.Board.Squares.Count && _session.Board.Mode == BingoMode.Lockout; square++)
        {
            if (_session.State.OwnerOf(square) is { } owner && owner != _localPlayer)
            {
                others[square] = Name(owner);
            }
        }
        _board.Update(filled, inLines, lines, _session.Board.Squares.Count, others, _viewing == _localPlayer ? null : Name(_viewing), OneAway());
        _board.SetPlayers(Previews(), View);
        _board.SetFeed(Feed());

        var myLines = _session.State.LinesFor(_localPlayer);
        _icon.Draw(size, _session.State.FilledFor(_localPlayer), myLines.SelectMany(line => BoardRules.Lines(size)[line]).ToHashSet());
    }

    /// <summary>
    /// Squares that would finish a line (spec 4.5): the shown player's (or team's), and in Lockout
    /// also each rival's, by name. Lockout lines with someone else's square can't be finished.
    /// </summary>
    private Dictionary<int, string> OneAway()
    {
        var result = new Dictionary<int, string>();
        if (_session is not { } session)
        {
            return result;
        }
        var size = session.Board.Size;
        var state = session.State;
        var lockout = session.Board.Mode == BingoMode.Lockout;
        IReadOnlySet<int> TakenByOthers(ulong player) => lockout
            ? Enumerable.Range(0, session.Board.Squares.Count).Where(sq => state.OwnerOf(sq) is { } o && o != player).ToHashSet()
            : new HashSet<int>();

        if (lockout)
        {
            foreach (var rival in state.Players.Where(p => p != _viewing))
            {
                foreach (var square in BoardRules.OneAway(size, state.FilledBy(rival), TakenByOthers(rival)))
                {
                    result[square] = Name(rival);
                }
            }
        }
        foreach (var square in BoardRules.OneAway(size, state.FilledFor(_viewing), TakenByOthers(_viewing)))
        {
            result[square] = "";
        }
        return result;
    }

    /// <summary>
    /// Co-op activity feed, newest first: "Sam: Gain 45+ Block in a single turn (floor 22)",
    /// "BINGO! Alex, row 2 (floor 18)". Built from the board's state, so it survives a Continue.
    /// </summary>
    private List<string> Feed()
    {
        if (_session is not { } session || session.State.Players.Count < 2)
        {
            return [];
        }
        var size = session.Board.Size;
        var squares = session.State.Results.Select(r =>
            (r.Order, Line: false, Text: $"{Who(r.PlayerId)}: {session.Board.Squares[r.Square].Text}{Floor(r.At)}"));
        var lines = _lines.Select(l =>
            (l.Order, Line: true, Text: (l.Ordinal == 1 ? "BINGO! " : $"Line #{l.Ordinal}: ") + $"{Who(l.PlayerId)}, {BoardRules.LineName(size, l.Line).ToLowerInvariant()}{Floor(l.At)}"));
        return squares.Concat(lines)
            .OrderByDescending(e => e.Order)
            .ThenByDescending(e => e.Line)
            .Select(e => e.Text)
            .ToList();

        string Who(ulong player) => player == _localPlayer ? "You" : Name(player);
        static string Floor(RunPosition? at) => at == null ? "" : $" (floor {at.Floor})";
    }

    /// <summary>Versus with two or more players: you first, then everyone else.</summary>
    private List<PlayerPreview> Previews()
    {
        if (_session is not { } session || session.Board.Mode != BingoMode.Versus || session.State.Players.Count < 2)
        {
            return [];
        }
        var size = session.Board.Size;
        return session.State.Players
            .OrderBy(p => p != _localPlayer)
            .Select(p =>
            {
                var lines = session.State.LinesFor(p);
                var filled = session.State.FilledFor(p);
                return new PlayerPreview(p, p == _localPlayer ? "You" : Name(p), filled,
                    lines.SelectMany(line => BoardRules.Lines(size)[line]).ToHashSet(), lines.Count, p == _viewing,
                    BoardRules.OneAway(size, filled).Count > 0);
            })
            .ToList();
    }

    // Checked as the event fires: by the time deferred work runs, the snapshot is over.
    private void OnFilled(SquareFilled filled)
    {
        if (!_muted)
        {
            ShowFilled(filled);
        }
    }

    private void OnLine(LineCompleted line)
    {
        if (!_lines.Any(l => l.PlayerId == line.PlayerId && l.Line == line.Line))
        {
            _lines.Add(line);
        }
        if (!_muted)
        {
            ShowLine(line);
        }
    }

    private void ShowFilled(SquareFilled filled) => Deferred(() =>
    {
        if (_session == null)
        {
            return;
        }
        var c = filled.Claim;
        var goal = _session.Board.Squares[filled.Square].Text;
        var mine = filled.PlayerId == _localPlayer;
        var shown = _session.Board.Mode != BingoMode.Versus || filled.PlayerId == _viewing;
        switch (_session.Board.Mode)
        {
            case BingoMode.Versus when !mine:
                // Their own copy of the board: update their preview, and their board if it's open.
                if (shown)
                {
                    _board.MarkFilled(filled.Square, Evidence(c));
                }
                Refresh();
                return;
            case BingoMode.Lockout when !mine:
                _board.SetEvidence(filled.Square, Evidence(c));
                Refresh();
                _toasts.Notice($"🔒 {Name(filled.PlayerId).ToUpperInvariant()} TOOK A SQUARE", goal, Toasts.Rival);
                return;
        }
        if (shown)
        {
            _board.MarkFilled(filled.Square, Evidence(c));
        }
        Refresh();
        _icon.Pulse();
#if DEBUG
        if (c.Evidence == DevTools.DevCommands.QuietEvidence)
        {
            return; // a whole row from the dev console: only the BINGO! should show
        }
#endif
        _toasts.Square(goal, (mine ? "" : $"{Name(filled.PlayerId)}: ") + Capitalize(c.Evidence));
    });

    private void ShowLine(LineCompleted line) => Deferred(() =>
    {
        if (_session == null)
        {
            return;
        }
        Refresh();
        if (line.PlayerId == _localPlayer || _session.Board.Mode == BingoMode.Team)
        {
            _toasts.Bingo();
        }
    });

    /// <summary>Lockout race: this player completed a goal, but someone else's claim reached the host first.</summary>
    private void OnRejected(ClaimResolved result) => Deferred(() =>
    {
        if (_session != null && result.PlayerId == _localPlayer && _session.State.OwnerOf(result.Square) is { } owner)
        {
            _toasts.Notice($"TOO LATE: {Name(owner).ToUpperInvariant()} GOT IT FIRST", _session.Board.Squares[result.Square].Text, Toasts.Rival);
        }
    });

    /// <summary>Someone else won (your own first line already gets the BINGO card).</summary>
    private void OnWon(BingoWin win)
    {
        if (_muted)
        {
            return;
        }
        Deferred(() =>
        {
            if (_session == null || (win.PlayerId == _localPlayer && win.Reason == WinReason.Line))
            {
                return;
            }
            var who = win.PlayerId == _localPlayer ? "YOU WIN" : $"{Name(win.PlayerId).ToUpperInvariant()} WINS";
            var how = win.Reason switch
            {
                WinReason.Clinch => "So far ahead that nobody can catch up",
                WinReason.MostSquares => "Most squares when the run ended",
                _ => "First line",
            };
            _toasts.Notice(who, how, win.PlayerId == _localPlayer ? BingoStyle.Gold : Toasts.Rival, big: true);
        });
    }

    private readonly Dictionary<ulong, string> _names = new();

    private string Name(ulong player)
    {
        if (!_names.TryGetValue(player, out var name))
        {
            _names[player] = name = Adapter.PlayerName(player);
        }
        return name;
    }

    private void OnProgress(GoalProgress progress) => Deferred(() =>
    {
        if (progress.PlayerId == _viewing)
        {
            _board.SetProgress(progress.Square, progress.Current, progress.Target);
        }
    });

    /// <summary>Runs UI work on the next frame, and only while the UI still exists.</summary>
    private void Deferred(Action action) => Callable.From(() =>
    {
        if (GodotObject.IsInstanceValid(_layer))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                BingoLog.Error($"bingo UI update failed: {ex}");
            }
        }
    }).CallDeferred();

    /// <summary>Hover details for a filled square: what happened and where, and who did it if it wasn't you.</summary>
    private string Evidence(ClaimResolved claim) =>
        (claim.PlayerId != _localPlayer ? $"{Name(claim.PlayerId)}: " : "") + Capitalize(claim.Evidence) + Where(claim.At);

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Where(RunPosition? at) =>
        at == null ? "" : $"\n· Act {at.Act + 1}, floor {at.Floor}" + (at.Round is { } r ? $", turn {r}" : "");
}
