using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>Pop-ups at the top of the screen: a green card for a square, the BINGO! pop-up for a line.</summary>
internal sealed class Toasts
{
    private readonly VBoxContainer _stack;

    public Toasts(Control parent)
    {
        _stack = new VBoxContainer { Name = "BingoToasts", MouseFilter = Control.MouseFilterEnum.Ignore };
        _stack.AddThemeConstantOverride("separation", 10);
        _stack.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _stack.OffsetTop = 130;
        _stack.OffsetLeft = -330;
        _stack.OffsetRight = 330;
        parent.AddChild(_stack);
    }

    /// <summary>A square was filled: a green card with a white "SQUARE COMPLETE!" label on its top edge.</summary>
    public void Square(string goal, string evidence)
    {
        // The label overlaps the card's top edge, drawn on top of it.
        var toast = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        toast.AddThemeConstantOverride("separation", -TabOverlap);
        _stack.AddChild(toast);

        var tab = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 1, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        tab.AddThemeStyleboxOverride("panel", TabStyle);
        tab.AddChild(BingoStyle.Ink("SQUARE COMPLETE!", BingoFonts.Heading, 22, TabInk));
        toast.AddChild(tab);

        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = BingoStyle.Box(new Color(0.08f, 0.32f, 0.17f, 0.97f), 14, 3, BingoStyle.FilledBorder, 16, BingoStyle.FilledGlow, 18);
        box.ContentMarginTop = 16 + TabOverlap;
        card.AddThemeStyleboxOverride("panel", box);
        toast.AddChild(card);

        var column = Column(card);
        column.AddChild(Centered(BingoStyle.Text(goal, 24, Colors.White, outline: 7), wrap: true));
        if (evidence.Length > 0)
        {
            column.AddChild(Centered(BingoStyle.Text(evidence, 15, BingoStyle.Muted, outline: 4)));
        }
        Show(toast, hold: 3.0, popFrom: 0.7f);
    }

    private const int TabOverlap = 18;
    private static readonly Color TabInk = Color.FromHtml("#111111");
    private static readonly StyleBoxFlat TabStyle = MakeTabStyle();

    private static StyleBoxFlat MakeTabStyle()
    {
        var box = BingoStyle.Box(Colors.White, 12, 4, TabInk, 0);
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        box.ContentMarginTop = 3;
        box.ContentMarginBottom = 5;
        box.ShadowColor = TabInk;
        box.ShadowSize = 1;
        box.ShadowOffset = new Vector2(0, 4);
        box.AntiAliasing = true;
        return box;
    }

    /// <summary>A line was made: the BINGO! pop-up (see <see cref="BingoPop"/>). The board marks which line.</summary>
    public void Bingo() => BingoPop.Play(_stack);

    /// <summary>Accent for other players' moves in Lockout.</summary>
    public static readonly Color Rival = new(0.95f, 0.42f, 0.5f);

    /// <summary>A plain announcement: a title in the accent color and a line of text.</summary>
    public void Notice(string title, string text, Color accent, bool big = false)
    {
        var card = Card(accent.Darkened(0.72f) with { A = 0.97f }, accent, accent with { A = 0.45f }, big ? 26 : 16);
        var column = Column(card);
        column.AddChild(Centered(BingoStyle.Text(title, big ? 44 : 18, accent.Lightened(0.25f), outline: big ? 10 : 5, font: BingoFonts.Heading)));
        column.AddChild(Centered(BingoStyle.Text(text, big ? 20 : 22, Colors.White, outline: 6), wrap: true));
        Show(card, hold: big ? 4.5 : 3.0, popFrom: big ? 0.5f : 0.7f);
    }

    private PanelContainer Card(Color background, Color border, Color glow, int glowSize)
    {
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", BingoStyle.Box(background, 14, 3, border, 16, glow, glowSize));
        _stack.AddChild(card);
        return card;
    }

    private static VBoxContainer Column(PanelContainer card)
    {
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        card.AddChild(column);
        return column;
    }

    private static Label Centered(Label label, bool wrap = false)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(600, 0);
        }
        return label;
    }

    private static void Show(Control card, double hold, float popFrom)
    {
        BingoStyle.CenterPivot(card);
        card.Scale = new Vector2(popFrom, popFrom);
        card.Modulate = new Color(1, 1, 1, 0);
        var tween = card.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(card, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "modulate:a", 1.0, 0.2);
        tween.Chain().TweenInterval(hold);
        tween.Chain().TweenProperty(card, "modulate:a", 0.0, 0.5);
        tween.Chain().TweenCallback(Callable.From(card.QueueFree));
    }
}
