using System.Diagnostics;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Mod.GameAdapter;
using BingoTheSpire.Mod.Ui;

namespace BingoTheSpire.Mod;

/// <summary>
/// Entry point. The game's mod loader finds this class by its <see cref="ModInitializerAttribute"/>
/// and calls <see cref="Initialize"/> once while loading mods.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "BingoTheSpire";

    public static string Version { get; } =
        typeof(ModEntry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    internal static Harmony? Harmony { get; private set; }

    internal static EventBus? Events { get; private set; }

    internal static BingoRuntime? Bingo { get; private set; }

    internal static BingoSettings? Settings { get; private set; }

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
            Settings = BingoSettings.Load();
            Bingo = new BingoRuntime(Settings);
            Bingo.SessionStarted += session => Callable.From(() =>
                BingoOverlay.Current?.Bind(session, Bingo.LocalPlayerId)).CallDeferred();
            Bingo.SessionStopped += () => Callable.From(() => BingoOverlay.Current?.Unbind()).CallDeferred();
            Bingo.RunFinished += entry => Callable.From(() => BingoOverlay.Current?.ShowResults(entry)).CallDeferred();
            Events.Subscribe(Bingo.OnEvent);
            Adapter.RunAttaching += Events.Reset;
            Adapter.Install(Events);

            Harmony = new Harmony(ModId);
            ApplyPatches(Harmony);
            Adapter.LogPatchSummary();

            BingoLog.Info($"v{Version} loaded. Data folder: {DataPaths.Root}");
        }
        catch (Exception ex)
        {
            // Never take the game down with us: log and leave bingo off.
            BingoLog.Error($"failed to initialize, bingo is off for this session:\n{ex}");
        }
    }

    /// <summary>
    /// Applies each patch class on its own, so one patch failing (e.g. after a game update) only
    /// loses that patch's events instead of every patch after it.
    /// </summary>
    private static void ApplyPatches(Harmony harmony)
    {
        foreach (var type in typeof(ModEntry).Assembly.GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0)
            {
                continue;
            }
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                Adapter.ReportPatch(type.Name, false);
                BingoLog.Error($"patch {type.Name} failed and is skipped: {ex.Message}");
            }
        }
    }
}
