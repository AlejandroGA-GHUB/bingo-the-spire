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
        (BingoMode.Lockout, "Lockout", "One board, race for it",
            "One shared board. Complete a goal first and the square is yours. The first line wins. If the run ends with no line, whoever has the most squares wins. If someone gets so far ahead that nobody else can catch up on squares or still make a line, they win right away."),
        (BingoMode.Team, "Team", "One board, together",
            "One shared board, filled together. Counts add up across the team, like potions used or cards drawn in a turn, so their numbers are bigger. One-moment goals, like a single big hit, still need one player. There's no winner: see how many squares and lines you can make together."),
    ];

    private static readonly (BoardDifficulty Difficulty, string Name, string Short, int Band, string Text)[] Difficulties =
    [
        (BoardDifficulty.Easy, "Easy", "no 5s", 1, "Easy: every square is difficulty 1 to 4, and every line is equally hard."),
        (BoardDifficulty.Medium, "Medium", "one 5", 4, "Medium: one difficulty 5 square, in the middle of the board."),
        (BoardDifficulty.Hard, "Hard", "three 5s", 5, "Hard: three difficulty 5 squares (two on Blitz boards): the middle one and more, never two in one line."),
    ];

    private const string SoloText =
        "Goals fill in by themselves as you play. Finish a row, a column or a diagonal for BINGO. Hover a note on the board to see what counted.";

    private readonly BingoSettings _settings;
    private readonly NetRole _role;
    private readonly BingoIcon _icon;
    private readonly BookFrame _book;
    private const float SwitchWidth = 88;
    private const float SwitchHeight = 40;
    private readonly Button _on;
    private readonly Button _off;
    private readonly Panel _pill;
    private readonly StyleBoxFlat _pillStyle;
    private bool? _pillOn;
    private Tween? _pillTween;
    private readonly LineEdit _seed;
    private readonly Label _seedHint;
    private readonly Label _modeName;
    private readonly Label _modeText;
    private readonly List<(PaperNote Note, PanelContainer Picked)> _modeNotes = [];
    private readonly List<PaperNote> _difficultyNotes = [];
    private readonly Label _difficultyHint;
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
        // Controller: R2 opens the panel. The game only uses it in fights, and on the Daily screen's
        // leaderboard, where it's left alone.
        var padHint = BingoStyle.Text("R2", 15, Colors.White, outline: 6, font: BingoFonts.Heading);
        padHint.MouseFilter = Control.MouseFilterEnum.Ignore;
        padHint.HorizontalAlignment = HorizontalAlignment.Center;
        padHint.Position = new Vector2(0, BingoIcon.IconSize - 2);
        padHint.Size = new Vector2(BingoIcon.IconSize, 20);
        padHint.Visible = false;
        _icon.Root.AddChild(padHint);
        Gamepad.OnRightTrigger(_icon.Root,
            () => _icon.Root.IsVisibleInTree() && !BookFrame.AnyBackdropOpen && !HasLeaderboard(screen),
            () =>
            {
                Sfx.Play(Sfx.Click);
                _book.Show();
            });
        // The hint shows while a controller is in use. The leaderboard check walks the screen, so it runs once a second.
        if (Engine.GetMainLoop() is SceneTree hintTree)
        {
            var frames = 0;
            var leaderboard = false;
            void Tick()
            {
                if (!GodotObject.IsInstanceValid(padHint))
                {
                    hintTree.ProcessFrame -= Tick;
                    return;
                }
                if (frames++ % 60 == 0)
                {
                    leaderboard = GodotObject.IsInstanceValid(screen) && HasLeaderboard(screen);
                }
                padHint.Visible = Gamepad.ControllerActive && !leaderboard;
            }
            hintTree.ProcessFrame += Tick;
        }

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
        // ON / OFF: one frame around both words, with a colored pill that slides behind the picked one.
        var frameStyle = BingoStyle.Box(new Color(0, 0, 0, 0.03f), 10, 3, BingoStyle.Unpicked, 0);
        frameStyle.ContentMarginLeft = frameStyle.ContentMarginRight = frameStyle.ContentMarginTop = frameStyle.ContentMarginBottom = 3;
        frameStyle.AntiAliasing = true;
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        frame.AddThemeStyleboxOverride("panel", frameStyle);
        left.AddChild(frame);
        var track = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(SwitchWidth * 2, SwitchHeight), ClipContents = true };
        frame.AddChild(track);
        _pillStyle = BingoStyle.Box(BingoStyle.On, 7, 0, Colors.Transparent, 0);
        _pillStyle.AntiAliasing = true;
        _pillStyle.BorderWidthBottom = 3;
        _pillStyle.BorderColor = BingoStyle.On.Darkened(0.35f);
        _pill = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(SwitchWidth, SwitchHeight) };
        _pill.AddThemeStyleboxOverride("panel", _pillStyle);
        track.AddChild(_pill);
        _on = SwitchButton("ON", 0, editable);
        _off = SwitchButton("OFF", SwitchWidth, editable);
        _on.Pressed += () => Flip(true);
        _off.Pressed += () => Flip(false);
        track.AddChild(_on);
        track.AddChild(_off);
        left.AddChild(Spacer(8));
        left.AddChild(Heading("Board difficulty"));
        var difficulties = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        difficulties.AddThemeConstantOverride("separation", 14);
        left.AddChild(Spacer(4));
        left.AddChild(difficulties);
        float[] difficultyTilts = [-1.6f, 0.8f, 1.4f];
        for (var i = 0; i < Difficulties.Length; i++)
        {
            var note = DifficultyNote(i, difficultyTilts[i], editable);
            difficulties.AddChild(note.Slot);
            _difficultyNotes.Add(note);
        }
        _difficultyHint = Body("", 15, BingoStyle.PageInkSoft);
        left.AddChild(_difficultyHint);
        left.AddChild(Spacer(8));
        left.AddChild(Heading("Board seed (optional)"));
        _seed = SeedBox(editable);
        _seed.TextChanged += _ => Changed();
        left.AddChild(_seed);
        _seedHint = Body("", 15, BingoStyle.PageInkSoft);
        left.AddChild(_seedHint);
        left.AddChild(Spacer(8));
        var links = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        links.AddThemeConstantOverride("separation", 10);
        left.AddChild(links);
        links.AddChild(HistoryBook.Button("Past runs", () => HistoryBook.ShowHistory(screen)));
        links.AddChild(HistoryBook.Button("Settings", () => SettingsBook.Show(screen, settings)));

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
    /// <summary>The Daily screen pages its leaderboard with the triggers; R2 stays the game's there.</summary>
    private static bool HasLeaderboard(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if ((child.GetType().Name == "NLeaderboardPageArrow" && child is CanvasItem { Visible: true }) || HasLeaderboard(child))
            {
                return true;
            }
        }
        return false;
    }

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

    private void Pick(bool? enabled = null, BingoMode? mode = null, BoardDifficulty? difficulty = null)
    {
        if (_role == NetRole.Client)
        {
            return;
        }
        _shown = _shown with { Enabled = enabled ?? _shown.Enabled, Mode = mode ?? _shown.Mode, Difficulty = difficulty ?? _shown.Difficulty };
        Changed();
    }

    private void Changed()
    {
        if (_showing || _role == NetRole.Client)
        {
            return;
        }
        _settings.SetSetup(_shown.Enabled, _seed.Text, _shown.Mode, _shown.Difficulty);
        _shown = _settings.Setup;
        UpdateLabels();
        BingoNet.SendSettings(_shown);
    }

    private void UpdateLabels()
    {
        var setup = _shown;
        SlidePill(setup.Enabled);

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
        for (var i = 0; i < _difficultyNotes.Count; i++)
        {
            var picked = Difficulties[i].Difficulty == setup.Difficulty;
            var note = _difficultyNotes[i];
            note.SetBorder(picked ? BingoStyle.Brass : null);
            note.SetLifted(picked);
            note.Slot.Modulate = picked ? Colors.White : new Color(1, 1, 1, 0.8f);
        }
        _difficultyHint.Text = entry.Kind == SeedEntryKind.Code
            ? $"The pasted board brings its own difficulty ({entry.Code!.Difficulty})."
            : Array.Find(Difficulties, d => d.Difficulty == setup.Difficulty).Text;
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
        // Inside the note's margins, with a little room for its tilt.
        BingoStyle.FitWidth(sub, BingoFonts.Note, 15, 220 - 12 - 12 - 8);
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
            Gamepad.Focusable(note.Slot, () => Pick(mode: mode));
        }
        return note;
    }

    private PaperNote DifficultyNote(int index, float tilt, bool editable)
    {
        var (difficulty, name, shortText, band, _) = Difficulties[index];
        var note = new PaperNote(new Vector2(104, 60), tilt);
        note.HideTape();
        note.SetTag(BingoStyle.Difficulty(band), "", 20);
        note.SetMargins(left: 6, top: 8, right: 6, bottom: 6);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 0);
        note.Content.AddChild(column);
        var title = BingoStyle.Ink(name, BingoFonts.Heading, 17, BingoStyle.PageInk);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);
        var sub = BingoStyle.Ink(shortText, BingoFonts.Note, 13, BingoStyle.PageInkSoft);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(sub);

        if (editable)
        {
            note.Slot.GuiInput += evt =>
            {
                if (evt is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    Pick(difficulty: difficulty);
                    note.Slot.AcceptEvent();
                }
            };
            Gamepad.Focusable(note.Slot, () => Pick(difficulty: difficulty));
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

    private Button SwitchButton(string text, float x, bool editable)
    {
        var button = BingoFonts.Keep(new Button
        {
            Text = text,
            Position = new Vector2(x, 0),
            Size = new Vector2(SwitchWidth, SwitchHeight),
            Disabled = !editable,
            MouseDefaultCursorShape = Control.CursorShape.Arrow, // the game only draws its own cursor for the arrow
        });
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, BingoStyle.Empty());
        }
        button.AddThemeFontOverride("font", BingoFonts.Heading);
        button.AddThemeFontSizeOverride("font_size", 19);
        // The switch plays the game's tickbox sounds itself, only when it actually flips.
        return Gamepad.Focusable(button, click: null, ring: Gamepad.InsetRing);
    }

    private void Flip(bool on)
    {
        if (_role == NetRole.Client || _shown.Enabled == on)
        {
            return;
        }
        Sfx.Play(on ? Sfx.TickOn : Sfx.TickOff);
        Pick(enabled: on);
    }

    /// <summary>Slides the pill under ON or OFF and colors it; jumps straight there the first time.</summary>
    private void SlidePill(bool on)
    {
        var fill = on ? BingoStyle.On : BingoStyle.Off;
        var x = on ? 0 : SwitchWidth;
        foreach (var (button, picked) in new[] { (_on, on), (_off, !on) })
        {
            var text = picked ? Colors.White : BingoStyle.Unpicked.Darkened(0.1f);
            foreach (var state in new[] { "font_color", "font_pressed_color", "font_focus_color", "font_disabled_color" })
            {
                button.AddThemeColorOverride(state, text);
            }
            button.AddThemeColorOverride("font_hover_color", picked ? Colors.White : BingoStyle.PageInkSoft);
        }
        if (_pillOn == on)
        {
            return;
        }
        var first = _pillOn == null;
        _pillOn = on;
        _pillTween?.Kill();
        if (first || !_pill.IsInsideTree())
        {
            _pill.Position = new Vector2(x, 0);
            _pillStyle.BgColor = fill;
            _pillStyle.BorderColor = fill.Darkened(0.35f);
            return;
        }
        // Eases in to its stop without overshooting, so it never leaves the frame.
        var from = _pillStyle.BgColor;
        _pillTween = _pill.CreateTween();
        _pillTween.TweenProperty(_pill, "position:x", x, 0.3).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _pillTween.Parallel().TweenMethod(Callable.From<float>(t =>
        {
            _pillStyle.BgColor = from.Lerp(fill, t);
            _pillStyle.BorderColor = _pillStyle.BgColor.Darkened(0.35f);
            _pill.QueueRedraw();
        }), 0f, 1f, 0.25);
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
