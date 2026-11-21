using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>Pop-up cards at the top of the screen: green for a square, big pulsing gold for BINGO.</summary>
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

    public void Square(string goal, string evidence)
    {
        var card = Card(new Color(0.08f, 0.32f, 0.17f, 0.97f), BingoStyle.FilledBorder, BingoStyle.FilledGlow, 18);
        var column = Column(card);
        column.AddChild(Centered(BingoStyle.Text("SQUARE COMPLETE", 16, BingoStyle.Gold, outline: 5, font: BingoFonts.Heading)));
        column.AddChild(Centered(BingoStyle.Text(goal, 24, Colors.White, outline: 7), wrap: true));
        if (evidence.Length > 0)
        {
            column.AddChild(Centered(BingoStyle.Text(evidence, 15, BingoStyle.Muted, outline: 4)));
        }
        Show(card, hold: 3.0, popFrom: 0.7f);
    }

    public void Bingo(int ordinal, string line)
    {
        var first = ordinal == 1;
        var glow = first ? BingoStyle.GoldGlow : new Color(0.75f, 0.5f, 1f, 0.55f);
        var card = Card(first ? new Color(0.55f, 0.36f, 0.05f, 0.98f) : new Color(0.32f, 0.17f, 0.5f, 0.98f),
            first ? BingoStyle.Gold : new Color(0.85f, 0.7f, 1f), glow, 30);
        var column = Column(card);
        column.AddChild(Centered(BingoStyle.Text(first ? "BINGO!" : $"LINE #{ordinal}!", first ? 60 : 40, first ? BingoStyle.Gold : Colors.White, outline: 12, font: BingoFonts.Heading)));
        column.AddChild(Centered(BingoStyle.Text(line, 20, Colors.White, outline: 6)));
        Show(card, hold: first ? 4.5 : 3.5, popFrom: 0.4f);

        // Breathing glow while it's up.
        if (card.GetThemeStylebox("panel") is StyleBoxFlat style)
        {
            var pulse = card.CreateTween().SetLoops(4);
            pulse.TweenProperty(style, "shadow_size", 46, 0.45).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(style, "shadow_size", 22, 0.45).SetTrans(Tween.TransitionType.Sine);
        }
    }

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

    private static void Show(PanelContainer card, double hold, float popFrom)
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
