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
    public static readonly Color Ink = new(0.22f, 0.17f, 0.13f);

    /// <summary>One color per difficulty 1-5: teal, blue, purple, orange, red.</summary>
    public static Color Difficulty(int difficulty) => difficulty switch
    {
        <= 1 => new Color(0.30f, 0.80f, 0.68f),
        2 => new Color(0.33f, 0.62f, 0.95f),
        3 => new Color(0.68f, 0.50f, 0.96f),
        4 => new Color(0.97f, 0.58f, 0.28f),
        _ => new Color(0.95f, 0.33f, 0.43f),
    };

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

    /// <summary>A label with the game's chunky outlined look.</summary>
    public static Label Text(string text, int size, Color? color = null, int outline = 6)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Colors.White);
        label.AddThemeColorOverride("font_outline_color", Outline);
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
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
