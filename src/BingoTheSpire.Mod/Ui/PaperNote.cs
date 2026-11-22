using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// A paper note taped into the book: a slightly tilted card with a strip of tape, an optional
/// colored corner (the difficulty), and a crease when it lies across the spine.
/// <see cref="Slot"/> goes into a container; containers reset rotation, so the tilted paper sits
/// inside it.
/// </summary>
internal sealed class PaperNote
{
    private const float TagSize = 40;

    private readonly Control _art;
    private readonly Panel _tape;
    private readonly StyleBoxFlat _style;
    private readonly float _tilt;
    private Color? _tag;
    private string _tagText = "";
    private float _tagSize = TagSize;
    private bool _onSpine;

    public PaperNote(Vector2 size, float tiltDegrees)
    {
        _tilt = Mathf.DegToRad(tiltDegrees);
        Slot = new Control { CustomMinimumSize = size, MouseFilter = Control.MouseFilterEnum.Pass };

        _style = new StyleBoxFlat
        {
            BgColor = BingoStyle.Paper,
            ShadowColor = new Color(0, 0, 0, 0.32f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(0, 4),
            AntiAliasing = true,
        };
        _style.SetCornerRadiusAll(3);
        Paper = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Rotation = _tilt };
        Paper.AddThemeStyleboxOverride("panel", _style);
        Paper.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Slot.AddChild(Paper);
        BingoStyle.CenterPivot(Paper);

        // Filtered with mipmaps, so the corner tag stays smooth however the note is tilted or scaled.
        _art = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
        _art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _art.Draw += PaintArt;
        Paper.AddChild(_art);

        var tapeStyle = new StyleBoxFlat { BgColor = BingoStyle.Tape, BorderColor = new Color(0, 0, 0, 0.07f), AntiAliasing = true };
        tapeStyle.SetBorderWidthAll(1);
        _tape = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(46, 18) };
        _tape.AddThemeStyleboxOverride("panel", tapeStyle);
        _tape.PivotOffset = _tape.Size / 2;
        Paper.AddChild(_tape);
        Paper.Resized += PlaceTape;
        PlaceTape();

        Content = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        SetMargins(left: 14, top: 14, right: 12, bottom: 12);
        Paper.AddChild(Content);
    }

    /// <summary>The control to put in a grid or row.</summary>
    public Control Slot { get; }

    /// <summary>The paper itself (tilted). Extra marks like stamps go directly on it.</summary>
    public Panel Paper { get; }

    /// <summary>Where the note's text goes, inside the margins.</summary>
    public MarginContainer Content { get; }

    public void SetMargins(int left, int top, int right, int bottom)
    {
        Content.AddThemeConstantOverride("margin_left", left);
        Content.AddThemeConstantOverride("margin_top", top);
        Content.AddThemeConstantOverride("margin_right", right);
        Content.AddThemeConstantOverride("margin_bottom", bottom);
    }

    /// <summary>The colored corner in the top left, with a number in it.</summary>
    public void SetTag(Color color, string text, float size = TagSize)
    {
        _tag = color;
        _tagText = text;
        _tagSize = size;
        _art.QueueRedraw();
    }

    /// <summary>For small notes that sit in a row of choices rather than taped to the page.</summary>
    public void HideTape() => _tape.Visible = false;

    /// <summary>Lies across the spine: no tilt, a crease down the middle.</summary>
    public void SetOnSpine(bool onSpine)
    {
        _onSpine = onSpine;
        Paper.Rotation = onSpine ? 0 : _tilt;
        PlaceTape();
        _style.ShadowOffset = onSpine ? new Vector2(0, 3) : new Vector2(0, 4);
        _art.QueueRedraw();
    }

    public void SetPaperColor(Color color) => _style.BgColor = color;

    /// <summary>A colored outline (e.g. gold for a square in a finished line), or none.</summary>
    public void SetBorder(Color? color, int width = 3)
    {
        _style.BorderColor = color ?? Colors.Transparent;
        _style.SetBorderWidthAll(color == null ? 0 : width);
        _border = color == null ? 0 : width;
        _art.QueueRedraw(); // the spine shading leaves room for the border
    }

    private int _border;

    /// <summary>Lifts the note off the page a little (e.g. the picked mode).</summary>
    public void SetLifted(bool lifted)
    {
        var lift = lifted && !_onSpine;
        // Small notes (e.g. difficulty choices) lift half as far, or they look like they float away.
        var small = Slot.CustomMinimumSize.Y < 80;
        Paper.Rotation = lift || _onSpine ? 0 : _tilt;
        Paper.Scale = lift ? new Vector2(small ? 1.02f : 1.04f, small ? 1.02f : 1.04f) : Vector2.One;
        Paper.Position = lift ? new Vector2(0, small ? -2 : -4) : Vector2.Zero;
        _style.ShadowSize = lift ? (small ? 8 : 10) : 6;
        _style.ShadowOffset = lift ? new Vector2(0, small ? 6 : 8) : new Vector2(0, _onSpine ? 3 : 4);
        PlaceTape();
    }

    private void PlaceTape()
    {
        _tape.Position = new Vector2((Paper.Size.X - _tape.Size.X) / 2, -9);
        _tape.Rotation = -2 * Paper.Rotation;
    }

    private void PaintArt()
    {
        var size = _art.Size;
        if (_onSpine)
        {
            // Darker toward the middle, like paper bending into the fold. A see-through warm tan (on
            // white paper it gives the fold's original #ecdfc0 / #d6c49c), so any paper color, e.g. a
            // taken square's grey, shows through. Kept inside the border so a picked note on the spine
            // still shows its outline.
            var fold = Color.FromHtml("#98701c");
            Color Shade(float alpha) => new(fold, alpha);
            (float At, Color Color)[] stops =
            [
                (0f, Shade(0)), (0.36f, Shade(0)), (0.46f, Shade(0.2f)),
                (0.5f, Shade(0.4f)), (0.54f, Shade(0.2f)), (0.64f, Shade(0)), (1f, Shade(0)),
            ];
            float b = _border, top = b, bottom = size.Y - b, width = size.X - 2 * b;
            for (var i = 1; i < stops.Length; i++)
            {
                float x0 = b + stops[i - 1].At * width, x1 = b + stops[i].At * width;
                _art.DrawPolygon(
                    [new(x0, top), new(x1, top), new(x1, bottom), new(x0, bottom)],
                    [stops[i - 1].Color, stops[i].Color, stops[i].Color, stops[i - 1].Color]);
            }
        }

        if (_tag is { } tag)
        {
            // One smooth shape: a triangle that follows the paper's rounded corner, with a soft edge.
            DrawTag(_art, _tagSize, tag);
            if (_tagText.Length > 0)
            {
                var font = BingoFonts.NoteBold;
                _art.DrawString(font, new Vector2(6, 19), _tagText, HorizontalAlignment.Left, -1, 16, new Color(0, 0, 0, 0.25f));
                _art.DrawString(font, new Vector2(5, 18), _tagText, HorizontalAlignment.Left, -1, 16, Colors.White);
            }
        }
    }

    private const int MaskScale = 4;
    private const float MaskPad = 2;
    private const float CornerRadius = 3;
    private static readonly Dictionary<int, ImageTexture?> TagMasks = new();

    /// <summary>
    /// The corner tag's shape as a white mask, drawn 4× larger and shrunk with mipmaps so its edges are
    /// smooth at any tilt. A transparent margin round it keeps the outer edges soft too (the edge falls
    /// inside the drawn rectangle, where the filtering smooths it).
    /// </summary>
    /// <summary>
    /// Draws a smooth difficulty triangle in <paramref name="canvas"/>'s top-left corner. The canvas
    /// should filter with mipmaps (<see cref="CanvasItem.TextureFilterEnum.LinearWithMipmaps"/>).
    /// </summary>
    public static void DrawTag(CanvasItem canvas, float size, Color color)
    {
        if (TagMask(size) is { } mask)
        {
            canvas.DrawTextureRect(mask, new Rect2(-MaskPad, -MaskPad, size + MaskPad, size + MaskPad), false, color);
        }
    }

    private static ImageTexture? TagMask(float size)
    {
        var key = (int)Math.Round(size);
        if (TagMasks.TryGetValue(key, out var cached))
        {
            return cached;
        }
        ImageTexture? texture = null;
        try
        {
            var n = (int)Math.Ceiling((size + MaskPad) * MaskScale);
            var image = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
            for (var j = 0; j < n; j++)
            {
                for (var i = 0; i < n; i++)
                {
                    // Texel centre in note pixels, with the paper's corner at (0, 0).
                    var x = (i + 0.5f) / MaskScale - MaskPad;
                    var y = (j + 0.5f) / MaskScale - MaskPad;
                    var inside = x >= 0 && y >= 0 && x + y <= size;
                    if (inside && x < CornerRadius && y < CornerRadius)
                    {
                        var dx = x - CornerRadius;
                        var dy = y - CornerRadius;
                        inside = dx * dx + dy * dy <= CornerRadius * CornerRadius;
                    }
                    image.SetPixel(i, j, inside ? Colors.White : new Color(1, 1, 1, 0));
                }
            }
            image.GenerateMipmaps();
            texture = ImageTexture.CreateFromImage(image);
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't draw a note's corner tag: {ex.Message}");
        }
        TagMasks[key] = texture;
        return texture;
    }

    /// <summary>A rubber stamp (e.g. DONE, PICKED): heading font in a rounded box, tilted.</summary>
    public static PanelContainer Stamp(string text, Color color, int fontSize, float tiltDegrees)
    {
        var box = new StyleBoxFlat { BgColor = BingoStyle.Paper with { A = 0.85f }, BorderColor = color, AntiAliasing = true };
        box.SetBorderWidthAll(3);
        box.SetCornerRadiusAll(6);
        box.ContentMarginLeft = box.ContentMarginRight = 7;
        box.ContentMarginTop = 1;
        box.ContentMarginBottom = 2;
        var stamp = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Rotation = Mathf.DegToRad(tiltDegrees), Modulate = new Color(1, 1, 1, 0.9f) };
        stamp.AddThemeStyleboxOverride("panel", box);
        stamp.AddChild(BingoStyle.Ink(text, BingoFonts.Heading, fontSize, color));
        BingoStyle.CenterPivot(stamp);
        return stamp;
    }

    /// <summary>Pins a control to a corner of the paper, growing inward from it.</summary>
    public static void PinTo(Control child, Control.LayoutPreset corner, float x, float y)
    {
        child.SetAnchorsPreset(corner);
        var right = corner is Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight;
        var bottom = corner is Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight;
        child.GrowHorizontal = right ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        child.GrowVertical = bottom ? Control.GrowDirection.Begin : Control.GrowDirection.End;
        child.OffsetLeft = child.OffsetRight = right ? -x : x;
        child.OffsetTop = child.OffsetBottom = bottom ? -y : y;
    }
}
