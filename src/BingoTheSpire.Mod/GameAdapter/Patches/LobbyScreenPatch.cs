using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using BingoTheSpire.Mod.Ui;

namespace BingoTheSpire.Mod.GameAdapter.Patches;

/// <summary>
/// Adds the Bingo button to every screen where a run is set up: character select (solo and co-op),
/// custom run, and beta's extra modes. They all open through NSubmenu.OnSubmenuOpened and expose a
/// public <c>Lobby</c>; matching that by name covers screens that only exist on one branch.
/// The postfix only adds our own controls to the screen.
/// </summary>
[HarmonyPatch]
[PatchTarget(typeof(NSubmenu), "OnSubmenuOpened")]
internal static class LobbyScreenPatch
{
    private const string Source = "NSubmenu.OnSubmenuOpened";

    private static MethodBase? TargetMethod() => PatchTargetAttribute.Resolve(typeof(LobbyScreenPatch));

    private static bool Prepare()
    {
        var found = TargetMethod() != null;
        Adapter.ReportPatch(Source, found);
        return found;
    }

    private static void Postfix(NSubmenu __instance) =>
        Adapter.Safe(Source, () =>
        {
            var lobby = __instance.GetType().GetProperty("Lobby", BindingFlags.Public | BindingFlags.Instance)?.GetValue(__instance);
            if (lobby is StartRunLobby runLobby && ModEntry.Settings is { } settings)
            {
                // Subclasses finish setting up after calling the base method: add our controls next frame.
                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(__instance))
                    {
                        Adapter.Safe(Source, () => LobbyPanel.Attach(__instance, runLobby, settings));
                    }
                }).CallDeferred();
            }
        });
}
