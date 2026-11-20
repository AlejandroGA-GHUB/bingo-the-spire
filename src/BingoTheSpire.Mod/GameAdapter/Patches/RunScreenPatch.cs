using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using BingoTheSpire.Mod.Ui;

namespace BingoTheSpire.Mod.GameAdapter.Patches;

/// <summary>
/// Adds the bingo UI when the run's screen is ready. A postfix that only adds our own nodes under
/// the run screen; the game's nodes and state are left alone.
/// </summary>
[HarmonyPatch]
[PatchTarget(typeof(NRun), "_Ready")]
internal static class RunScreenPatch
{
    private const string Source = "NRun._Ready";

    private static MethodBase? TargetMethod() => PatchTargetAttribute.Resolve(typeof(RunScreenPatch));

    private static bool Prepare()
    {
        var found = TargetMethod() != null;
        Adapter.ReportPatch(Source, found);
        return found;
    }

    private static void Postfix(NRun __instance) =>
        Adapter.Safe(Source, () =>
        {
            if (ModEntry.Bingo is { } runtime && ModEntry.Settings is { } settings)
            {
                BingoOverlay.Attach(__instance, runtime, settings);
            }
        });
}
