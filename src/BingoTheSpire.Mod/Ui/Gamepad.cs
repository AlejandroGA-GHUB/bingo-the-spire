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

    /// <summary>
    /// True while the game's current input is a controller (it switches back when the mouse or
    /// keyboard is used): the same rule the game uses to show its button glyphs. Read by name, since
    /// the enum's numbers can differ between branches.
    /// </summary>
    public static bool ControllerActive
    {
        get
        {
            try
            {
                return NControllerManager.Instance?.InputType.ToString() == "Controller";
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
            // Only the book on top reacts: one covered by another book is hidden.
            if (owner is CanvasItem { Visible: true } && CancelActions.Any(a => InputMap.HasAction(a) && Input.IsActionJustPressed(a)))
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

    /// <summary>
    /// Adds the bingo icon to the ends of the game's top bar row, so a controller can reach it: the
    /// top panel button (□ / X) focuses the top bar, and moving right from its last element (or left
    /// from its first) lands on the icon. With Steam Input every button is one of the game's actions,
    /// so this is the controller's way to the board. Only focus links change; the game rebuilds them
    /// whenever the top bar changes, and we add the icon again after it.
    /// </summary>
    public static void JoinTopBar(Control icon)
    {
        if (!GodotObject.IsInstanceValid(icon) || icon.GetParent() is not Control bar || !icon.IsVisibleInTree())
        {
            return;
        }
        // The game marks each element of the top bar row by making it its own neighbor above.
        var row = new List<Control>();
        void Collect(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Control control || control == icon)
                {
                    continue;
                }
                if (control.FocusMode == Control.FocusModeEnum.All && control.IsVisibleInTree()
                    && control.FocusNeighborTop.ToString() == control.GetPath().ToString())
                {
                    row.Add(control);
                }
                Collect(control);
            }
        }
        Collect(bar);
        if (row.Count == 0)
        {
            return;
        }
        var first = row.MinBy(c => c.GlobalPosition.X)!;
        var last = row.MaxBy(c => c.GlobalPosition.X)!;
        var path = icon.GetPath();
        last.FocusNeighborRight = path;
        first.FocusNeighborLeft = path;
        icon.FocusNeighborLeft = last.GetPath();
        icon.FocusNeighborRight = first.GetPath();
        icon.FocusNeighborTop = path;
        icon.FocusNeighborBottom = last.FocusNeighborBottom.IsEmpty ? path : last.FocusNeighborBottom;
    }

    /// <summary>
    /// Calls <paramref name="press"/> when the controller's right trigger is pressed and
    /// <paramref name="when"/> allows it, for as long as <paramref name="owner"/> exists. The game
    /// turns the trigger into its "view discard pile" action, which is only used in fights.
    /// </summary>
    public static void OnRightTrigger(Node owner, Func<bool> when, Action press)
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }
        string[] actions = ["controller_right_trigger", "mega_view_discard_pile"];
        void Poll()
        {
            if (!GodotObject.IsInstanceValid(owner))
            {
                tree.ProcessFrame -= Poll;
                return;
            }
            try
            {
                // A keyboard key can also be bound to the game's action: only react while a controller is in use.
                if (ControllerActive && actions.Any(a => InputMap.HasAction(a) && Input.IsActionJustPressed(a)) && when())
                {
                    press();
                }
            }
            catch (Exception ex)
            {
                tree.ProcessFrame -= Poll;
                BingoLog.Warn($"couldn't read the controller's right trigger: {ex.Message}");
            }
        }
        tree.ProcessFrame += Poll;
    }

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

    /// <summary>A ring drawn inside the control, for controls in a clipped box (the ON / OFF switch).</summary>
    public static readonly StyleBoxFlat InsetRing = MakeInsetRing();

    private static StyleBoxFlat MakeInsetRing()
    {
        var box = new StyleBoxFlat { DrawCenter = false, BorderColor = BingoStyle.Gold, AntiAliasing = true };
        box.SetBorderWidthAll(3);
        box.SetCornerRadiusAll(7);
        return box;
    }

    /// <summary>
    /// Lets a button be reached with the controller, with a gold ring when focused, and gives it the
    /// game's hover and click sounds (<paramref name="click"/> null for buttons that play their own).
    /// </summary>
    /// <param name="ring">The focus ring to use, for buttons where the usual one would be clipped.</param>
    public static T Focusable<T>(T button, string? click = Sfx.Click, StyleBox? ring = null) where T : BaseButton
    {
        var focusRing = ring ?? FocusRing;
        button.FocusMode = Control.FocusModeEnum.All;
        button.AddThemeStyleboxOverride("focus", focusRing);
        // A mouse click shouldn't leave a ring behind, so the ring only shows while using a controller.
        // Never drop the focus here instead: a button that loses focus mid-click cancels the click.
        button.FocusEntered += () =>
        {
            button.AddThemeStyleboxOverride("focus", InUse ? focusRing : NoRing);
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
        // Godot buttons only react to ui_accept, but with Steam Input the controller's Accept arrives
        // as the game's ui_confirm / ui_select. ui_accept itself is left to Godot, so nothing fires twice.
        button.GuiInput += input =>
        {
            if (!button.Disabled && input is not InputEventMouse && !input.IsAction("ui_accept")
                && AcceptActions.Any(a => a != "ui_accept" && InputMap.HasAction(a) && input.IsActionPressed(a)))
            {
                button.AcceptEvent();
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
        };
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
