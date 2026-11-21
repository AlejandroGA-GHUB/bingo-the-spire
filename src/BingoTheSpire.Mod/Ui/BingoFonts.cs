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
    public static Theme Theme => _theme ??= new Theme { DefaultFont = Note, DefaultFontSize = 18 };

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
