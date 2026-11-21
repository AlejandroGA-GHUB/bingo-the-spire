using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using BingoTheSpire.Mod.Ui;

namespace BingoTheSpire.Mod.GameAdapter.Patches;

/// <summary>
/// Lets a controller reach the bingo icon in the top bar. NTopBar.UpdateNavigation (private on main,
/// public on beta) sets the focus links of the top bar row; the postfix adds our icon to the ends of
/// that row. It changes only focus links, never the run.
/// </summary>
[HarmonyPatch]
[PatchTarget(typeof(NTopBar), "UpdateNavigation")]
internal static class TopBarNavigationPatch
{
    private const string Source = "NTopBar.UpdateNavigation";

    private static MethodBase? TargetMethod() => PatchTargetAttribute.Resolve(typeof(TopBarNavigationPatch));

    private static bool Prepare()
    {
        var found = TargetMethod() != null;
        Adapter.ReportPatch(Source, found);
        return found;
    }

    private static void Postfix(NTopBar __instance) =>
        Adapter.Safe(Source, () =>
        {
            if (__instance.GetNodeOrNull<Control>(BingoIcon.NodeName) is { } icon)
            {
                Gamepad.JoinTopBar(icon);
            }
        });
}
