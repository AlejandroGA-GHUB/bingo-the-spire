using Godot;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The bingo settings (spec 4.5): the board hotkey, colorblind-safe colors, and putting the bingo
/// button back in its spot. Opened from the lobby's bingo panel; saved to settings.json at once.
/// </summary>
internal static class SettingsBook
{
    private const float ColumnWidth = 360;
    private const float SpineGap = 100;
    private const string Name = "BingoTheSpireSettings";

    public static void Show(Control parent, BingoSettings settings)
    {
        foreach (var old in parent.GetChildren().Where(c => c.Name == Name))
        {
            old.QueueFree();
        }
        BookFrame? book = null;
        book = new BookFrame(parent, Name, ColumnWidth * 2 + SpineGap, backdrop: true, onClosed: () => book?.Root.QueueFree());
        var content = book.Content;
        content.AddThemeConstantOverride("separation", 14);
        content.AddChild(BoardPanel.Nameplate("SETTINGS", null));

        var spread = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        spread.AddThemeConstantOverride("separation", (int)SpineGap);
        content.AddChild(spread);
        var left = Column();
        var right = Column();
        spread.AddChild(left);
        spread.AddChild(right);

        // Left page: the hotkey.
        left.AddChild(Heading("Board hotkey"));
        left.AddChild(Body("Opens and closes the board during a run. Click the key, then press the one you want.", 16, BingoStyle.PageInk));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        left.AddChild(row);
        var keyButton = HistoryBook.Button(KeyText(settings.Hotkey), () => { });
        keyButton.FocusMode = Control.FocusModeEnum.All;
        row.AddChild(keyButton);
        var offButton = HistoryBook.Button("Turn off", () => { });
        row.AddChild(offButton);
        var keyNote = Body("", 15, BingoStyle.PageInkSoft);
        left.AddChild(keyNote);
        left.AddChild(Body("Changes apply from your next run.", 14, BingoStyle.PageInkSoft));
        left.AddChild(Spacer(6));
        left.AddChild(Heading("Controller"));
        left.AddChild(Body(Gamepad.PickBoardButton() != null
            ? $"{Gamepad.BoardButtonName} opens the board. In the bingo books, move with the stick or d-pad, Accept picks, Back closes."
            : "Every controller button is taken by the game: open the board from the bingo button in the top bar. In the bingo books, move with the stick or d-pad, Accept picks, Back closes.", 16, BingoStyle.PageInk));

        var listening = false;
        keyButton.Pressed += () =>
        {
            listening = true;
            keyButton.Text = "Press a key…";
            keyNote.Text = "Esc to cancel.";
            keyButton.GrabFocus();
        };
        keyButton.GuiInput += input =>
        {
            if (!listening || input is not InputEventKey { Pressed: true, Echo: false } key)
            {
                return;
            }
            keyButton.AcceptEvent();
            listening = false;
            var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
            if (code == Key.Escape)
            {
                keyNote.Text = "";
            }
            else if (code is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.None)
            {
                keyNote.Text = "Pick a plain key, without Shift, Ctrl or Alt.";
            }
            else if (BingoHotkey.FindConflict(code) is { } used)
            {
                keyNote.Text = $"{OS.GetKeycodeString(code)} is already used by the game ({Pretty(used)}). Pick another key.";
            }
            else
            {
                settings.SetHotkey(code.ToString());
                keyNote.Text = $"Saved: {OS.GetKeycodeString(code)} opens the board.";
            }
            keyButton.Text = KeyText(settings.Hotkey);
            keyButton.ReleaseFocus();
        };
        offButton.Pressed += () =>
        {
            listening = false;
            settings.SetHotkey(null);
            keyButton.Text = KeyText(null);
            keyNote.Text = "Hotkey off: use the bingo button to open the board.";
        };

        // Right page: colors and the bingo button.
        right.AddChild(Heading("Colorblind-safe colors"));
        right.AddChild(Body("Difficulty tags, mini-boards and the 1 AWAY stamps use colors that stay apart with red-green colorblindness. Tags always show their number too.", 16, BingoStyle.PageInk));
        var colors = HistoryBook.Button(OnOff(settings.ColorblindSafe), () => { });
        colors.Pressed += () =>
        {
            settings.SetColorblindSafe(!settings.ColorblindSafe);
            BingoStyle.ColorblindSafe = settings.ColorblindSafe;
            colors.Text = OnOff(settings.ColorblindSafe);
        };
        right.AddChild(colors);
        right.AddChild(Body("Boards already open update when they're next drawn.", 14, BingoStyle.PageInkSoft));
        right.AddChild(Spacer(10));

        right.AddChild(Heading("Bingo button"));
        right.AddChild(Body("Drag the button during a run to move it. Right-click it, or use this, to put it back by the run timer.", 16, BingoStyle.PageInk));
        var reset = HistoryBook.Button("Reset position", () => { });
        reset.Pressed += () =>
        {
            settings.SetIconPosition(null);
            reset.Text = "Done!";
        };
        right.AddChild(reset);

        var where = Body($"Saved in {BingoSettings.FilePath}", 13, BingoStyle.PageInkSoft);
        where.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        where.CustomMinimumSize = new Vector2(ColumnWidth * 2 + SpineGap, 0);
        where.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(where);
        book.Show();
    }

    private static string KeyText(string? key) =>
        key != null && Enum.TryParse<Key>(key, ignoreCase: true, out var k) ? $"Key: {OS.GetKeycodeString(k)}" : "Key: off";

    private static string OnOff(bool on) => on ? "On" : "Off";

    /// <summary>"ui_accept" → "ui accept", "mega_view_deck" → "view deck".</summary>
    private static string Pretty(string action) => action.Replace("mega_", "").Replace('_', ' ');

    private static VBoxContainer Column()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        return column;
    }

    private static Label Heading(string text) => BingoStyle.Ink(text, BingoFonts.Heading, 20, BingoStyle.PageInk);

    private static Label Body(string text, int size, Color color)
    {
        var label = BingoStyle.Ink(text, BingoFonts.Note, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(ColumnWidth, 0);
        return label;
    }

    private static Control Spacer(int height) => new() { CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore };
}
