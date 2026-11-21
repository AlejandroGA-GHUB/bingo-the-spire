using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// Controller support (spec 4.5): a free controller button for the board, focus navigation in our
/// books, and Back / Esc closing them. Uses the game's own hotkey manager to keep its hotkeys from
/// firing while a book is open. Everything here fails quietly: the mouse always still works.
/// </summary>
internal static class Gamepad
{
    private static readonly string[] CancelActions = ["ui_cancel", "mega_pause_and_back", "mega_back"];
    private static readonly string[] AcceptActions = ["ui_accept", "ui_confirm", "ui_select"];

    /// <summary>Buttons tried for the board, in order; the first one the game doesn't use wins.</summary>
    private static readonly JoyButton[] BoardButtons = [JoyButton.Back, JoyButton.RightStick, JoyButton.LeftStick, JoyButton.Misc1];

    /// <summary>True while the player is using a controller (or the arrow keys) rather than the mouse.</summary>
    public static bool InUse
    {
        get
        {
            try
            {
                return NControllerManager.Instance?.IsUsingDirectionalNavigation ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The controller button that opens the board, if one is free.</summary>
    public static JoyButton? BoardButton { get; private set; }

    /// <summary>Picks a controller button for the board that no game action already uses.</summary>
    public static JoyButton? PickBoardButton()
    {
        BoardButton = null;
        foreach (var button in BoardButtons)
        {
            var used = InputMap.GetActions().Any(action => action != BingoHotkey.ActionName
                && InputMap.ActionGetEvents(action).Any(e => e is InputEventJoypadButton j && j.ButtonIndex == button));
            if (!used)
            {
                BoardButton = button;
                break;
            }
        }
        return BoardButton;
    }

    /// <summary>"Select", "R3", …: a name for the board button as players know it.</summary>
    public static string? BoardButtonName => BoardButton switch
    {
        JoyButton.Back => "Select / View",
        JoyButton.RightStick => "R3",
        JoyButton.LeftStick => "L3",
        JoyButton.Misc1 => "Share",
        _ => null,
    };

    /// <summary>
    /// While a book is open: the game's hotkeys are held back, and Back / Esc closes the book.
    /// Undone by <see cref="Release"/>, or when the book's node leaves the tree.
    /// </summary>
    public static void Capture(Node owner, Action close)
    {
        if (!owner.IsInsideTree() || owner.HasMeta(CapturedMeta))
        {
            return;
        }
        owner.SetMeta(CapturedMeta, true);
        try
        {
            NHotkeyManager.Instance?.AddBlockingScreen(owner);
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't hold back the game's hotkeys: {ex.Message}");
        }

        var tree = owner.GetTree();
        void Poll()
        {
            if (!GodotObject.IsInstanceValid(owner) || !owner.HasMeta(CapturedMeta))
            {
                tree.ProcessFrame -= Poll;
                return;
            }
            if (CancelActions.Any(a => InputMap.HasAction(a) && Input.IsActionJustPressed(a)))
            {
                close();
            }
        }
        tree.ProcessFrame += Poll;
        owner.TreeExiting += () => Release(owner);
    }

    public static void Release(Node owner)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.HasMeta(CapturedMeta))
        {
            return;
        }
        owner.RemoveMeta(CapturedMeta);
        try
        {
            NHotkeyManager.Instance?.RemoveBlockingScreen(owner);
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't give the game its hotkeys back: {ex.Message}");
        }
    }

    private const string CapturedMeta = "bingothespire_captured";

    /// <summary>With a controller in use, puts focus on the first thing in <paramref name="root"/> that takes it.</summary>
    public static void FocusFirst(Control root, Control? fallback = null)
    {
        if (!InUse)
        {
            return;
        }
        var first = FindFocusable(root) ?? fallback;
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static Control? FindFocusable(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control { Visible: true } control)
            {
                if (control.FocusMode == Control.FocusModeEnum.All && control is not LineEdit)
                {
                    return control;
                }
                if (FindFocusable(control) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    /// <summary>The gold ring drawn around whatever the controller is on.</summary>
    public static readonly StyleBoxFlat FocusRing = MakeFocusRing();

    private static StyleBoxFlat MakeFocusRing()
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = BingoStyle.Gold, AntiAliasing = true };
        box.SetBorderWidthAll(3);
        box.SetCornerRadiusAll(10);
        box.SetExpandMarginAll(4);
        return box;
    }

    private static readonly StyleBoxEmpty NoRing = new();

    /// <summary>
    /// Lets a button be reached with the controller, with a gold ring when focused, and gives it the
    /// game's hover and click sounds (<paramref name="click"/> null for buttons that play their own).
    /// </summary>
    public static T Focusable<T>(T button, string? click = Sfx.Click) where T : BaseButton
    {
        button.FocusMode = Control.FocusModeEnum.All;
        button.AddThemeStyleboxOverride("focus", FocusRing);
        // A mouse click shouldn't leave a ring behind, so the ring only shows while using a controller.
        // Never drop the focus here instead: a button that loses focus mid-click cancels the click.
        button.FocusEntered += () =>
        {
            button.AddThemeStyleboxOverride("focus", InUse ? FocusRing : NoRing);
            if (InUse)
            {
                Sfx.Play(Sfx.Hover);
            }
        };
        button.MouseEntered += () =>
        {
            if (!button.Disabled)
            {
                Sfx.Play(Sfx.Hover);
            }
        };
        button.Pressed += () => Sfx.Play(click);
        return button;
    }

    /// <summary>
    /// Lets a clickable control (a note, a preview card, the icon) be reached with the controller:
    /// a gold ring while focused, and Accept does what a click does.
    /// </summary>
    public static void Focusable(Control control, Action accept)
    {
        control.FocusMode = Control.FocusModeEnum.All;
        Panel? ring = null;
        control.FocusEntered += () =>
        {
            if (!InUse)
            {
                control.CallDeferred(Control.MethodName.ReleaseFocus);
                return;
            }
            ring ??= MakeRing(control);
            ring.Visible = true;
            Sfx.Play(Sfx.Hover);
        };
        control.MouseEntered += () => Sfx.Play(Sfx.Hover);
        control.FocusExited += () =>
        {
            if (ring != null && GodotObject.IsInstanceValid(ring))
            {
                ring.Visible = false;
            }
        };
        control.GuiInput += input =>
        {
            if (AcceptActions.Any(a => InputMap.HasAction(a) && input.IsActionPressed(a)) && input is not InputEventMouse)
            {
                control.AcceptEvent();
                Sfx.Play(Sfx.Click);
                accept();
            }
            else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Sfx.Play(Sfx.Click);
            }
        };
    }

    private static Panel MakeRing(Control control)
    {
        var ring = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 5 };
        ring.AddThemeStyleboxOverride("panel", FocusRing);
        ring.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.AddChild(ring);
        return ring;
    }
}
