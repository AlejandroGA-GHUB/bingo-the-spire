using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The bingo UI's fonts, embedded in the DLL: Mochiy Pop One for headings, Mali for everything
/// else. Characters they don't have (player names in other scripts, symbols) fall back to the
/// game's own font, so nothing shows up as boxes.
/// </summary>
internal static class BingoFonts
{
    private static Font? _heading;
    private static Font? _note;
    private static Font? _noteBold;
    private static Theme? _theme;

    /// <summary>Titles, the nameplate, stamps and buttons.</summary>
    public static Font Heading => _heading ??= Load("MochiyPopOne-Regular.ttf");

    /// <summary>Goal text and body text.</summary>
    public static Font Note => _note ??= Load("Mali-SemiBold.ttf");

    public static Font NoteBold => _noteBold ??= Load("Mali-Bold.ttf");

    /// <summary>Put on the root of each bingo UI, so every control in it uses Mali unless told otherwise.</summary>
    public static Theme Theme => _theme ??= BuildTheme();

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFont = Note, DefaultFontSize = 18 };
        var paper = new StyleBoxFlat
        {
            BgColor = BingoStyle.Paper,
            BorderColor = BingoStyle.PageInkSoft,
            ShadowColor = new Color(0, 0, 0, 0.35f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(0, 3),
            AntiAliasing = true,
        };
        paper.SetBorderWidthAll(2);
        paper.SetCornerRadiusAll(4);
        paper.SetContentMarginAll(10);
        theme.SetStylebox("panel", "TooltipPanel", paper);
        theme.SetFont("font", "TooltipLabel", NoteBold);
        theme.SetFontSize("font_size", "TooltipLabel", 17);
        theme.SetColor("font_color", "TooltipLabel", BingoStyle.PageInk);
        theme.SetColor("font_shadow_color", "TooltipLabel", Colors.Transparent);
        theme.SetConstant("outline_size", "TooltipLabel", 0);
        return theme;
    }

    /// <summary>
    /// Keeps our fonts on a text control when a font-replacing mod is installed. CznStyleUI swaps
    /// the font on every text control that enters the tree unless it carries this marker. Has to be
    /// set before the control is added to the tree.
    /// </summary>
    public static T Keep<T>(T control) where T : Control
    {
        control.SetMeta("czn_hud_numeric_font", true);
        return control;
    }

    private static Font Load(string file)
    {
        var fallback = GameFont();
        try
        {
            using var stream = typeof(BingoFonts).Assembly.GetManifestResourceStream("BingoTheSpire.Fonts." + file)
                ?? throw new FileNotFoundException(file);
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var font = new FontFile
            {
                Data = bytes,
                Antialiasing = TextServer.FontAntialiasing.Gray,
                Hinting = TextServer.Hinting.Light,
                GenerateMipmaps = true,
            };
            if (fallback != null)
            {
                font.Fallbacks = [fallback];
            }
            return font;
        }
        catch (Exception ex)
        {
            BingoLog.Error($"couldn't load the font {file}, using the game's: {ex.Message}");
            return fallback ?? ThemeDB.FallbackFont;
        }
    }

    private static Font? GameFont()
    {
        try
        {
            return ThemeDB.GetProjectTheme()?.DefaultFont ?? ThemeDB.FallbackFont;
        }
        catch
        {
            return ThemeDB.FallbackFont;
        }
    }
}
