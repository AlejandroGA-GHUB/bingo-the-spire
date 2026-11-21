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
    private readonly BingoIcon _icon;
    private readonly BoardPanel _board;
    private readonly Toasts _toasts;

    private BingoSession? _session;
    private ulong _localPlayer;

    // True while the host's snapshot is applied: its squares are drawn, not announced.
    private bool _muted;

    private BingoOverlay(Node runNode, BingoSettings settings)
    {
        _layer = new CanvasLayer { Name = "BingoTheSpireOverlay", Layer = 90 };
        runNode.AddChild(_layer);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);

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
        };
    }

    public static BingoOverlay? Current { get; private set; }

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
        overlay._board.SetFooter(key != null ? $"{key} to show or hide" : "Hotkey off (settings.json)");

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
        var local = _localPlayer;
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
            _board.Toggle();
        }
    }

    private void PlaceIcon()
    {
        if (GodotObject.IsInstanceValid(_layer))
        {
            _icon.Place();
        }
    }

    private void Refresh()
    {
        if (_session == null)
        {
            return;
        }
        var size = _session.Board.Size;
        var filled = _session.State.FilledFor(_localPlayer);
        var lines = _session.State.LinesFor(_localPlayer);
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
        _board.Update(filled, inLines, lines.Count, _session.Board.Squares.Count, others);
        _icon.Draw(size, filled, inLines);
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
        switch (_session.Board.Mode)
        {
            case BingoMode.Versus when !mine:
                return; // their own copy of the board
            case BingoMode.Lockout when !mine:
                _board.SetEvidence(filled.Square, Evidence(c));
                Refresh();
                _toasts.Notice($"🔒 {Name(filled.PlayerId).ToUpperInvariant()} TOOK A SQUARE", goal, Toasts.Rival);
                return;
        }
        _board.MarkFilled(filled.Square, Evidence(c));
        Refresh();
        _icon.Pulse();
        _toasts.Square(goal, (mine ? "" : $"{Name(filled.PlayerId)}: ") + Capitalize(c.Evidence));
    });

    private void ShowLine(LineCompleted line) => Deferred(() =>
    {
        if (_session == null || (line.PlayerId != _localPlayer && _session.Board.Mode != BingoMode.Team))
        {
            return;
        }
        Refresh();
        _toasts.Bingo(line.Ordinal, BoardRules.LineName(_session.Board.Size, line.Line) + Where(line.At).Replace('\n', ' '));
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
        if (progress.PlayerId == _localPlayer)
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
