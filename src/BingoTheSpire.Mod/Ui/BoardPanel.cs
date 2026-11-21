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
    private HBoxContainer? _players;
    private VBoxContainer? _feed;
    private GridContainer? _grid;
    private Control? _strikes;
    private readonly Dictionary<int, float> _struck = new(); // line → how much of its stroke is drawn (0-1)
    private readonly HashSet<int> _striking = [];
    private bool _firstUpdate;
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
        // Lines made while the board was closed get struck through once the book has opened.
        foreach (var line in _struck.Where(kv => kv.Value < 1).Select(kv => kv.Key).ToList())
        {
            Strike(line, delay: 0.3);
        }
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
    /// <param name="modeName">What to call the mode on the nameplate (e.g. "Solo" for a one-player run).</param>
    public void Build(BingoBoard board, string modeName)
    {
        var wasVisible = Visible;
        if (_book != null)
        {
            _book.Root.QueueFree();
        }
        _cells.Clear();
        _fresh.Clear();
        _struck.Clear();
        _striking.Clear();
        _firstUpdate = true;
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
        // "Board: code [clip] · mode"
        var codeRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        codeRow.AddThemeConstantOverride("separation", 2);
        var soft = BingoStyle.NameplateText with { A = 0.8f };
        codeRow.AddChild(BingoStyle.Ink($"Board: {board.Code}", BingoFonts.NoteBold, 15, soft));
        codeRow.AddChild(CopyClip.Create(board.Code, BingoStyle.NameplateText, 15));
        codeRow.AddChild(BingoStyle.Ink($"·  {modeName}", BingoFonts.NoteBold, 15, soft));
        info.AddChild(codeRow);
        // The nameplate stays centered; Versus previews of each player's board sit at the right.
        var header = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        content.AddChild(header);
        header.AddChild(Nameplate("BINGO!", info));
        _players = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _players.AddThemeConstantOverride("separation", 8);
        header.AddChild(_players);
        // Co-op: the latest happenings, at the left.
        _feed = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(FeedWidth, 0),
        };
        _feed.AddThemeConstantOverride("separation", 0);
        header.AddChild(_feed);

        _grid = new GridContainer { Columns = board.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", Gap);
        _grid.AddThemeConstantOverride("v_separation", 14);
        // The grid shares its space with a layer on top for the strokes through finished lines.
        var gridLayers = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        content.AddChild(gridLayers);
        gridLayers.AddChild(_grid);
        _strikes = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Material = HighlighterMaterial };
        _strikes.Draw += DrawStrikes;
        gridLayers.AddChild(_strikes);
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

    private const float FeedWidth = 250;

    /// <summary>Co-op activity feed: the newest lines first. Hover shows a longer list.</summary>
    public void SetFeed(IReadOnlyList<string> newestFirst)
    {
        if (_feed == null)
        {
            return;
        }
        foreach (var child in _feed.GetChildren())
        {
            child.QueueFree();
        }
        _feed.TooltipText = string.Join('\n', newestFirst.Take(12));
        if (newestFirst.Count == 0)
        {
            return;
        }
        _feed.AddChild(BingoStyle.Ink("Latest", BingoFonts.Heading, 14, BingoStyle.PageInkSoft));
        foreach (var (text, i) in newestFirst.Take(3).Select((t, i) => (t, i)))
        {
            var line = BingoStyle.Ink(text, BingoFonts.NoteBold, 13, i == 0 ? BingoStyle.PageInk : BingoStyle.PageInkSoft);
            line.ClipText = true;
            line.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.CustomMinimumSize = new Vector2(FeedWidth, 0);
            line.MouseFilter = Control.MouseFilterEnum.Pass;
            _feed.AddChild(line);
        }
    }

    /// <summary>Versus: the row of player previews (empty for one player, Lockout and Team).</summary>
    public void SetPlayers(IReadOnlyList<PlayerPreview> players, Action<ulong> pick)
    {
        if (_players != null)
        {
            PlayerPreviews.Fill(_players, _size, players, pick);
        }
    }

    /// <param name="others">Lockout only: squares another player took, with their name.</param>
    /// <param name="lines">Finished lines (indices into <see cref="BoardRules.Lines"/>).</param>
    /// <param name="whose">Whose board this is when it isn't yours, for the nameplate.</param>
    /// <param name="oneAway">Squares that would finish a line: "" for this board's player, or (Lockout) a rival's name.</param>
    public void Update(IReadOnlySet<int> filled, IReadOnlySet<int> inLines, IReadOnlyList<int> lines, int total,
        IReadOnlyDictionary<int, string>? others = null, string? whose = null, IReadOnlyDictionary<int, string>? oneAway = null)
    {
        foreach (var line in lines.Where(l => !_struck.ContainsKey(l)))
        {
            // Lines already there when the board is built (e.g. after Continue) are drawn straight away.
            _struck[line] = _firstUpdate ? 1 : 0;
            if (!_firstUpdate && Visible)
            {
                Strike(line, delay: 0.15);
            }
        }
        _firstUpdate = false;
        _strikes?.QueueRedraw();

        foreach (var cell in _cells)
        {
            var index = cell.Square.Index;
            cell.SetState(filled.Contains(index), inLines.Contains(index), others?.GetValueOrDefault(index));
            cell.SetOneAway(oneAway?.GetValueOrDefault(index));
        }
        if (_stats != null)
        {
            _stats.Text = (whose != null ? $"{whose}'s board  ·  " : "") + $"{filled.Count} / {total} squares" + (lines.Count switch
            {
                0 => "",
                1 => "  ·  1 line!",
                _ => $"  ·  {lines.Count} lines!",
            });
        }
    }

    /// <summary>Swipes the highlighter through a finished line, from its first square to its last.</summary>
    private void Strike(int line, double delay)
    {
        if (_strikes == null || !_striking.Add(line))
        {
            return;
        }
        var strikes = _strikes;
        var tween = strikes.CreateTween();
        tween.TweenInterval(delay);
        tween.TweenMethod(Callable.From<float>(t =>
        {
            _struck[line] = t;
            strikes.QueueRedraw();
        }), 0f, 1f, 0.52).SetTrans(Tween.TransitionType.Quart).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => _striking.Remove(line)));
    }

    private static readonly Color HighlighterInk = Color.FromHtml("#ffd54a");
    private const float HighlighterWidth = 32;

    /// <summary>
    /// Multiplies the strokes into what's under them, like a real highlighter: the note text stays
    /// dark and the paper turns yellow. Soft edges fade to white first, so they blend in too.
    /// </summary>
    private static readonly ShaderMaterial HighlighterMaterial = new()
    {
        Shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                render_mode blend_mul;
                void fragment() {
                    COLOR = vec4(mix(vec3(1.0), COLOR.rgb, COLOR.a), 1.0);
                }
                """,
        },
    };

    /// <summary>A highlighter swipe along each finished line, with a gentle wave.</summary>
    private void DrawStrikes()
    {
        if (_strikes == null || _cells.Count == 0)
        {
            return;
        }
        foreach (var (line, drawn) in _struck)
        {
            if (drawn <= 0)
            {
                continue;
            }
            var cells = BoardRules.Lines(_size)[line];
            var first = _cells[cells[0]].Note.Slot;
            var last = _cells[cells[^1]].Note.Slot;
            var start = first.Position + first.Size / 2;
            var end = last.Position + last.Size / 2;
            var direction = (end - start).Normalized();
            // Reach to just inside the end notes' edges, whichever way the line runs (rows, columns, diagonals).
            var toEdgeX = Math.Abs(direction.X) > 0.001f ? first.Size.X / 2 / Math.Abs(direction.X) : float.MaxValue;
            var toEdgeY = Math.Abs(direction.Y) > 0.001f ? first.Size.Y / 2 / Math.Abs(direction.Y) : float.MaxValue;
            var reach = Math.Min(toEdgeX, toEdgeY) - 10;
            start -= direction * reach;
            end += direction * reach;
            var across = new Vector2(-direction.Y, direction.X);

            const int steps = 48;
            var count = Math.Max(2, (int)Math.Ceiling(steps * drawn) + 1);
            var points = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var t = Math.Min(drawn, (float)i / steps);
                var wave = MathF.Sin(t * 7f + 1f + line) * 4f;
                points[i] = start + (end - start) * t + across * wave;
            }
            _strikes.DrawPolyline(points, HighlighterInk with { A = 0.55f }, HighlighterWidth, antialiased: true);
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
        // Cream letters with a thick near-black outline and a hard drop shadow.
        var ink = Color.FromHtml("#1c1611");
        var heading = BingoStyle.Ink(title, BingoFonts.Heading, 46, BingoStyle.Cream);
        heading.AddThemeColorOverride("font_outline_color", ink);
        heading.AddThemeConstantOverride("outline_size", 9);
        heading.AddThemeColorOverride("font_shadow_color", ink);
        heading.AddThemeConstantOverride("shadow_offset_x", 0);
        heading.AddThemeConstantOverride("shadow_offset_y", 4);
        heading.AddThemeConstantOverride("shadow_outline_size", 9);
        heading.VerticalAlignment = VerticalAlignment.Center;
        // Lifted a little, so the drop shadow has room above the plate's bottom edge.
        var lift = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        lift.AddThemeConstantOverride("margin_top", -7);
        lift.AddThemeConstantOverride("margin_bottom", 11);
        lift.AddChild(heading);
        row.AddChild(lift);
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
        private readonly PanelContainer _oneAway;
        private readonly Label _oneAwayText;
        private Tween? _oneAwayPulse;
        private bool _filled;
        private bool _isTaken;

        public Cell(BoardSquare square, Vector2 size, float tilt, bool onSpine)
        {
            Square = square;
            Note = new PaperNote(size, tilt);
            Note.SetTag(BingoStyle.Difficulty(square.Difficulty), square.Difficulty.ToString());
            Note.SetOnSpine(onSpine);
            Note.SetMargins(left: 24, top: 10, right: 12, bottom: TextBottom);

            _text = BingoStyle.Ink(square.Text, BingoFonts.Note, FitFontSize(square.Text, size), BingoStyle.PageInk);
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _text.AddThemeConstantOverride("line_spacing", TextLineSpacing);
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

            _oneAway = PaperNote.Stamp("1 AWAY!", BingoStyle.OneAwayInk, 13, 7);
            _oneAwayText = (Label)_oneAway.GetChild(0);
            _oneAway.Visible = false;
            PaperNote.PinTo(_oneAway, Control.LayoutPreset.TopRight, 6, -10);
            Note.Paper.AddChild(_oneAway);

            SetState(filled: false, inLine: false);
        }

        public BoardSquare Square { get; }

        public PaperNote Note { get; }

        // Room under the text for the progress bar, so long goals never run into it.
        private const int TextBottom = 20;
        private const int TextLineSpacing = -2;

        /// <summary>The largest size, from 18 down to 12, at which the goal text fits on its note.</summary>
        private static int FitFontSize(string text, Vector2 size)
        {
            var width = size.X - 24 - 12;
            var height = size.Y - 10 - TextBottom;
            var font = BingoFonts.Note;
            for (var fontSize = 18; fontSize > 12; fontSize--)
            {
                var measured = font.GetMultilineStringSize(text, HorizontalAlignment.Center, width, fontSize);
                var lines = Math.Max(1, (int)Math.Round(measured.Y / font.GetHeight(fontSize)));
                if (measured.Y + TextLineSpacing * (lines - 1) <= height)
                {
                    return fontSize;
                }
            }
            return 12;
        }

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


        /// <summary>
        /// Marks a square that would finish a line: null for none, "" for this board's player (or team),
        /// or a Lockout rival's name. It pulses gently so it's easy to spot.
        /// </summary>
        public void SetOneAway(string? who)
        {
            var show = who != null && !_filled && !_isTaken;
            if (show)
            {
                var ink = who!.Length == 0 ? BingoStyle.OneAwayInk : BingoStyle.RivalOneAwayInk;
                _oneAwayText.Text = who.Length == 0 ? "1 AWAY!" : $"{(who.Length > 9 ? who[..8] + "…" : who).ToUpperInvariant()} 1 AWAY";
                _oneAwayText.AddThemeColorOverride("font_color", ink);
                if (_oneAway.GetThemeStylebox("panel") is StyleBoxFlat box)
                {
                    box.BorderColor = ink;
                }
            }
            if (show == _oneAway.Visible)
            {
                return;
            }
            _oneAway.Visible = show;
            _oneAwayPulse?.Kill();
            _oneAwayPulse = null;
            _oneAway.Scale = Vector2.One;
            if (show)
            {
                BingoStyle.CenterPivot(_oneAway);
                _oneAwayPulse = _oneAway.CreateTween().SetLoops();
                _oneAwayPulse.TweenProperty(_oneAway, "scale", new Vector2(1.08f, 1.08f), 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                _oneAwayPulse.TweenProperty(_oneAway, "scale", Vector2.One, 0.6).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
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
