using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.Net;

namespace BingoTheSpire.Mod.Ui;

/// <summary>
/// The Bingo button and panel on the screens where a run is set up (spec 4.1): bingo on or off,
/// the bingo-seed box, and in co-op the mode. Only the host can change them; everyone else sees
/// the host's choice. Built from Godot's built-in controls, added under the game's screen.
/// </summary>
internal sealed class LobbyPanel
{
    private const string ButtonName = "BingoTheSpireLobbyButton";

    // Lobbies whose player-joined event we already listen to (one per lobby, however often the screen opens).
    private static readonly ConditionalWeakTable<StartRunLobby, object> HookedLobbies = new();

    private static readonly (BingoMode Mode, string Name, string Text)[] Modes =
    [
        (BingoMode.Versus, "Versus", "Everyone fills their own copy of the board. First line wins."),
        (BingoMode.Lockout, "Lockout", "One shared board: whoever completes a goal first takes the square. First line, or a clinch, wins."),
        (BingoMode.Team, "Team", "One shared board, filled together. Any player completing a goal fills it for everyone."),
    ];

    private readonly BingoSettings _settings;
    private readonly NetRole _role;
    private readonly Button _button;
    private readonly CenterContainer _overlay;
    private readonly CheckButton _enabled;
    private readonly LineEdit _seed;
    private readonly Label _seedHint;
    private readonly Control _modeSection;
    private readonly List<Button> _modeButtons = [];
    private readonly Label _modeText;
    private readonly Label _hostNote;

    private LobbyPanel(Control screen, BingoSettings settings, NetRole role)
    {
        _settings = settings;
        _role = role;

        _button = new Button { Name = ButtonName, FocusMode = Control.FocusModeEnum.None, TooltipText = "Bingo settings for this run" };
        _button.AddThemeFontSizeOverride("font_size", 22);
        _button.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _button.OffsetLeft = -300;
        _button.OffsetRight = -24;
        _button.OffsetTop = 24;
        _button.OffsetBottom = 72;
        _button.Pressed += () => _overlay!.Visible = !_overlay.Visible;
        screen.AddChild(_button);

        _overlay = new CenterContainer { Name = "BingoTheSpireLobbyPanel", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(_overlay);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(640, 0) };
        panel.AddThemeStyleboxOverride("panel",
            BingoStyle.Box(BingoStyle.PanelBg, 16, 3, BingoStyle.PanelBorder, padding: 22, glow: new Color(0, 0, 0, 0.6f), glowSize: 24));
        _overlay.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        var title = BingoStyle.Text("BINGO", 36, BingoStyle.Gold, outline: 10);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = new Button { Text = "✕", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(44, 44) };
        close.AddThemeFontSizeOverride("font_size", 22);
        close.Pressed += () => _overlay.Visible = false;
        header.AddChild(close);

        _hostNote = BingoStyle.Text("The host chooses the bingo settings for everyone.", 16, BingoStyle.Muted, outline: 3);
        _hostNote.Visible = role == NetRole.Client;
        column.AddChild(_hostNote);

        _enabled = new CheckButton { Text = "Play this run with a bingo board", FocusMode = Control.FocusModeEnum.None };
        _enabled.AddThemeFontSizeOverride("font_size", 20);
        _enabled.Toggled += _ => Changed();
        column.AddChild(_enabled);

        column.AddChild(BingoStyle.Text("Bingo seed (optional)", 18, Colors.White, outline: 4));
        _seed = new LineEdit
        {
            PlaceholderText = "Empty: the board comes from the run seed. Or type a seed, or paste a board code.",
            MaxLength = 48,
            CustomMinimumSize = new Vector2(0, 44),
        };
        _seed.AddThemeFontSizeOverride("font_size", 18);
        _seed.TextChanged += _ => Changed();
        column.AddChild(_seed);
        _seedHint = BingoStyle.Text("", 15, BingoStyle.Muted, outline: 3);
        _seedHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_seedHint);

        var modes = new VBoxContainer { Visible = role != NetRole.Solo };
        modes.AddThemeConstantOverride("separation", 8);
        _modeSection = modes;
        column.AddChild(modes);
        modes.AddChild(BingoStyle.Text("Co-op mode", 18, Colors.White, outline: 4));
        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 10);
        modes.AddChild(modeRow);
        var group = new ButtonGroup();
        foreach (var (mode, name, _) in Modes)
        {
            var button = new Button
            {
                Text = name, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44),
            };
            button.AddThemeFontSizeOverride("font_size", 18);
            button.Toggled += on =>
            {
                if (on)
                {
                    Changed();
                }
            };
            modeRow.AddChild(button);
            _modeButtons.Add(button);
        }
        _modeText = BingoStyle.Text("", 15, BingoStyle.Muted, outline: 3);
        _modeText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        modes.AddChild(_modeText);

        Show(role == NetRole.Client ? BingoNet.HostSetup ?? BingoSetup.Default : settings.Setup);
        SetEditable(role != NetRole.Client);
    }

    /// <summary>Adds the Bingo button to a run-setup screen that has just opened.</summary>
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
                if (GodotObject.IsInstanceValid(panel._button))
                {
                    panel.Show(BingoNet.HostSetup ?? BingoSetup.Default);
                }
            }).CallDeferred();
            BingoNet.HostSetupChanged += refresh;
            panel._button.TreeExiting += () => BingoNet.HostSetupChanged -= refresh;
            // The host may have sent its settings before this game was listening: ask again.
            BingoNet.RequestSettings();
        }
    }

    private bool _showing;

    /// <summary>Puts a setup into the controls without treating it as a change.</summary>
    private void Show(BingoSetup setup)
    {
        _showing = true;
        _enabled.ButtonPressed = setup.Enabled;
        if (_seed.Text != (setup.SeedEntry ?? ""))
        {
            _seed.Text = setup.SeedEntry ?? "";
        }
        var index = Array.FindIndex(Modes, m => m.Mode == setup.Mode);
        _modeButtons[Math.Max(0, index)].ButtonPressed = true;
        _showing = false;
        UpdateLabels(setup);
    }

    private void SetEditable(bool editable)
    {
        _enabled.Disabled = !editable;
        _seed.Editable = editable;
        foreach (var button in _modeButtons)
        {
            button.Disabled = !editable;
        }
    }

    private void Changed()
    {
        if (_showing || _role == NetRole.Client)
        {
            return;
        }
        var mode = Modes[Math.Max(0, _modeButtons.FindIndex(b => b.ButtonPressed))].Mode;
        _settings.SetSetup(_enabled.ButtonPressed, _seed.Text, mode);
        var setup = _settings.Setup;
        UpdateLabels(setup);
        BingoNet.SendSettings(setup);
    }

    private void UpdateLabels(BingoSetup setup)
    {
        var mode = Array.Find(Modes, m => m.Mode == setup.Mode);
        _modeText.Text = mode.Text;
        _modeSection.Modulate = setup.Enabled ? Colors.White : new Color(1, 1, 1, 0.45f);
        _seed.Modulate = _modeSection.Modulate;

        var entry = SeedEntryParser.Parse(setup.SeedEntry);
        (_seedHint.Text, var color) = entry.Kind switch
        {
            SeedEntryKind.Empty => ("The board comes from the run's seed.", BingoStyle.Muted),
            SeedEntryKind.Seed => ($"Bingo seed {entry.Seed}: the same seed and characters always give the same board.", BingoStyle.FilledBorder),
            SeedEntryKind.Code => ($"Board code: rebuilds that exact board ({entry.Code!.Mode}, {entry.Code.Size}×{entry.Code.Size}, {entry.Code.Difficulty}) if the characters match.", BingoStyle.FilledBorder),
            _ => (entry.Error + ". Bingo will be off unless this is fixed.", new Color(1f, 0.45f, 0.45f)),
        };
        _seedHint.AddThemeColorOverride("font_color", color);

        var modeName = _role == NetRole.Solo ? "" : $" · {mode.Name}";
        _button.Text = setup.Enabled ? $"BINGO: ON{modeName}" : "BINGO: OFF";
        _button.AddThemeColorOverride("font_color", setup.Enabled ? BingoStyle.Gold : BingoStyle.Muted);
    }
}
