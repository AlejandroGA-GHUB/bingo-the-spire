using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Debug;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.Net;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The bingo settings on the screens where a run is set up (spec 4.1): the bingo icon under the
/// game's version text opens a book with bingo on or off, the bingo-seed box, and in co-op the
/// three modes as notes to pick from. Only the host can change them; everyone else sees the
/// host's choice.
/// </summary>
internal sealed class LobbyPanel
{
    private const float ColumnWidth = 330;
    private const float SpineGap = 100;
    private const float ContentWidth = ColumnWidth * 2 + SpineGap;

    // Lobbies whose player-joined event we already listen to (one per lobby, however often the screen opens).
    private static readonly ConditionalWeakTable<StartRunLobby, object> HookedLobbies = new();

    private static readonly (BingoMode Mode, string Name, string Short, string Text)[] Modes =
    [
        (BingoMode.Versus, "Versus", "Own board each",
            "Everyone fills their own copy of the same board. The first player to finish a line wins. If nobody makes a line before the run ends, whoever filled the most squares wins."),
        (BingoMode.Lockout, "Lockout", "One board, race for squares",
            "One shared board. Complete a goal first and the square is yours. The first line wins. If the run ends with no line, whoever has the most squares wins. If someone gets so far ahead that nobody else can catch up on squares or still make a line, they win right away."),
        (BingoMode.Team, "Team", "One board, together",
            "One shared board, filled together. Counts add up across the team, like potions used or cards drawn in a turn, so their numbers are bigger. One-moment goals, like a single big hit, still need one player. There's no winner: see how many squares and lines you can make together."),
    ];

    private const string SoloText =
        "Goals fill in by themselves as you play. Finish a row, a column or a diagonal for BINGO. Hover a note on the board to see what counted.";

    private readonly BingoSettings _settings;
    private readonly NetRole _role;
    private readonly BingoIcon _icon;
    private readonly BookFrame _book;
    private readonly Button _on;
    private readonly Button _off;
    private readonly LineEdit _seed;
    private readonly Label _seedHint;
    private readonly Label _modeName;
    private readonly Label _modeText;
    private readonly List<(PaperNote Note, PanelContainer Picked)> _modeNotes = [];
    private BingoSetup _shown = BingoSetup.Default;
    private bool _showing;

    private LobbyPanel(Control screen, BingoSettings settings, NetRole role)
    {
        _settings = settings;
        _role = role;
        var editable = role != NetRole.Client;

        _book = new BookFrame(screen, "BingoTheSpireLobbyPanel", ContentWidth, backdrop: true);
        _icon = new BingoIcon(screen, null, _book.Toggle, icon => AboveSeedBox(screen) ?? UnderVersionText(icon));
        _icon.Draw(5, new HashSet<int>(), new HashSet<int>());
        _icon.Root.TooltipText = editable ? "Bingo settings for this run" : "Bingo settings (the host picks these)";

        var content = _book.Content;
        content.AddThemeConstantOverride("separation", 14);
        content.AddChild(BoardPanel.Nameplate("BINGO!", null));

        var spread = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        spread.AddThemeConstantOverride("separation", (int)SpineGap);
        content.AddChild(spread);

        // Left page: on/off and the seed.
        var left = Column();
        spread.AddChild(left);
        left.AddChild(Heading("Bingo this run"));
        var onOff = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        onOff.AddThemeConstantOverride("separation", 12);
        left.AddChild(onOff);
        _on = OnOffButton("ON", editable);
        _off = OnOffButton("OFF", editable);
        _on.Pressed += () => Pick(enabled: true);
        _off.Pressed += () => Pick(enabled: false);
        onOff.AddChild(_on);
        onOff.AddChild(_off);
        left.AddChild(Spacer(8));
        left.AddChild(Heading("Board seed (optional)"));
        _seed = SeedBox(editable);
        _seed.TextChanged += _ => Changed();
        left.AddChild(_seed);
        _seedHint = Body("", 15, BingoStyle.PageInkSoft);
        left.AddChild(_seedHint);

        // Right page: what the chosen mode means.
        var right = Column();
        spread.AddChild(right);
        _modeName = Heading("");
        right.AddChild(_modeName);
        _modeText = Body("", 17, BingoStyle.PageInk);
        right.AddChild(_modeText);
        if (role != NetRole.Solo)
        {
            right.AddChild(Body(editable ? "You're the host: you pick these for everyone." : "The host picks these for everyone.", 15, BingoStyle.PageInkSoft));
        }

        // Bottom: the modes, as notes to pick from (co-op only).
        if (role != NetRole.Solo)
        {
            var title = BingoStyle.Ink(editable ? "Pick a co-op mode" : "Co-op mode", BingoFonts.Heading, 18, BingoStyle.PageInk);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            content.AddChild(title);
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 50);
            content.AddChild(row);
            float[] tilts = [-1.4f, 0.6f, 1.3f];
            for (var i = 0; i < Modes.Length; i++)
            {
                var note = ModeNote(i, tilts[i], editable, out var picked);
                note.SetOnSpine(i == 1);
                row.AddChild(note.Slot);
                _modeNotes.Add((note, picked));
            }
        }

        Show(role == NetRole.Client ? BingoNet.HostSetup ?? BingoSetup.Default : settings.Setup);

        screen.Resized += Place;
        _icon.Root.TreeExiting += () => screen.Resized -= Place;
        screen.GetTree().CreateTimer(0.15).Timeout += Place;
        Place();
    }

    /// <summary>Adds the bingo icon and settings book to a run-setup screen that has just opened.</summary>
    public static void Attach(Control screen, StartRunLobby lobby, BingoSettings settings)
    {
        BingoNet.Use(lobby.NetService);
        var role = BingoNet.Role;

        // The screen is reused between visits: replace our controls rather than stacking them.
        foreach (var old in screen.GetChildren().Where(c => c.Name.ToString().StartsWith("BingoTheSpire", StringComparison.Ordinal)))
        {
            old.QueueFree();
        }
        var panel = new LobbyPanel(screen, settings, role);

        if (role == NetRole.Host)
        {
            if (!HookedLobbies.TryGetValue(lobby, out _))
            {
                HookedLobbies.Add(lobby, new object());
                // Send the settings to each player as they join; everyone already here gets them on every change.
                lobby.PlayerConnected += _ => BingoNet.SendSettings(settings.Setup);
            }
            BingoNet.SendSettings(settings.Setup);
        }
        else if (role == NetRole.Client)
        {
            Action refresh = () => Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(panel._icon.Root))
                {
                    panel.Show(BingoNet.HostSetup ?? BingoSetup.Default);
                }
            }).CallDeferred();
            BingoNet.HostSetupChanged += refresh;
            panel._icon.Root.TreeExiting += () => BingoNet.HostSetupChanged -= refresh;
            // The host may have sent its settings before this game was listening: ask again.
            BingoNet.RequestSettings();
        }
    }

    private void Place()
    {
        if (GodotObject.IsInstanceValid(_icon.Root))
        {
            _icon.Place();
        }
    }

    /// <summary>Puts a setup into the controls without treating it as a change.</summary>
    private void Show(BingoSetup setup)
    {
        _showing = true;
        _shown = setup;
        if (_seed.Text != (setup.SeedEntry ?? ""))
        {
            _seed.Text = setup.SeedEntry ?? "";
        }
        _showing = false;
        UpdateLabels();
    }

    private void Pick(bool? enabled = null, BingoMode? mode = null)
    {
        if (_role == NetRole.Client)
        {
            return;
        }
        _shown = _shown with { Enabled = enabled ?? _shown.Enabled, Mode = mode ?? _shown.Mode };
        Changed();
    }

    private void Changed()
    {
        if (_showing || _role == NetRole.Client)
        {
            return;
        }
        _settings.SetSetup(_shown.Enabled, _seed.Text, _shown.Mode);
        _shown = _settings.Setup;
        UpdateLabels();
        BingoNet.SendSettings(_shown);
    }

    private void UpdateLabels()
    {
        var setup = _shown;
        StyleOnOff(_on, setup.Enabled, BingoStyle.On, BingoStyle.On.Darkened(0.35f));
        StyleOnOff(_off, !setup.Enabled, BingoStyle.Off, BingoStyle.Off.Darkened(0.35f));

        var mode = Array.Find(Modes, m => m.Mode == setup.Mode);
        _modeName.Text = _role == NetRole.Solo ? "How it works" : mode.Name;
        _modeText.Text = _role == NetRole.Solo ? SoloText : mode.Text;
        for (var i = 0; i < _modeNotes.Count; i++)
        {
            var picked = Modes[i].Mode == setup.Mode;
            var (note, mark) = _modeNotes[i];
            note.SetBorder(picked ? BingoStyle.Brass : null);
            note.SetLifted(picked);
            note.Slot.Modulate = picked ? Colors.White : new Color(1, 1, 1, 0.8f);
            mark.Visible = picked;
        }

        var entry = SeedEntryParser.Parse(setup.SeedEntry);
        (_seedHint.Text, var color) = entry.Kind switch
        {
            SeedEntryKind.Empty => ("Leave this empty to get a board from the run's seed. To play a specific board instead, paste its board seed here. Every board shows its own at the top, as \"Board: ...\".", BingoStyle.PageInkSoft),
            SeedEntryKind.Seed => ($"Using \"{entry.Seed}\" as the board seed instead of the run's seed. Anyone who types the same thing and plays the same characters gets the same board.", BingoStyle.On.Darkened(0.2f)),
            SeedEntryKind.Code => ($"Using this board ({entry.Code!.Mode}) instead of the run's seed. Play the same characters to get exactly the same goals.", BingoStyle.On.Darkened(0.2f)),
            _ => (entry.Error + ". Bingo will be off unless this is fixed.", BingoStyle.RedInk),
        };
        _seedHint.AddThemeColorOverride("font_color", color);

        _icon.SetBadge(setup.Enabled ? "ON" : "OFF", setup.Enabled ? new Color(0.55f, 0.95f, 0.65f) : new Color(1f, 0.6f, 0.6f));
    }

    // ---- Pieces --------------------------------------------------------------------------

    private PaperNote ModeNote(int index, float tilt, bool editable, out PanelContainer picked)
    {
        var (mode, name, shortText, _) = Modes[index];
        var note = new PaperNote(new Vector2(220, 96), tilt);
        note.SetMargins(left: 12, top: 16, right: 12, bottom: 12);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 2);
        note.Content.AddChild(column);
        var title = BingoStyle.Ink(name, BingoFonts.Heading, 21, BingoStyle.PageInk);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        var sub = BingoStyle.Ink(shortText, BingoFonts.Note, 15, BingoStyle.PageInkSoft);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(sub);

        picked = PaperNote.Stamp("PICKED", BingoStyle.RedInk, 11, -10);
        PaperNote.PinTo(picked, Control.LayoutPreset.TopRight, 8, 6);
        note.Paper.AddChild(picked);

        if (editable)
        {
            note.Slot.GuiInput += evt =>
            {
                if (evt is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    Pick(mode: mode);
                    note.Slot.AcceptEvent();
                }
            };
        }
        return note;
    }

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

    private static Button OnOffButton(string text, bool editable)
    {
        var button = BingoFonts.Keep(new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(96, 46),
        });
        button.AddThemeFontOverride("font", BingoFonts.Heading);
        button.AddThemeFontSizeOverride("font_size", 19);
        return button;
    }

    /// <summary>Picked: filled with its color. Not picked: greyed outline.</summary>
    private static void StyleOnOff(Button button, bool picked, Color color, Color shade)
    {
        StyleBoxFlat Box(Color fill, Color border, bool hover)
        {
            var box = BingoStyle.Box(hover ? fill.Lightened(0.08f) : fill, 9, 3, border, 0);
            box.AntiAliasing = true;
            if (picked)
            {
                box.ShadowColor = shade;
                box.ShadowSize = 1;
                box.ShadowOffset = new Vector2(0, 3);
            }
            return box;
        }
        var fill = picked ? color : new Color(0, 0, 0, 0.03f);
        var border = picked ? color : BingoStyle.Unpicked;
        button.AddThemeStyleboxOverride("normal", Box(fill, border, false));
        button.AddThemeStyleboxOverride("hover", Box(fill, border, true));
        button.AddThemeStyleboxOverride("pressed", Box(fill, border, false));
        button.AddThemeStyleboxOverride("disabled", Box(fill, border, false));
        var text = picked ? Colors.White : BingoStyle.Unpicked.Darkened(0.1f);
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color" })
        {
            button.AddThemeColorOverride(state, text);
        }
    }

    private static LineEdit SeedBox(bool editable)
    {
        var box = BingoFonts.Keep(new LineEdit
        {
            PlaceholderText = "Empty: board from the run's seed",
            MaxLength = 48,
            Editable = editable,
            CustomMinimumSize = new Vector2(ColumnWidth, 40),
            CaretBlink = true,
            MouseDefaultCursorShape = Control.CursorShape.Arrow, // the game only draws its own cursor for the arrow
        });
        var line = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.02f), BorderColor = BingoStyle.Unpicked, BorderWidthBottom = 2 };
        line.ContentMarginLeft = line.ContentMarginRight = 4;
        line.ContentMarginTop = line.ContentMarginBottom = 4;
        var focused = (StyleBoxFlat)line.Duplicate();
        focused.BorderColor = BingoStyle.PageInkSoft;
        box.AddThemeStyleboxOverride("normal", line);
        box.AddThemeStyleboxOverride("read_only", line);
        box.AddThemeStyleboxOverride("focus", focused);
        box.AddThemeFontOverride("font", BingoFonts.NoteBold);
        box.AddThemeFontSizeOverride("font_size", 19);
        box.AddThemeColorOverride("font_color", BingoStyle.PageInk);
        box.AddThemeColorOverride("font_uneditable_color", BingoStyle.PageInk);
        box.AddThemeColorOverride("font_placeholder_color", Color.FromHtml("#a99e8a"));
        box.AddThemeColorOverride("caret_color", BingoStyle.PageInk);
        box.AddThemeColorOverride("selection_color", BingoStyle.Brass with { A = 0.4f });
        return box;
    }

    /// <summary>Custom run: centered just above the run's seed box (above its label, if the label is in the way).</summary>
    private static Vector2? AboveSeedBox(Control screen)
    {
        if (screen.GetNodeOrNull<Control>("%SeedInput") is not { } seed || !seed.IsVisibleInTree())
        {
            return null;
        }
        var box = seed.GetGlobalRect();
        var spot = new Vector2(box.GetCenter().X - BingoIcon.IconSize / 2f, box.Position.Y - BingoIcon.IconSize - 12);
        if (screen.GetNodeOrNull<Control>("%SeedLabel") is { } label && label.IsVisibleInTree()
            && label.GetGlobalRect().Intersects(new Rect2(spot, new Vector2(BingoIcon.IconSize, BingoIcon.IconSize))))
        {
            spot.Y = label.GetGlobalRect().Position.Y - BingoIcon.IconSize - 8;
        }
        return spot;
    }

    /// <summary>
    /// Under the game's version text in the top right (and its "modded" line), right-aligned with
    /// it; the top right corner if that text can't be found.
    /// </summary>
    private static Vector2? UnderVersionText(Control icon)
    {
        var fallback = new Vector2(icon.GetViewportRect().Size.X - BingoIcon.IconSize - 24, 96);
        if (icon.GetTree()?.Root is not { } root)
        {
            return fallback;
        }
        Rect2? area = null;
        foreach (var manager in Descendants(root).OfType<NDebugInfoLabelManager>())
        {
            foreach (var label in Descendants(manager).OfType<Control>())
            {
                var name = label.Name.ToString();
                if ((name == "ReleaseInfo" || name == "ModdedWarning") && label.IsVisibleInTree())
                {
                    var rect = label.GetGlobalRect();
                    area = area?.Merge(rect) ?? rect;
                }
            }
        }
        return area is { } r ? new Vector2(r.End.X - BingoIcon.IconSize, r.End.Y + 10) : fallback;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }
}
