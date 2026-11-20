using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The board hotkey, added as our own input action and registered with the game's hotkey
/// manager like the game's own hotkeys. It's only turned on if no other binding (the game's or
/// another mod's) already uses the key, so it can never steal an existing control.
/// </summary>
internal static class BingoHotkey
{
    public const string ActionName = "bingothespire_toggle_board";

    public static Key? ActiveKey { get; private set; }

    /// <summary>Sets up the input action. Runs at the start of every run, so it sees the current key bindings.</summary>
    public static void Configure(string? keyName)
    {
        if (InputMap.HasAction(ActionName))
        {
            InputMap.EraseAction(ActionName);
        }
        ActiveKey = null;

        if (keyName == null)
        {
            BingoLog.Info("board hotkey is off (settings); use the Bingo button");
            return;
        }
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key) || key == Key.None)
        {
            BingoLog.Warn($"board hotkey '{keyName}' isn't a key name Godot knows; hotkey is off");
            return;
        }
        if (FindConflict(key) is { } other)
        {
            BingoLog.Info($"board hotkey {key} is already used by '{other}', so it's off. Pick another key in {BingoSettings.FilePath}");
            return;
        }

        InputMap.AddAction(ActionName);
        InputMap.ActionAddEvent(ActionName, new InputEventKey { Keycode = key });
        ActiveKey = key;
    }

    /// <summary>Hooks the action to <paramref name="toggle"/>. The game clears all hotkeys when a run starts, so call this per run.</summary>
    public static void Bind(Action toggle)
    {
        if (ActiveKey != null)
        {
            NHotkeyManager.Instance?.PushHotkeyPressedBinding(ActionName, toggle);
        }
    }

    /// <summary>Name of an action already bound to the plain key (no modifiers), if any.</summary>
    private static string? FindConflict(Key key)
    {
        foreach (var action in InputMap.GetActions())
        {
            if (action == ActionName)
            {
                continue;
            }
            foreach (var evt in InputMap.ActionGetEvents(action))
            {
                if (evt is InputEventKey k
                    && (k.Keycode == key || k.PhysicalKeycode == key)
                    && !k.CtrlPressed && !k.AltPressed && !k.ShiftPressed && !k.MetaPressed)
                {
                    return action;
                }
            }
        }
        return null;
    }
}
