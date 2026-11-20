using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using StS2Bingo.Core.Events;
using StS2Bingo.Mod.GameAdapter;

namespace StS2Bingo.Mod;

/// <summary>
/// Entry point. The game's mod loader finds this class by its <see cref="ModInitializerAttribute"/>
/// and calls <see cref="Initialize"/> once while loading mods.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "StS2Bingo";

    public static string Version { get; } =
        typeof(ModEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    internal static Harmony? Harmony { get; private set; }

    internal static EventBus? Events { get; private set; }

    public static void Initialize()
    {
        try
        {
            var clock = Stopwatch.StartNew();
            Events = new EventBus(
                () => clock.ElapsedMilliseconds,
                (evt, ex) => BingoLog.Error($"handling {evt} failed: {ex}"));

            var recorder = new RunRecorder();
            Events.Subscribe(recorder.OnEvent);
            Adapter.RunAttaching += Events.Reset;
            Adapter.Install(Events);

            Harmony = new Harmony(ModId);
            Harmony.PatchAll(typeof(ModEntry).Assembly);

            BingoLog.Info($"v{Version} loaded. Data folder: {DataPaths.Root}");
        }
        catch (Exception ex)
        {
            // Never take the game down with us: log and leave bingo off.
            BingoLog.Error($"failed to initialize, bingo is off for this session:\n{ex}");
        }
    }
}
