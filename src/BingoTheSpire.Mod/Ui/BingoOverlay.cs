using Godot;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Session;

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

        BingoHotkey.Configure(settings.Hotkey);
        BingoHotkey.Bind(overlay.Toggle);
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
        session.Tracker.Progressed += OnProgress;
        _board.Build(session.Board);

        // A continued run arrives with progress already restored: draw it as it stands, no toasts.
        foreach (var square in session.State.FilledFor(localPlayer))
        {
            var by = session.State.OwnerOf(square) ?? localPlayer;
            if (session.State.ClaimOf(square, by) is { } c)
            {
                _board.SetEvidence(square, $"{c.Evidence}{Where(c.At)}");
            }
        }
        foreach (var progress in session.Tracker.ProgressOf(localPlayer))
        {
            _board.SetProgress(progress.Square, progress.Current, progress.Target);
        }
        Refresh();
    }

    private void Unbind()
    {
        if (_session == null)
        {
            return;
        }
        _session.State.Filled -= OnFilled;
        _session.State.LineDone -= OnLine;
        _session.Tracker.Progressed -= OnProgress;
        _session = null;
    }

    private void Toggle()
    {
        if (GodotObject.IsInstanceValid(_layer))
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
        _board.Update(filled, inLines, lines.Count, _session.Board.Squares.Count);
        _icon.Draw(size, filled, inLines);
    }

    private void OnFilled(SquareFilled filled) => Deferred(() =>
    {
        // Your own squares; in Team mode, your teammates' too.
        if (_session == null || (filled.PlayerId != _localPlayer && _session.Board.Mode != BingoMode.Team))
        {
            return;
        }
        var c = filled.Claim;
        _board.MarkFilled(filled.Square, $"{c.Evidence}{Where(c.At)}");
        Refresh();
        _icon.Pulse();
        _toasts.Square(_session.Board.Squares[filled.Square].Text, Capitalize(c.Evidence));
    });

    private void OnLine(LineCompleted line) => Deferred(() =>
    {
        if (_session == null || (line.PlayerId != _localPlayer && _session.Board.Mode != BingoMode.Team))
        {
            return;
        }
        Refresh();
        _toasts.Bingo(line.Ordinal, BoardRules.LineName(_session.Board.Size, line.Line) + Where(line.At).Replace('\n', ' '));
    });

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

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Where(RunPosition? at) =>
        at == null ? "" : $"\n· Act {at.Act + 1}, floor {at.Floor}" + (at.Round is { } r ? $", turn {r}" : "");
}
