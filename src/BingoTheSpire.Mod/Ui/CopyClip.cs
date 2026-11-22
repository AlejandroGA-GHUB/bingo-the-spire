using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// A small paperclip button placed after a board seed. Pressing it copies the seed to the clipboard
/// and shows a quick green "Copied!" above the clip.
/// </summary>
internal static class CopyClip
{
    private const string Svg =
        "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' width='24' height='24' fill='none' stroke='#{0}' "
        + "stroke-width='2.2' stroke-linecap='round' stroke-linejoin='round'>"
        + "<path d='M21.4 11.1l-9.2 9.2a6 6 0 0 1-8.5-8.5l9.2-9.2a4 4 0 0 1 5.7 5.7l-9.2 9.2a2 2 0 0 1-2.8-2.8l8.5-8.5'/></svg>";

    private static readonly Dictionary<string, Texture2D?> Icons = new();

    public static Button Create(string text, Color color, int size = 18)
    {
        var button = new Button
        {
            Flat = true,
            TooltipText = "Copy board seed",
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(size + 8, size + 8),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseDefaultCursorShape = Control.CursorShape.Arrow, // the game only draws its own cursor for the arrow
            Icon = Icon(color),
        };
        button.AddThemeConstantOverride("icon_max_width", size);
        var hover = BingoStyle.Box(new Color(0, 0, 0, 0.08f), 6, 0, Colors.Transparent, 4);
        button.AddThemeStyleboxOverride("normal", BingoStyle.Empty(4));
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", BingoStyle.Empty(4));
        button.AddThemeColorOverride("icon_normal_color", Colors.White);
        button.AddThemeColorOverride("icon_hover_color", new Color(0.75f, 0.75f, 0.75f));
        button.Pressed += () =>
        {
            try
            {
                DisplayServer.ClipboardSet(text);
                Copied(button);
            }
            catch (Exception ex)
            {
                BingoLog.Warn($"couldn't copy the board seed: {ex.Message}");
            }
        };
        return Gamepad.Focusable(button);
    }

    private static void Copied(Button button)
    {
        var tag = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 20 };
        tag.AddThemeStyleboxOverride("panel", BingoStyle.Box(BingoStyle.On, 5, 0, Colors.Transparent, 0));
        var label = BingoFonts.Keep(BingoStyle.Ink(" Copied! ", BingoFonts.NoteBold, 14, Colors.White));
        tag.AddChild(label);
        button.AddChild(tag);
        var size = tag.GetCombinedMinimumSize();
        tag.Position = new Vector2((button.Size.X - size.X) / 2, -size.Y - 4);
        var tween = tag.CreateTween();
        tween.TweenProperty(tag, "position:y", tag.Position.Y - 6, 0.9).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(tag, "modulate:a", 0.0, 0.4).SetDelay(0.7);
        tween.TweenCallback(Callable.From(tag.QueueFree));
    }

    private static Texture2D? Icon(Color color)
    {
        var key = color.ToHtml(false);
        if (Icons.TryGetValue(key, out var cached))
        {
            return cached;
        }
        Texture2D? texture = null;
        try
        {
            var image = new Image();
            // Drawn at 3× and scaled down by the button, so it stays sharp.
            if (image.LoadSvgFromString(string.Format(System.Globalization.CultureInfo.InvariantCulture, Svg, key), 3f) == Error.Ok)
            {
                texture = ImageTexture.CreateFromImage(image);
            }
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't draw the copy clip: {ex.Message}");
        }
        Icons[key] = texture;
        return texture;
    }
}
