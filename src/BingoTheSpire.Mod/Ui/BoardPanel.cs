using Godot;
using BingoTheSpire.Core.Boards;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The full board, drawn as an open book: a brass nameplate with the score and board code, one
/// paper note per square (difficulty in the colored corner, DONE stamped when filled), and the
/// difficulty key along the bottom of the page.
/// </summary>
internal sealed class BoardPanel
{
    private const int Gap = 16;

    private static readonly float[] Tilts = [-1.2f, 0.8f, -0.4f, 1.1f, -0.9f, 0.5f, -1.3f, 0.9f, -0.3f, 1.2f, -0.7f, 0.3f, -1.0f, 0.7f, -0.5f];

    private readonly Control _parent;
    private readonly List<Cell> _cells = [];
    private readonly HashSet<int> _fresh = [];
    private BookFrame? _book;
    private Label? _stats;
    private Label? _code;
    private GridContainer? _grid;
    private Label? _footer;
    private string _footerText = "";
    private int _size;

    public BoardPanel(Control parent)
    {
        _parent = parent;
    }

    public bool Visible => _book?.Visible ?? false;

    public void SetFooter(string text)
    {
        _footerText = text;
        if (_footer != null)
        {
            _footer.Text = "·  " + text;
        }
    }

    public void Toggle()
    {
        if (Visible)
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
        if (_book == null)
        {
            return;
        }
        _book.Show();
        // Squares filled while the board was closed get their moment now.
        foreach (var index in _fresh.Where(i => i < _cells.Count))
        {
            var cell = _cells[index];
            Callable.From(() => BingoStyle.Pulse(cell.Note.Paper, 1.1f)).CallDeferred();
        }
        _fresh.Clear();
    }

    public void Hide() => _book?.Hide();

    /// <summary>Builds the book for a board. The book's width depends on the board size, so it's made per board.</summary>
    public void Build(BingoBoard board)
    {
        var wasVisible = Visible;
        if (_book != null)
        {
            _book.Root.QueueFree();
        }
        _cells.Clear();
        _fresh.Clear();
        _size = board.Size;

        var noteSize = board.Size >= 5 ? new Vector2(196, 104) : new Vector2(240, 124);
        var contentWidth = noteSize.X * board.Size + Gap * (board.Size - 1);
        _book = new BookFrame(_parent, "BingoBoard", contentWidth, backdrop: false);
        _parent.MoveChild(_book.Root, 0); // under the toasts and the icon
        var content = _book.Content;

        // Nameplate: title, score and board code.
        var info = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        info.AddThemeConstantOverride("separation", 0);
        _stats = BingoStyle.Ink("", BingoFonts.NoteBold, 19, BingoStyle.NameplateText);
        info.AddChild(_stats);
        _code = BingoStyle.Ink($"Board: {board.Code}  ·  {board.Mode}", BingoFonts.NoteBold, 15, BingoStyle.NameplateText with { A = 0.8f });
        info.AddChild(_code);
        content.AddChild(Nameplate("BINGO!", info));

        _grid = new GridContainer { Columns = board.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", Gap);
        _grid.AddThemeConstantOverride("v_separation", 14);
        content.AddChild(_grid);
        foreach (var square in board.Squares)
        {
            var column = square.Index % board.Size;
            var onSpine = board.Size % 2 == 1 && column == board.Size / 2;
            var cell = new Cell(square, noteSize, Tilts[square.Index % Tilts.Length], onSpine);
            _grid.AddChild(cell.Note.Slot);
            _cells.Add(cell);
        }

        content.AddChild(Legend(out _footer));
        SetFooter(_footerText);

        if (wasVisible)
        {
            _book.Show();
        }
    }

    /// <param name="others">Lockout only: squares another player took, with their name.</param>
    public void Update(IReadOnlySet<int> filled, IReadOnlySet<int> inLines, int lines, int total,
        IReadOnlyDictionary<int, string>? others = null)
    {
        foreach (var cell in _cells)
        {
            var index = cell.Square.Index;
            cell.SetState(filled.Contains(index), inLines.Contains(index), others?.GetValueOrDefault(index));
        }
        if (_stats != null)
        {
            _stats.Text = $"{filled.Count} / {total} squares" + (lines switch
            {
                0 => "",
                1 => "  ·  1 line!",
                _ => $"  ·  {lines} lines!",
            });
        }
    }

    public void SetEvidence(int index, string evidence)
    {
        if (index < _cells.Count)
        {
            _cells[index].Evidence = evidence;
        }
    }

    public void MarkFilled(int index, string evidence)
    {
        if (index >= _cells.Count)
        {
            return;
        }
        _cells[index].Evidence = evidence;
        if (Visible)
        {
            BingoStyle.Pulse(_cells[index].Note.Paper, 1.1f);
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

    /// <summary>The brass nameplate at the top of a book page.</summary>
    public static Control Nameplate(string title, Control? info)
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = BingoStyle.Box(BingoStyle.Brass, 12, 0, BingoStyle.BrassDark, 0, new Color(0, 0, 0, 0.3f), 8);
        box.BorderWidthBottom = 4;
        box.BorderColor = BingoStyle.BrassDark;
        box.BorderWidthTop = 1;
        box.ShadowOffset = new Vector2(0, 4);
        box.ContentMarginLeft = box.ContentMarginRight = 26;
        box.ContentMarginTop = 6;
        box.ContentMarginBottom = 8;
        box.AntiAliasing = true;
        plate.AddThemeStyleboxOverride("panel", box);
        center.AddChild(plate);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 20);
        plate.AddChild(row);
        var heading = BingoStyle.Ink(title, BingoFonts.Heading, 40, BingoStyle.Cream);
        heading.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#6b4512"));
        heading.AddThemeConstantOverride("shadow_offset_x", 0);
        heading.AddThemeConstantOverride("shadow_offset_y", 3);
        heading.AddThemeConstantOverride("shadow_outline_size", 2);
        heading.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(heading);
        if (info != null)
        {
            info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(info);
        }
        return center;
    }

    /// <summary>The difficulty key along the bottom of the page.</summary>
    private static Control Legend(out Label footer)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(BingoStyle.Ink("Difficulty", BingoFonts.NoteBold, 17, BingoStyle.PageInkSoft));
        for (var d = 1; d <= 5; d++)
        {
            var key = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            key.AddThemeConstantOverride("separation", 6);
            var chip = new Control { CustomMinimumSize = new Vector2(22, 22), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            var color = BingoStyle.Difficulty(d);
            chip.Draw += () =>
            {
                chip.DrawColoredPolygon([new(0, 0), new(22, 0), new(0, 22)], color);
                chip.DrawColoredPolygon([new(22, 0), new(22, 22), new(0, 22)], new Color(0, 0, 0, 0.08f));
                chip.DrawLine(new Vector2(22, 0), new Vector2(0, 22), color, 1.2f, antialiased: true);
            };
            key.AddChild(chip);
            key.AddChild(BingoStyle.Ink(d switch { 1 => "1 easy", 5 => "5 hard", _ => d.ToString() }, BingoFonts.NoteBold, 17, BingoStyle.PageInkSoft));
            row.AddChild(key);
        }
        footer = BingoStyle.Ink("", BingoFonts.NoteBold, 17, BingoStyle.PageInkSoft);
        row.AddChild(footer);
        return row;
    }

    /// <summary>One square: a paper note with the goal, a progress bar while it's being worked on, and a DONE stamp.</summary>
    private sealed class Cell
    {
        private readonly Label _text;
        private readonly Control _barRow;
        private readonly ProgressBar _bar;
        private readonly Label _barLabel;
        private readonly PanelContainer _stamp;
        private readonly Label _stampText;
        private readonly Label _taken;
        private bool _filled;
        private bool _isTaken;

        public Cell(BoardSquare square, Vector2 size, float tilt, bool onSpine)
        {
            Square = square;
            Note = new PaperNote(size, tilt);
            Note.SetTag(BingoStyle.Difficulty(square.Difficulty), square.Difficulty.ToString());
            Note.SetOnSpine(onSpine);
            Note.SetMargins(left: 24, top: 12, right: 12, bottom: 16);

            _text = BingoStyle.Ink(square.Text, BingoFonts.Note, 18, BingoStyle.PageInk);
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _text.AddThemeConstantOverride("line_spacing", -2);
            Note.Content.AddChild(_text);

            var barRow = new HBoxContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            barRow.AddThemeConstantOverride("separation", 6);
            barRow.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            barRow.OffsetLeft = 24;
            barRow.OffsetRight = -12;
            barRow.OffsetTop = -17;
            barRow.OffsetBottom = -5;
            _barRow = barRow;
            Note.Paper.AddChild(barRow);
            var accent = BingoStyle.Difficulty(square.Difficulty);
            _bar = new ProgressBar { ShowPercentage = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 7), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
            _bar.AddThemeStyleboxOverride("background", BingoStyle.Box(new Color(0.42f, 0.36f, 0.3f, 0.18f), 4, 0, Colors.Transparent, 0));
            _bar.AddThemeStyleboxOverride("fill", BingoStyle.Box(accent.Darkened(0.1f), 4, 0, Colors.Transparent, 0));
            barRow.AddChild(_bar);
            _barLabel = BingoStyle.Ink("", BingoFonts.NoteBold, 13, BingoStyle.PageInkSoft);
            barRow.AddChild(_barLabel);

            _stamp = PaperNote.Stamp("DONE", BingoStyle.RedInk, 15, -12);
            _stampText = (Label)_stamp.GetChild(0);
            _stamp.Visible = false;
            PaperNote.PinTo(_stamp, Control.LayoutPreset.BottomRight, 8, 5);
            Note.Paper.AddChild(_stamp);

            _taken = BingoStyle.Ink("", BingoFonts.NoteBold, 14, BingoStyle.TakenInk);
            _taken.HorizontalAlignment = HorizontalAlignment.Center;
            _taken.Visible = false;
            _taken.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            _taken.OffsetTop = -22;
            _taken.OffsetBottom = -3;
            Note.Paper.AddChild(_taken);

            SetState(filled: false, inLine: false);
        }

        public BoardSquare Square { get; }

        public PaperNote Note { get; }

        public string Evidence { get; set; } = "";

        /// <param name="takenBy">Lockout: the player who took this square from you.</param>
        public void SetState(bool filled, bool inLine, string? takenBy = null)
        {
            _filled = filled;
            _isTaken = !filled && takenBy != null;

            Note.SetPaperColor(_isTaken ? BingoStyle.PaperTaken : BingoStyle.Paper);
            Note.SetBorder(filled && inLine ? BingoStyle.Brass : null);
            _text.Modulate = _isTaken ? new Color(1, 1, 1, 0.45f) : filled ? new Color(1, 1, 1, 0.6f) : Colors.White;

            _stamp.Visible = filled;
            var ink = inLine ? BingoStyle.BrassDark : BingoStyle.RedInk;
            _stampText.AddThemeColorOverride("font_color", ink);
            if (_stamp.GetThemeStylebox("panel") is StyleBoxFlat stampBox)
            {
                stampBox.BorderColor = ink;
            }

            _taken.Visible = _isTaken;
            _taken.Text = _isTaken ? $"Taken by {takenBy}" : "";
            if (filled || _isTaken)
            {
                _barRow.Visible = false;
            }

            // Hover only says what happened and where; the goal itself is already on the note.
            Note.Slot.TooltipText = (filled || _isTaken) && Evidence.Length > 0 ? Evidence : "";
        }

        public void SetProgress(int current, int target)
        {
            if (_filled || _isTaken || target <= 1)
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
