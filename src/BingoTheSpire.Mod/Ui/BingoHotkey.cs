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

    /// <summary>
    /// NHotkeyManager.PushHotkeyPressedBinding(string, Action): beta adds an optional third
    /// parameter, which changes the compiled signature, so it's found by name and its first two
    /// parameters, and any extra ones get their defaults.
    /// </summary>
    private static readonly System.Reflection.MethodInfo? PushBinding = typeof(NHotkeyManager)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .FirstOrDefault(m => m.Name == "PushHotkeyPressedBinding"
            && m.GetParameters() is { Length: >= 2 } p
            && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(Action)
            && p.Skip(2).All(x => x.HasDefaultValue));

    /// <summary>Hooks the action to <paramref name="toggle"/>. The game clears all hotkeys when a run starts, so call this per run.</summary>
    public static void Bind(Action toggle)
    {
        if (ActiveKey == null || NHotkeyManager.Instance is not { } manager)
        {
            return;
        }
        if (PushBinding == null)
        {
            BingoLog.Warn("this game version's hotkey manager is different; the board hotkey is off (the bingo icon still works)");
            return;
        }
        var parameters = PushBinding.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = ActionName;
        args[1] = toggle;
        for (var i = 2; i < args.Length; i++)
        {
            args[i] = parameters[i].DefaultValue;
        }
        PushBinding.Invoke(manager, args);
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
