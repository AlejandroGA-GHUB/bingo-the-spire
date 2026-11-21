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

        _art = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
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
    public void SetTag(Color color, string text)
    {
        _tag = color;
        _tagText = text;
        _art.QueueRedraw();
    }

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
    }

    /// <summary>Lifts the note off the page a little (e.g. the picked mode).</summary>
    public void SetLifted(bool lifted)
    {
        var lift = lifted && !_onSpine;
        Paper.Rotation = lift || _onSpine ? 0 : _tilt;
        Paper.Scale = lift ? new Vector2(1.04f, 1.04f) : Vector2.One;
        Paper.Position = lift ? new Vector2(0, -4) : Vector2.Zero;
        _style.ShadowSize = lift ? 10 : 6;
        _style.ShadowOffset = lift ? new Vector2(0, 8) : new Vector2(0, _onSpine ? 3 : 4);
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
            // Darker toward the middle, like paper bending into the fold.
            (float At, Color Color)[] stops =
            [
                (0f, BingoStyle.Paper), (0.36f, BingoStyle.Paper), (0.46f, Color.FromHtml("#ecdfc0")),
                (0.5f, Color.FromHtml("#d6c49c")), (0.54f, Color.FromHtml("#ecdfc0")), (0.64f, BingoStyle.Paper), (1f, BingoStyle.Paper),
            ];
            for (var i = 1; i < stops.Length; i++)
            {
                float x0 = stops[i - 1].At * size.X, x1 = stops[i].At * size.X;
                _art.DrawPolygon(
                    [new(x0, 0), new(x1, 0), new(x1, size.Y), new(x0, size.Y)],
                    [stops[i - 1].Color, stops[i].Color, stops[i].Color, stops[i - 1].Color]);
            }
        }

        if (_tag is { } tag)
        {
            Vector2[] corner = [new(0, 0), new(TagSize, 0), new(0, TagSize)];
            _art.DrawColoredPolygon(corner, tag);
            _art.DrawLine(new Vector2(TagSize, 0), new Vector2(0, TagSize), tag, 1.2f, antialiased: true);
            if (_tagText.Length > 0)
            {
                var font = BingoFonts.NoteBold;
                _art.DrawString(font, new Vector2(6, 19), _tagText, HorizontalAlignment.Left, -1, 16, new Color(0, 0, 0, 0.25f));
                _art.DrawString(font, new Vector2(5, 18), _tagText, HorizontalAlignment.Left, -1, 16, Colors.White);
            }
        }
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
