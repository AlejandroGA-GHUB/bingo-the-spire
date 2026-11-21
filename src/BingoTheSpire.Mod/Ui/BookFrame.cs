using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// A window drawn as an open book: content laid out on its pages, a brass close button on the
/// corner, centered on screen and shrunk to fit small windows. Laid out by hand rather than in a
/// container, because containers reset scale.
/// </summary>
internal sealed class BookFrame
{
    private readonly float _contentWidth;
    private readonly Action _onClosed;
    private readonly Button _close;
    private readonly ColorRect? _backdrop;
    private float _scale = 1;
    private bool _layoutQueued;

    // Books opened from another book (Settings or Past runs from the lobby) cover the one below:
    // it's hidden until the new one closes, then comes back.
    private static readonly List<BookFrame> OpenBooks = [];
    private BookFrame? _under;
    private bool _covered;

    /// <param name="backdrop">Dim the screen behind the book, and close when it's clicked.</param>
    public BookFrame(Control parent, string name, float contentWidth, bool backdrop, Action? onClosed = null)
    {
        _contentWidth = contentWidth;
        _onClosed = onClosed ?? (() => { });

        Root = new Control { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Theme = BingoFonts.Theme };
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(Root);

        if (backdrop)
        {
            _backdrop = new ColorRect { Color = new Color(0.03f, 0.03f, 0.06f, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
            _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _backdrop.GuiInput += evt =>
            {
                if (evt is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    Hide();
                }
            };
            Root.AddChild(_backdrop);
        }

        Book = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        BookArt.Attach(Book);
        Root.AddChild(Book);

        Content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Content.AddThemeConstantOverride("separation", 12);
        Book.AddChild(Content);
        Content.MinimumSizeChanged += QueueLayout;

        _close = BingoFonts.Keep(new Button
        {
            Text = "✕",
            FocusMode = Control.FocusModeEnum.All,
            TooltipText = "Close",
            Size = new Vector2(46, 46),
                    });
        _close.AddThemeFontOverride("font", BingoFonts.NoteBold);
        _close.AddThemeFontSizeOverride("font_size", 22);
        foreach (var (state, color) in new[] { ("normal", BingoStyle.Brass), ("hover", BingoStyle.Brass.Lightened(0.15f)), ("pressed", BingoStyle.Brass.Darkened(0.1f)) })
        {
            var box = BingoStyle.Box(color, 23, 2, BingoStyle.BrassDark, 0, new Color(0, 0, 0, 0.35f), 4);
            box.ShadowOffset = new Vector2(0, 3);
            box.AntiAliasing = true;
            _close.AddThemeStyleboxOverride(state, box);
        }
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            _close.AddThemeColorOverride(state, BingoStyle.NameplateText);
        }
        _close.AddThemeStyleboxOverride("focus", Gamepad.FocusRing);
        _close.Pressed += () =>
        {
            Sfx.Play(Sfx.Back);
            Hide();
        };
        _close.MouseEntered += () => Sfx.Play(Sfx.Hover);
        Book.AddChild(_close);

        Root.Resized += QueueLayout;
        QueueLayout();
    }

    /// <summary>Full-screen holder; lets clicks through around the book unless there's a backdrop.</summary>
    public Control Root { get; }

    /// <summary>The book (drawn art), sized to its content.</summary>
    public Control Book { get; }

    /// <summary>What's written on the pages, top to bottom.</summary>
    public VBoxContainer Content { get; }

    public bool Visible => Root.Visible;

    /// <summary>True while a book with a backdrop (lobby, settings, results, history) is open on top.</summary>
    public static bool AnyBackdropOpen => OpenBooks.Any(b => GodotObject.IsInstanceValid(b.Root) && b.Root.Visible);

    /// <summary>Page insets for this book's width: content placed with these clears the curves.</summary>
    public (float Side, float Top, float Bottom) Insets => BookArt.Insets(BookArt.WidthFor(_contentWidth));

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
        if (_backdrop != null && !OpenBooks.Contains(this))
        {
            OpenBooks.RemoveAll(b => !GodotObject.IsInstanceValid(b.Root));
            _under = OpenBooks.LastOrDefault(b => b._backdrop != null && b.Root.Visible);
            if (_under != null)
            {
                _under._covered = true;
                _under.Root.Visible = false;
            }
            OpenBooks.Add(this);
            // In front of everything else on the screen, the bingo icon included.
            Root.GetParent()?.MoveChild(Root, -1);
        }
        Root.Visible = true;
        Layout();
        Gamepad.Capture(Root, Hide);
        Gamepad.FocusFirst(Content, _close);
        Book.Modulate = new Color(1, 1, 1, 0);
        var settle = Book.Position;
        Book.Position = settle + new Vector2(0, 18);
        var tween = Book.CreateTween().SetParallel();
        tween.TweenProperty(Book, "position", settle, 0.24).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(Book, "modulate:a", 1.0, 0.16);
    }

    public void Hide()
    {
        if (!Root.Visible && !_covered)
        {
            return;
        }
        Root.Visible = false;
        _covered = false;
        OpenBooks.Remove(this);
        Gamepad.Release(Root);
        _onClosed();

        // Bring back the book this one covered. Deferred, so the Back press that closed this book
        // isn't seen by the one below in the same frame.
        var under = _under;
        _under = null;
        if (under != null)
        {
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(under.Root) && under._covered)
                {
                    under._covered = false;
                    under.Root.Visible = true;
                    Gamepad.FocusFirst(under.Content, under._close);
                }
            }).CallDeferred();
        }
    }

    private void QueueLayout()
    {
        if (_layoutQueued)
        {
            return;
        }
        _layoutQueued = true;
        Callable.From(() =>
        {
            _layoutQueued = false;
            if (GodotObject.IsInstanceValid(Book))
            {
                Layout();
            }
        }).CallDeferred();
    }

    private void Layout()
    {
        var width = BookArt.WidthFor(_contentWidth);
        var (side, top, bottom) = BookArt.Insets(width);
        var contentHeight = Content.GetCombinedMinimumSize().Y;
        Content.Position = new Vector2(side, top);
        Content.Size = new Vector2(_contentWidth, contentHeight);
        var size = new Vector2(width, top + contentHeight + bottom);
        Book.Size = size;
        _close.Position = new Vector2(width - 52, 2);

        var screen = Root.Size.X > 0 ? Root.Size : Root.GetViewportRect().Size;
        _scale = Math.Min(1f, Math.Min((screen.X - 32) / size.X, (screen.Y - 32) / size.Y));
        Book.Scale = new Vector2(_scale, _scale);
        Book.Position = ((screen - size * _scale) / 2).Floor();
        Book.QueueRedraw();
    }
}
