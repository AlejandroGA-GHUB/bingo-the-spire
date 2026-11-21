using MegaCrit.Sts2.Core.Commands;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The game's own menu sounds, so our books sound like the rest of the game. Playing a sound
/// changes nothing in the run; if the game's audio isn't there, it just stays quiet.
/// </summary>
internal static class Sfx
{
    public const string Click = "event:/sfx/ui/clicks/ui_click";
    public const string Hover = "event:/sfx/ui/clicks/ui_hover";
    public const string Back = "event:/sfx/ui/clicks/ui_back";
    public const string TickOn = "event:/sfx/ui/clicks/ui_checkbox_on";
    public const string TickOff = "event:/sfx/ui/clicks/ui_checkbox_off";

    private static bool _broken;

    public static void Play(string? sound)
    {
        if (sound == null || _broken)
        {
            return;
        }
        try
        {
            SfxCmd.Play(sound);
        }
        catch (Exception ex)
        {
            _broken = true;
            BingoLog.Warn($"couldn't play the game's menu sounds, staying quiet: {ex.Message}");
        }
    }
}
