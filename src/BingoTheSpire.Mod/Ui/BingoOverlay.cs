using Godot;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The in-run bingo UI: a "BINGO 3/25" button, the full board panel, and toasts. Built only from
/// Godot's built-in controls (no custom node classes, so nothing needs registering with Godot)
/// and added under the run's scene, so the game frees it when the run ends.
/// </summary>
internal sealed class BingoOverlay
{
    private static readonly Color Background = new(0.08f, 0.09f, 0.12f, 0.96f);
    private static readonly Color CellColor = new(0.16f, 0.18f, 0.23f);
    private static readonly Color FilledColor = new(0.17f, 0.45f, 0.25f);
    private static readonly Color LineBorder = new(0.95f, 0.78f, 0.25f);
    private static readonly Color Muted = new(0.75f, 0.77f, 0.82f);

    private readonly CanvasLayer _layer;
    private readonly Button _toggle;
    private readonly Control _boardRoot;
    private readonly Label _title;
    private readonly GridContainer _grid;
    private readonly Label _footer;
    private readonly VBoxContainer _toasts;
    private readonly List<CellView> _cells = [];

    private BingoSession? _session;
    private ulong _localPlayer;

    private BingoOverlay(Node runNode)
    {
        _layer = new CanvasLayer { Name = "BingoTheSpireOverlay", Layer = 90 };
        runNode.AddChild(_layer);

        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(root);

        _toggle = new Button { Text = "BINGO", FocusMode = Control.FocusModeEnum.None, TooltipText = "Show the bingo board" };
        _toggle.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _toggle.OffsetLeft = -190;
        _toggle.OffsetRight = -20;
        _toggle.OffsetTop = 92;
        _toggle.OffsetBottom = 132;
        _toggle.AddThemeFontSizeOverride("font_size", 20);
        _toggle.Pressed += Toggle;
        root.AddChild(_toggle);

        // Full-screen centering container that lets clicks through outside the panel.
        _boardRoot = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _boardRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_boardRoot);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", Box(Background, 12, 2, new Color(1, 1, 1, 0.15f), padding: 18));
        _boardRoot.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        _title = new Label { Text = "Bingo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(_title);
        var close = new Button { Text = "  X  ", FocusMode = Control.FocusModeEnum.None, TooltipText = "Close" };
        close.Pressed += () => _boardRoot.Visible = false;
        header.AddChild(close);

        _grid = new GridContainer();
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(_grid);

        _footer = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = Muted };
        _footer.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(_footer);

        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };
        _toasts.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _toasts.OffsetTop = 150;
        _toasts.OffsetLeft = -300;
        _toasts.OffsetRight = 300;
        root.AddChild(_toasts);
    }

    public static BingoOverlay? Current { get; private set; }

    /// <summary>Creates the UI for a run that just appeared on screen.</summary>
    public static void Attach(Node runNode, BingoRuntime runtime, BingoSettings settings)
    {
        Current?.Unbind();
        var overlay = new BingoOverlay(runNode);
        Current = overlay;

        BingoHotkey.Configure(settings.Hotkey);
        BingoHotkey.Bind(overlay.Toggle);
        overlay._footer.Text = BingoHotkey.ActiveKey is { } key
            ? $"Press {key} or the BINGO button to show or hide the board"
            : "Hotkey off: use the BINGO button. Set one in settings.json in the mod's data folder.";

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
        session.Filled += OnFilled;
        session.LineDone += OnLine;
        session.Tracker.Progressed += OnProgress;
        BuildGrid(session.Board);
        Refresh();
    }

    private void Unbind()
    {
        if (_session == null)
        {
            return;
        }
        _session.Filled -= OnFilled;
        _session.LineDone -= OnLine;
        _session.Tracker.Progressed -= OnProgress;
        _session = null;
    }

    private void Toggle()
    {
        if (GodotObject.IsInstanceValid(_boardRoot))
        {
            _boardRoot.Visible = !_boardRoot.Visible;
        }
    }

    private void BuildGrid(BingoBoard board)
    {
        foreach (var child in _grid.GetChildren())
        {
            child.QueueFree();
        }
        _cells.Clear();
        _grid.Columns = board.Size;
        _title.Text = $"Bingo  ·  {board.Code}";

        var cellSize = board.Size >= 5 ? new Vector2(168, 104) : new Vector2(196, 120);
        foreach (var square in board.Squares)
        {
            var view = new CellView(square, cellSize);
            _grid.AddChild(view.Root);
            _cells.Add(view);
        }
    }

    private void Refresh()
    {
        if (_session == null)
        {
            return;
        }
        var filled = _session.FilledBy(_localPlayer);
        var inLines = _session.LinesBy(_localPlayer)
            .SelectMany(line => BoardRules.Lines(_session.Board.Size)[line])
            .ToHashSet();
        foreach (var cell in _cells)
        {
            cell.SetState(filled.Contains(cell.Square.Index), inLines.Contains(cell.Square.Index));
        }
        var lines = _session.LinesBy(_localPlayer).Count;
        _toggle.Text = $"BINGO {filled.Count}/{_session.Board.Squares.Count}" + (lines > 0 ? $"  ★{lines}" : "");
    }

    private void OnFilled(SquareFilled filled) => Deferred(() =>
    {
        if (filled.PlayerId != _localPlayer || filled.Square >= _cells.Count)
        {
            return;
        }
        var c = filled.Completion;
        _cells[filled.Square].SetEvidence($"{c.Evidence}{Where(c.At)}");
        Refresh();
        Toast($"✔ {_cells[filled.Square].Square.Text}", FilledColor);
    });

    private void OnLine(LineCompleted line) => Deferred(() =>
    {
        if (line.PlayerId != _localPlayer)
        {
            return;
        }
        Refresh();
        Toast(line.Ordinal == 1 ? "BINGO!" : $"Line #{line.Ordinal}!", LineBorder, big: true);
    });

    private void OnProgress(GoalProgress progress) => Deferred(() =>
    {
        if (progress.PlayerId == _localPlayer && progress.Square < _cells.Count)
        {
            _cells[progress.Square].SetProgress(progress.Current, progress.Target);
        }
    });

    private void Toast(string text, Color accent, bool big = false)
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", Box(Background, 10, 2, accent, padding: 12));
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", big ? 34 : 20);
        box.AddChild(label);
        _toasts.AddChild(box);

        var tween = box.CreateTween();
        tween.TweenInterval(big ? 4.0 : 2.8);
        tween.TweenProperty(box, "modulate:a", 0.0, 0.6);
        tween.TweenCallback(Callable.From(box.QueueFree));
    }

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

    private static string Where(RunPosition? at) =>
        at == null ? "" : $"\nAct {at.Act + 1}, floor {at.Floor}" + (at.Round is { } r ? $", turn {r}" : "");

    internal static StyleBoxFlat Box(Color background, int radius, int border, Color borderColor, int padding)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = borderColor };
        box.SetCornerRadiusAll(radius);
        box.SetBorderWidthAll(border);
        box.SetContentMarginAll(padding);
        return box;
    }

    /// <summary>One square of the board.</summary>
    private sealed class CellView
    {
        private readonly Label _progress;
        private string _evidence = "";

        public CellView(BoardSquare square, Vector2 size)
        {
            Square = square;
            Root = new PanelContainer { CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Pass };
            var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            Root.AddChild(column);

            var text = new Label
            {
                Text = square.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(size.X - 16, 0),
            };
            text.AddThemeFontSizeOverride("font_size", 16);
            column.AddChild(text);

            _progress = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = Muted, Visible = false };
            _progress.AddThemeFontSizeOverride("font_size", 14);
            column.AddChild(_progress);

            SetState(filled: false, inLine: false);
        }

        public BoardSquare Square { get; }

        public PanelContainer Root { get; }

        private bool Filled { get; set; }

        public void SetState(bool filled, bool inLine)
        {
            Filled = filled;
            Root.AddThemeStyleboxOverride("panel", Box(filled ? FilledColor : CellColor, 8, inLine ? 3 : 1,
                inLine ? LineBorder : new Color(1, 1, 1, 0.12f), padding: 8));
            if (filled)
            {
                _progress.Visible = false;
            }
            Root.TooltipText = $"{Square.Text}\nDifficulty {Square.Difficulty}/5"
                + (filled ? $"\n\nDone: {_evidence}" : "");
        }

        public void SetEvidence(string evidence) => _evidence = evidence;

        public void SetProgress(int current, int target)
        {
            if (Filled || target <= 1)
            {
                return;
            }
            _progress.Text = $"{current}/{target}";
            _progress.Visible = current > 0;
        }
    }
}
