using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>Colors and style helpers shared by the bingo UI.</summary>
internal static class BingoStyle
{
    public static readonly Color PanelBg = new(0.07f, 0.08f, 0.13f, 0.97f);
    public static readonly Color PanelBorder = new(0.93f, 0.74f, 0.29f);
    public static readonly Color Gold = new(1f, 0.83f, 0.30f);
    public static readonly Color GoldGlow = new(1f, 0.78f, 0.2f, 0.55f);
    public static readonly Color Filled = new(0.16f, 0.62f, 0.34f);
    public static readonly Color FilledBorder = new(0.49f, 0.94f, 0.63f);
    public static readonly Color FilledGlow = new(0.35f, 0.95f, 0.5f, 0.45f);
    public static readonly Color Muted = new(0.76f, 0.78f, 0.85f);
    public static readonly Color Outline = new(0, 0, 0, 0.85f);

    // Icon (parchment card, like the game's top bar art)
    public static readonly Color Parchment = new(0.91f, 0.85f, 0.73f);
    public static readonly Color ParchmentDark = new(0.74f, 0.66f, 0.52f);
    public static readonly Color IconInk = new(0.22f, 0.17f, 0.13f);

    // The book look: paper, ink and brass.
    public static readonly Color Paper = Color.FromHtml("#fffcf1");
    public static readonly Color PaperTaken = Color.FromHtml("#e9e3d5");
    public static readonly Color PageInk = Color.FromHtml("#2a221b");
    public static readonly Color PageInkSoft = Color.FromHtml("#6b5d4c");
    public static readonly Color RedInk = Color.FromHtml("#c8352b");
    public static readonly Color TakenInk = Color.FromHtml("#b03a48");
    public static readonly Color Brass = Color.FromHtml("#d9a441");
    public static readonly Color BrassDark = Color.FromHtml("#7a531b");
    public static readonly Color NameplateText = Color.FromHtml("#3b2508");
    public static readonly Color Cream = Color.FromHtml("#fff8e3");
    public static readonly Color Tape = new(0.95f, 0.91f, 0.79f, 0.72f);
    public static readonly Color On = Color.FromHtml("#2f8f5b");
    public static readonly Color Off = Color.FromHtml("#b23a3a");
    public static readonly Color Unpicked = Color.FromHtml("#b9ad97");

    /// <summary>One color per difficulty 1-5: teal, blue, purple, orange, red.</summary>
    /// <summary>Use the colorblind-safe palette (Okabe-Ito colors), from the player's settings.</summary>
    public static bool ColorblindSafe { get; set; }

    /// <summary>Filled squares on mini-boards.</summary>
    public static Color MiniFilled => ColorblindSafe ? Color.FromHtml("#0072b2") : Filled;

    /// <summary>Squares in a finished line on mini-boards.</summary>
    public static Color MiniLine => ColorblindSafe ? Color.FromHtml("#e69f00") : Gold;

    /// <summary>"1 AWAY!" stamps: yours, and a Lockout rival's.</summary>
    public static Color OneAwayInk => ColorblindSafe ? Color.FromHtml("#d55e00") : Color.FromHtml("#d9730d");

    public static Color RivalOneAwayInk => ColorblindSafe ? Color.FromHtml("#7a3f8f") : TakenInk;

    public static Color Difficulty(int difficulty) => ColorblindSafe ? SafeDifficulty(difficulty) : difficulty switch
    {
        <= 1 => new Color(0.30f, 0.80f, 0.68f),
        2 => new Color(0.33f, 0.62f, 0.95f),
        3 => new Color(0.68f, 0.50f, 0.96f),
        4 => new Color(0.97f, 0.58f, 0.28f),
        _ => new Color(0.95f, 0.33f, 0.43f),
    };

    /// <summary>Light to dark as well as different hues, so they stay apart without color too.</summary>
    private static Color SafeDifficulty(int difficulty) => Color.FromHtml(difficulty switch
    {
        <= 1 => "#56b4e9",
        2 => "#009e73",
        3 => "#0072b2",
        4 => "#e69f00",
        _ => "#cc79a7",
    });

    public static StyleBoxFlat Box(Color background, int radius, int border, Color borderColor, int padding,
        Color? glow = null, int glowSize = 0)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = borderColor };
        box.SetCornerRadiusAll(radius);
        box.SetBorderWidthAll(border);
        box.SetContentMarginAll(padding);
        if (glow is { } g)
        {
            box.ShadowColor = g;
            box.ShadowSize = glowSize;
        }
        return box;
    }

    /// <summary>A label with the game's chunky outlined look (for text over the game, like toasts).</summary>
    public static Label Text(string text, int size, Color? color = null, int outline = 6, Font? font = null)
    {
        var label = BingoFonts.Keep(new Label { Text = text });
        label.AddThemeFontOverride("font", font ?? BingoFonts.NoteBold);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }

    /// <summary>Ink on paper: no outline.</summary>
    public static Label Ink(string text, Font font, int size, Color color)
    {
        var label = BingoFonts.Keep(new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore });
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>A flat box with no fill, e.g. to remove a control's default look.</summary>
    public static StyleBoxEmpty Empty(int padding = 0)
    {
        var box = new StyleBoxEmpty();
        box.SetContentMarginAll(padding);
        return box;
    }

    /// <summary>Scales a control around its center, now and whenever it's resized.</summary>
    public static void CenterPivot(Control control)
    {
        control.PivotOffset = control.Size / 2;
        control.Resized += () => control.PivotOffset = control.Size / 2;
    }

    /// <summary>A quick "pop": grow a little and settle back.</summary>
    public static void Pulse(Control control, float amount = 1.12f)
    {
        CenterPivot(control);
        var tween = control.CreateTween();
        tween.TweenProperty(control, "scale", new Vector2(amount, amount), 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
