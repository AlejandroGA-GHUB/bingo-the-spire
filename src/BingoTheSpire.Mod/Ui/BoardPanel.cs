using Godot;
using BingoTheSpire.Core.Boards;

namespace BingoTheSpire.Mod.Ui;

/// <summary>The full board: colored by difficulty, glowing when filled, gold for finished lines.</summary>
internal sealed class BoardPanel
{
    private readonly PanelContainer _panel;
    private readonly Label _code;
    private readonly Label _stats;
    private readonly GridContainer _grid;
    private readonly Label _footer;
    private readonly List<Cell> _cells = [];
    private readonly HashSet<int> _fresh = [];

    public BoardPanel(Control parent)
    {
        // Full-screen centering container that lets clicks through outside the panel.
        Root = new CenterContainer { Name = "BingoBoard", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(Root);

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel",
            BingoStyle.Box(BingoStyle.PanelBg, 16, 3, BingoStyle.PanelBorder, padding: 20, glow: new Color(0, 0, 0, 0.6f), glowSize: 24));
        Root.AddChild(_panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        column.AddChild(header);
        header.AddChild(BingoStyle.Text("BINGO", 40, BingoStyle.Gold, outline: 10));
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        titles.AddThemeConstantOverride("separation", 0);
        header.AddChild(titles);
        _stats = BingoStyle.Text("", 20, Colors.White, outline: 5);
        titles.AddChild(_stats);
        _code = BingoStyle.Text("", 15, BingoStyle.Muted, outline: 4);
        titles.AddChild(_code);

        var close = new Button { Text = "✕", FocusMode = Control.FocusModeEnum.None, TooltipText = "Close", CustomMinimumSize = new Vector2(44, 44) };
        close.AddThemeFontSizeOverride("font_size", 22);
        close.Pressed += Hide;
        header.AddChild(close);

        _grid = new GridContainer();
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        column.AddChild(_grid);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        footer.AddThemeConstantOverride("separation", 14);
        column.AddChild(footer);
        footer.AddChild(BingoStyle.Text("Difficulty", 14, BingoStyle.Muted, outline: 3));
        for (var d = 1; d <= 5; d++)
        {
            footer.AddChild(BingoStyle.Text($"■ {d}", 15, BingoStyle.Difficulty(d), outline: 3));
        }
        _footer = BingoStyle.Text("", 14, BingoStyle.Muted, outline: 3);
        footer.AddChild(_footer);
    }

    public CenterContainer Root { get; }

    public bool Visible => Root.Visible;

    public void SetFooter(string text) => _footer.Text = "   ·   " + text;

    public void Toggle()
    {
        if (Root.Visible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        Root.Visible = true;
        BingoStyle.CenterPivot(_panel);
        _panel.Scale = new Vector2(0.9f, 0.9f);
        _panel.Modulate = new Color(1, 1, 1, 0);
        var tween = _panel.CreateTween().SetParallel();
        tween.TweenProperty(_panel, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_panel, "modulate:a", 1.0, 0.15);
        // Squares filled while the board was closed get their moment now.
        foreach (var index in _fresh.Where(i => i < _cells.Count))
        {
            var cell = _cells[index];
            Callable.From(() => BingoStyle.Pulse(cell.Root, 1.15f)).CallDeferred();
        }
        _fresh.Clear();
    }

    public void Hide() => Root.Visible = false;

    public void Build(BingoBoard board)
    {
        foreach (var child in _grid.GetChildren())
        {
            child.QueueFree();
        }
        _cells.Clear();
        _fresh.Clear();
        _grid.Columns = board.Size;
        _code.Text = $"Board {board.Code}  ·  {board.Mode}";

        var cellSize = board.Size >= 5 ? new Vector2(176, 112) : new Vector2(206, 128);
        foreach (var square in board.Squares)
        {
            var cell = new Cell(square, cellSize);
            _grid.AddChild(cell.Root);
            _cells.Add(cell);
        }
    }

    public void Update(IReadOnlySet<int> filled, IReadOnlySet<int> inLines, int lines, int total)
    {
        foreach (var cell in _cells)
        {
            cell.SetState(filled.Contains(cell.Square.Index), inLines.Contains(cell.Square.Index));
        }
        _stats.Text = $"{filled.Count} / {total} squares" + (lines switch
        {
            0 => "",
            1 => "   ★ BINGO!",
            _ => $"   ★ {lines} lines",
        });
    }

    public void MarkFilled(int index, string evidence)
    {
        if (index >= _cells.Count)
        {
            return;
        }
        _cells[index].Evidence = evidence;
        if (Root.Visible)
        {
            BingoStyle.Pulse(_cells[index].Root, 1.15f);
        }
        else
        {
            _fresh.Add(index);
        }
    }

    public void SetProgress(int index, int current, int target)
    {
        if (index < _cells.Count)
        {
            _cells[index].SetProgress(current, target);
        }
    }

    /// <summary>One square: difficulty pips, the goal, and a progress bar for goals being worked on.</summary>
    private sealed class Cell
    {
        private readonly Label _pips;
        private readonly Label _text;
        private readonly ProgressBar _bar;
        private readonly Label _barLabel;
        private readonly Control _barRow;
        private readonly Color _accent;
        private bool _filled;

        public Cell(BoardSquare square, Vector2 size)
        {
            Square = square;
            _accent = BingoStyle.Difficulty(square.Difficulty);
            Root = new PanelContainer { CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Pass };

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 2);
            Root.AddChild(column);

            _pips = BingoStyle.Text(new string('●', square.Difficulty) + new string('○', 5 - square.Difficulty), 12, _accent, outline: 3);
            column.AddChild(_pips);

            _text = BingoStyle.Text(square.Text, 17, Colors.White, outline: 5);
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _text.CustomMinimumSize = new Vector2(size.X - 20, 0);
            column.AddChild(_text);

            var barRow = new HBoxContainer { Visible = false };
            barRow.AddThemeConstantOverride("separation", 6);
            _barRow = barRow;
            column.AddChild(barRow);
            _bar = new ProgressBar { ShowPercentage = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            _bar.AddThemeStyleboxOverride("background", BingoStyle.Box(new Color(0, 0, 0, 0.45f), 4, 0, Colors.Transparent, 0));
            _bar.AddThemeStyleboxOverride("fill", BingoStyle.Box(_accent, 4, 0, Colors.Transparent, 0));
            barRow.AddChild(_bar);
            _barLabel = BingoStyle.Text("", 13, Colors.White, outline: 3);
            barRow.AddChild(_barLabel);

            SetState(filled: false, inLine: false);
        }

        public BoardSquare Square { get; }

        public PanelContainer Root { get; }

        public string Evidence { get; set; } = "";

        public void SetState(bool filled, bool inLine)
        {
            _filled = filled;
            StyleBoxFlat style;
            if (inLine)
            {
                style = BingoStyle.Box(BingoStyle.Filled.Lerp(BingoStyle.Gold, 0.35f), 10, 4, BingoStyle.Gold, 8, BingoStyle.GoldGlow, 12);
            }
            else if (filled)
            {
                style = BingoStyle.Box(BingoStyle.Filled, 10, 3, BingoStyle.FilledBorder, 8, BingoStyle.FilledGlow, 8);
            }
            else
            {
                style = BingoStyle.Box(_accent.Darkened(0.78f), 10, 2, _accent.Darkened(0.2f), 8);
            }
            Root.AddThemeStyleboxOverride("panel", style);
            _pips.Text = filled ? "✔ DONE" : new string('●', Square.Difficulty) + new string('○', 5 - Square.Difficulty);
            _pips.AddThemeColorOverride("font_color", filled ? (inLine ? BingoStyle.Gold : BingoStyle.FilledBorder) : _accent);
            if (filled)
            {
                _barRow.Visible = false;
            }
            Root.TooltipText = $"{Square.Text}\nDifficulty {Square.Difficulty}/5" + (filled && Evidence.Length > 0 ? $"\n\n✔ {Evidence}" : "");
        }

        public void SetProgress(int current, int target)
        {
            if (_filled || target <= 1)
            {
                return;
            }
            _bar.MaxValue = target;
            _bar.Value = current;
            _barLabel.Text = $"{current}/{target}";
            _barRow.Visible = current > 0;
        }
    }
}
