using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// Draws an open book, with pages that bow up from the spine, onto any control, at the control's
/// real size. It's all vector drawing, so it stays sharp at every resolution. The curves are a
/// fixed depth relative to the width, so <see cref="Insets"/> clears them at any height.
/// </summary>
internal static class BookArt
{
    private const float CurveDepth = 0.055f; // of the width
    private const int Samples = 64;

    private static readonly Color CoverLight = Color.FromHtml("#7a4128");
    private static readonly Color CoverMid = Color.FromHtml("#5a2e1c");
    private static readonly Color CoverDark = Color.FromHtml("#3a1c11");
    private static readonly Color CoverEdge = Color.FromHtml("#2a130b");
    private static readonly Color PageEdge = Color.FromHtml("#a8946d");
    private static readonly Color StackEdge = Color.FromHtml("#8d7a58");
    private static readonly Color Spine = Color.FromHtml("#7d6a48") with { A = 0.6f };

    // Page shading from the outer edge (0) to the spine (1).
    private static readonly (float At, Color Color)[] PageStops =
    [
        (0f, Color.FromHtml("#eadcbc")), (0.05f, Color.FromHtml("#fbf5e6")), (0.8f, Color.FromHtml("#f9f1de")),
        (0.95f, Color.FromHtml("#e5d6b4")), (1f, Color.FromHtml("#c9b58c")),
    ];

    private static readonly (float Dx, float Dy, Color Color)[] Stack =
    [
        (16, 18, Color.FromHtml("#b9a57d")), (11, 12, Color.FromHtml("#d3c19b")), (6, 6, Color.FromHtml("#e6d7b5")),
    ];

    /// <summary>Space to leave around the book's content: left/right, top, bottom.</summary>
    public static (float Side, float Top, float Bottom) Insets(float width) =>
        (0.065f * width, CurveDepth * width + 34, CurveDepth * width + 48);

    /// <summary>The book width that fits content of this width between the side insets.</summary>
    public static float WidthFor(float contentWidth) => contentWidth / 0.87f;

    /// <summary>Draws the book on <paramref name="target"/> whenever it redraws (under its children).</summary>
    public static void Attach(Control target)
    {
        target.Draw += () => Paint(target, target.Size);
        target.Resized += target.QueueRedraw;
    }

    private static void Paint(CanvasItem canvas, Vector2 size)
    {
        float w = size.X, h = size.Y;
        if (w < 40 || h < 40)
        {
            return;
        }
        var d = CurveDepth * w;
        var mid = w / 2;

        // Soft shadow under the book.
        var cover = Cover(w, h, d);
        for (var i = 3; i >= 1; i--)
        {
            canvas.DrawColoredPolygon(Offset(cover, new Vector2(0, 6 * i)), new Color(0, 0, 0, 0.12f));
        }

        var coverColors = cover.Select(p => Gradient3(CoverLight, CoverMid, CoverDark, (p.X / w + p.Y / h) / 2)).ToArray();
        canvas.DrawPolygon(cover, coverColors);
        Outline(canvas, cover, CoverEdge, 2);

        foreach (var left in new[] { true, false })
        {
            foreach (var (dx, dy, color) in Stack)
            {
                var layer = Page(w, h, d, left, dx, dy);
                canvas.DrawColoredPolygon(layer.Outline, color);
                Outline(canvas, layer.Outline, StackEdge, 1);
            }

            var page = Page(w, h, d, left, 0, 0);
            var outer = page.Top[^1].X;
            for (var i = 0; i < Samples; i++)
            {
                var quad = new[] { page.Top[i], page.Top[i + 1], page.Bottom[i + 1], page.Bottom[i] };
                var colors = quad.Select(p => PageShade(1 - Math.Abs(p.X - outer) / Math.Abs(mid - outer))).ToArray();
                canvas.DrawPolygon(quad, colors);
            }
            Outline(canvas, page.Outline, PageEdge, 1.2f);
        }

        canvas.DrawLine(new Vector2(mid, 14 + d), new Vector2(mid, h - 30), Spine, 1.5f, antialiased: true);
    }

    private sealed record PageShape(Vector2[] Top, Vector2[] Bottom, Vector2[] Outline);

    /// <summary>One page, as matching top and bottom curves sampled from the spine outwards.</summary>
    private static PageShape Page(float w, float h, float d, bool left, float dx, float dy)
    {
        float top = 14, bot = h - 30, mid = w / 2;
        float ox = 0.034f * w - dx, c1 = 0.392f * w - dx, c2 = 0.172f * w - dx;
        float X(float x) => left ? x : w - x;

        var topCurve = Sample(new(X(mid), top + d + dy), new(X(c1), top + 0.13f * d + dy), new(X(c2), top + dy), new(X(ox), top + 0.61f * d + dy));
        var bottomCurve = Sample(new(X(mid), bot + dy), new(X(c1), bot - 0.93f * d + dy), new(X(c2), bot - d + dy), new(X(ox), bot - 0.45f * d + dy));
        return new PageShape(topCurve, bottomCurve, topCurve.Concat(Enumerable.Reverse(bottomCurve)).ToArray());
    }

    private static Vector2[] Cover(float w, float h, float d)
    {
        float top = 14, mid = w / 2;
        var points = new List<Vector2>();
        points.AddRange(Sample(new(mid, top - 4 + 0.75f * d), new(0.38f * w, top - 14), new(0.15f * w, top - 18), new(6, top - 14 + 0.55f * d)));
        points.AddRange(Sample(new(6, h - 6 - 0.55f * d), new(0.15f * w, h - 6 - d), new(0.38f * w, h - 6 - 0.9f * d), new(mid, h - 2)).Skip(0));
        points.AddRange(Sample(new(mid, h - 2), new(0.62f * w, h - 6 - 0.9f * d), new(0.85f * w, h - 6 - d), new(w - 6, h - 6 - 0.55f * d)).Skip(1));
        points.AddRange(Sample(new(w - 6, top - 14 + 0.55f * d), new(0.85f * w, top - 18), new(0.62f * w, top - 14), new(mid, top - 4 + 0.75f * d)).SkipLast(1));
        return points.ToArray();
    }

    private static Vector2[] Sample(Vector2 a, Vector2 b, Vector2 c, Vector2 e)
    {
        var points = new Vector2[Samples + 1];
        for (var i = 0; i <= Samples; i++)
        {
            var t = (float)i / Samples;
            var u = 1 - t;
            points[i] = u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * e;
        }
        return points;
    }

    private static Vector2[] Offset(Vector2[] points, Vector2 by) => points.Select(p => p + by).ToArray();

    /// <summary>A thin antialiased edge, which also smooths the filled shape's own edge.</summary>
    public static void Outline(CanvasItem canvas, Vector2[] points, Color color, float width) =>
        canvas.DrawPolyline([.. points, points[0]], color, width, antialiased: true);

    private static Color PageShade(float toSpine)
    {
        toSpine = Math.Clamp(toSpine, 0, 1);
        for (var i = 1; i < PageStops.Length; i++)
        {
            if (toSpine <= PageStops[i].At)
            {
                var (a, ca) = PageStops[i - 1];
                var (b, cb) = PageStops[i];
                return ca.Lerp(cb, (toSpine - a) / Math.Max(0.0001f, b - a));
            }
        }
        return PageStops[^1].Color;
    }

    private static Color Gradient3(Color a, Color b, Color c, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5f ? a.Lerp(b, t * 2) : b.Lerp(c, (t - 0.5f) * 2);
    }
}
