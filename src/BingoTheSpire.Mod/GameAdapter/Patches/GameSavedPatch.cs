using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Mod.GameAdapter.Patches;

/// <summary>
/// Marks the points the game saves the run, which are where Continue resumes from. Every run save
/// goes through SaveManager.SaveRun. The postfix runs as the save starts, when the game takes
/// its snapshot, and only publishes an event; the save itself is untouched.
/// </summary>
[HarmonyPatch]
[PatchTarget(typeof(SaveManager), "SaveRun", typeof(AbstractRoom), typeof(bool))]
internal static class GameSavedPatch
{
    private const string Source = "SaveManager.SaveRun";

    private static MethodBase? TargetMethod() => PatchTargetAttribute.Resolve(typeof(GameSavedPatch));

    private static bool Prepare()
    {
        var found = TargetMethod() != null;
        Adapter.ReportPatch(Source, found, nameof(GameSaved));
        return found;
    }

    private static void Postfix() => Adapter.Safe(Source, Adapter.OnGameSaving);
}
