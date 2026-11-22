using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The BINGO! pop-up: a box with an ink outline, filled with a warm gold glow rising from the bottom
/// and faint light rays turning slowly behind the letters. The letters stamp down one after another; when the last one lands, stars pop around
/// the box and the letters bob in a wave. Then it floats up and fades. All drawn in code.
/// </summary>
internal static class BingoPop
{
    private const string Word = "BINGO!";
    private const int FontSize = 56;
    private const float PadX = 30;
    private const float PadY = 14;
    private const float Corner = 18;
    private const float Border = 4;
    private const int Rays = 20;
    private const double RayTurn = 14; // seconds per full turn

    private const double StampStart = 0.12;
    private const double StampStep = 0.085;
    private const double StampTime = 0.24;
    private const double BobStep = 0.09;
    private const double BobTime = 0.62;
    private const double Total = 3.3;

    private static readonly Color Ink = BingoStyle.PageInk;
    private static readonly Color GlowBottom = Color.FromHtml("#ffe9a8");
    private static readonly Color GlowMiddle = Color.FromHtml("#fffaf0");
    private static readonly Color GlowTop = Color.FromHtml("#fffdf8");
    private static readonly Color RayColor = new(0.965f, 0.769f, 0.267f, 0.14f);
    private static readonly Color LetterOutline = Color.FromHtml("#6b4512");
    private static readonly Color LetterShadow = Color.FromHtml("#3b2508");
    private static readonly Color StarFill = Color.FromHtml("#f6c444");

    /// <summary>Spots for the stars, as fractions of the box size.</summary>
    private static readonly Vector2[] StarSpots =
        [new(-0.14f, 0.06f), new(1.04f, -0.04f), new(-0.08f, 0.84f), new(1.00f, 0.86f), new(0.48f, -0.18f), new(0.52f, 1.04f)];

    /// <summary>Adds the pop-up to <paramref name="stack"/> and plays it; it frees itself when done.</summary>
    public static void Play(Container stack)
    {
        var font = BingoFonts.Heading;
        var widths = Word.Select(c => font.GetStringSize(c.ToString(), HorizontalAlignment.Left, -1, FontSize).X + 1).ToArray();
        var letterHeight = font.GetHeight(FontSize);
        var size = new Vector2(widths.Sum() + PadX * 2, letterHeight + PadY * 2);

        // The stack lays out its children and resets their scale, so the pop moves freely inside a fixed slot.
        var slot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, size.Y + 24) };
        stack.AddChild(slot);

        var pop = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = size, PivotOffset = size / 2 };
        pop.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        pop.OffsetLeft = -size.X / 2;
        pop.OffsetRight = size.X / 2;
        pop.OffsetTop = 8;
        pop.OffsetBottom = 8 + size.Y;
        slot.AddChild(pop);

        var box = BuildBox(pop, size);
        var letters = BuildLetters(box, widths, letterHeight, font);
        var stars = StarSpots.Select(spot => Star(box, spot * size)).ToList();

        Animate(slot, pop, box, letters, stars);
    }

    private static Control BuildBox(Control pop, Vector2 size)
    {
        var box = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = size, PivotOffset = size / 2 };
        pop.AddChild(box);

        // Hard ink shadow under the box, plus a soft one.
        var shadow = Panel(Ink, size, filled: true);
        shadow.Position = new Vector2(0, 6);
        if (shadow.GetThemeStylebox("panel") is StyleBoxFlat soft)
        {
            soft.ShadowColor = new Color(0, 0, 0, 0.45f);
            soft.ShadowSize = 14;
            soft.ShadowOffset = new Vector2(0, 6);
        }
        box.AddChild(shadow);

        // Card that clips the glow and the turning rays to its rounded shape.
        var card = Panel(GlowTop, size, filled: true);
        card.ClipChildren = CanvasItem.ClipChildrenMode.AndDraw;
        box.AddChild(card);

        // Gold glow rising from the bottom middle: an ellipse 70% of the width and 120% of the height,
        // gold in the middle, fading to warm white by 60% of the way out.
        var glow = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = size };
        glow.Draw += () =>
        {
            var center = new Vector2(size.X / 2, size.Y);
            var radius = new Vector2(size.X * 0.7f, size.Y * 1.2f);
            (float At, Color Color)[] stops = [(0f, GlowBottom), (0.6f, GlowMiddle), (1f, GlowTop)];
            const int segments = 48;
            for (var s = 1; s < stops.Length; s++)
            {
                for (var i = 0; i < segments; i++)
                {
                    float a0 = Mathf.Pi + Mathf.Pi * i / segments, a1 = Mathf.Pi + Mathf.Pi * (i + 1) / segments;
                    Vector2 Point(float at, float angle) => center + new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y) * at;
                    glow.DrawPolygon(
                        [Point(stops[s - 1].At, a0), Point(stops[s].At, a0), Point(stops[s].At, a1), Point(stops[s - 1].At, a1)],
                        [stops[s - 1].Color, stops[s].Color, stops[s].Color, stops[s - 1].Color]);
                }
            }
        };
        card.AddChild(glow);

        // Faint gold rays from the middle of the box, turning slowly.
        var rays = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Position = size / 2 };
        var reach = size.Length();
        rays.Draw += () =>
        {
            for (var i = 0; i < Rays; i++)
            {
                var a0 = Mathf.Tau * i / Rays;
                var a1 = a0 + Mathf.Tau / Rays / 2;
                rays.DrawColoredPolygon(
                    [Vector2.Zero, new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * reach, new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * reach],
                    RayColor);
            }
        };
        card.AddChild(rays);
        rays.CreateTween().SetLoops().TweenProperty(rays, "rotation", Mathf.Tau, RayTurn).From(0f);

        box.AddChild(Panel(Ink, size, filled: false));
        return box;
    }

    private static Panel Panel(Color color, Vector2 size, bool filled)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            DrawCenter = filled,
            BorderColor = Ink,
            AntiAliasing = true,
        };
        style.SetCornerRadiusAll((int)Corner);
        if (!filled)
        {
            style.SetBorderWidthAll((int)Border);
        }
        var panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Size = size };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static List<Label> BuildLetters(Control box, float[] widths, float height, Font font)
    {
        var letters = new List<Label>();
        var x = PadX;
        for (var i = 0; i < Word.Length; i++)
        {
            var label = BingoFonts.Keep(new Label
            {
                Text = Word[i].ToString(),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = new Vector2(x, PadY),
                Size = new Vector2(widths[i], height),
                PivotOffset = new Vector2(widths[i] / 2, height / 2),
            });
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", FontSize);
            label.AddThemeColorOverride("font_color", BingoStyle.Gold);
            label.AddThemeColorOverride("font_outline_color", LetterOutline);
            label.AddThemeConstantOverride("outline_size", 14);
            label.AddThemeColorOverride("font_shadow_color", LetterShadow);
            label.AddThemeConstantOverride("shadow_offset_x", 0);
            label.AddThemeConstantOverride("shadow_offset_y", 4);
            label.AddThemeConstantOverride("shadow_outline_size", 14);
            box.AddChild(label);
            letters.Add(label);
            x += widths[i];
        }
        return letters;
    }

    /// <summary>A small gold star with a brown outline, centered on <paramref name="center"/>.</summary>
    private static Control Star(Control parent, Vector2 center)
    {
        const float size = 22;
        var star = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(size, size),
            Position = center - new Vector2(size, size) / 2,
            PivotOffset = new Vector2(size, size) / 2,
            Scale = Vector2.Zero,
            Modulate = new Color(1, 1, 1, 0),
        };
        var points = Enumerable.Range(0, 10).Select(i =>
        {
            var angle = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
            var radius = i % 2 == 0 ? size / 2 - 1 : size / 5;
            return new Vector2(size / 2 + Mathf.Cos(angle) * radius, size / 2 + Mathf.Sin(angle) * radius);
        }).ToArray();
        star.Draw += () =>
        {
            star.DrawColoredPolygon(points, StarFill);
            star.DrawPolyline([.. points, points[0]], LetterOutline, 1.8f, antialiased: true);
        };
        parent.AddChild(star);
        return star;
    }

    private static void Animate(Control slot, Control pop, Control box, List<Label> letters, List<Control> stars)
    {
        // The whole pop: fade in, hold, then float up and fade out.
        pop.Modulate = new Color(1, 1, 1, 0);
        var whole = pop.CreateTween();
        whole.TweenProperty(pop, "modulate:a", 1.0, Total * 0.06);
        whole.TweenInterval(Total * 0.72);
        whole.TweenProperty(pop, "position:y", pop.Position.Y - 22, Total * 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        whole.Parallel().TweenProperty(pop, "modulate:a", 0.0, Total * 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        whole.TweenCallback(Callable.From(slot.QueueFree));

        // The box pops open.
        box.Scale = new Vector2(0.6f, 0.6f);
        box.CreateTween().TweenProperty(box, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // Each letter stamps down, then bobs once the last one has landed.
        var landed = StampStart + letters.Count * StampStep + 0.16;
        for (var i = 0; i < letters.Count; i++)
        {
            var letter = letters[i];
            var home = letter.Position;
            letter.Scale = new Vector2(2.4f, 2.4f);
            letter.Modulate = new Color(1, 1, 1, 0);
            letter.Position = home + new Vector2(0, -10);

            var stamp = letter.CreateTween();
            stamp.TweenInterval(StampStart + i * StampStep);
            stamp.TweenProperty(letter, "scale", new Vector2(0.9f, 0.9f), StampTime * 0.7).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            stamp.Parallel().TweenProperty(letter, "modulate:a", 1.0, StampTime * 0.5);
            stamp.Parallel().TweenProperty(letter, "position", home, StampTime * 0.7).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            stamp.TweenProperty(letter, "scale", Vector2.One, StampTime * 0.3);

            var bob = letter.CreateTween();
            bob.TweenInterval(landed + i * BobStep);
            for (var n = 0; n < 2; n++)
            {
                bob.TweenProperty(letter, "position:y", home.Y - 9, BobTime / 2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                bob.TweenProperty(letter, "position:y", home.Y, BobTime / 2).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }

        // Stars pop around the box as the bounce starts.
        for (var i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            var sparkle = star.CreateTween();
            sparkle.TweenInterval(landed - 0.06 + i * 0.07);
            sparkle.TweenProperty(star, "scale", new Vector2(1.2f, 1.2f), 0.32).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            sparkle.Parallel().TweenProperty(star, "rotation", Mathf.Pi / 2, 0.32);
            sparkle.Parallel().TweenProperty(star, "modulate:a", 1.0, 0.32);
            sparkle.TweenProperty(star, "scale", new Vector2(0.6f, 0.6f), 0.48);
            sparkle.Parallel().TweenProperty(star, "rotation", Mathf.Pi * 0.9f, 0.48);
            sparkle.Parallel().TweenProperty(star, "modulate:a", 0.0, 0.48);
        }
    }
}
