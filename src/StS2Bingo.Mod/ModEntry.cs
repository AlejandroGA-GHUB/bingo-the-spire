using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace StS2Bingo.Mod;

/// <summary>
/// Entry point. The game's ModManager finds this class by its <see cref="ModInitializerAttribute"/>
/// and calls <see cref="Initialize"/> once while loading mods.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "StS2Bingo";

    internal static Harmony? Harmony { get; private set; }

    public static void Initialize()
    {
        try
        {
            Harmony = new Harmony(ModId);
            Harmony.PatchAll(typeof(ModEntry).Assembly);
            Log.Info($"[{ModId}] loaded.");
        }
        catch (Exception ex)
        {
            // Never take the game down with us: log and leave bingo disabled.
            Log.Error($"[{ModId}] failed to initialize, bingo is disabled for this session:\n{ex}");
        }
    }
}
