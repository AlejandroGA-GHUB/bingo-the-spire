using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Mod.GameAdapter.Patches;

/// <summary>
/// Reports the end of a run. The game calls RunManager.OnEnded(bool isVictory) on a win, a death and
/// an abandon. Its return type differs between branches, so the target is matched by name and
/// parameters only, and the postfix never touches the return value.
/// </summary>
[HarmonyPatch]
[PatchTarget(typeof(RunManager), "OnEnded", typeof(bool))]
internal static class RunEndedPatch
{
    private const string Source = "RunManager.OnEnded";

    private static MethodBase? TargetMethod() => PatchTargetAttribute.Resolve(typeof(RunEndedPatch));

    private static bool Prepare()
    {
        var found = TargetMethod() != null;
        Adapter.ReportPatch(Source, found, nameof(RunEnded));
        return found;
    }

    private static void Postfix(bool isVictory) =>
        Adapter.Safe(Source, () => Adapter.OnRunEnded(isVictory));
}
