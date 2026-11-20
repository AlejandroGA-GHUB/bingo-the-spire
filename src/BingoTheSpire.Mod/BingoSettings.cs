using System.Text.Json;
using System.Text.Json.Nodes;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Mod;

/// <summary>
/// Player settings, kept in <c>settings.json</c> in the mod's data folder. Read with JsonNode so
/// no reflection-based serialization runs inside the game. Until the in-game settings screen
/// exists, players edit this file (the icon position is saved by dragging the icon).
/// </summary>
internal sealed class BingoSettings
{
    public const string DefaultHotkey = "B";

    /// <summary>Godot key name for the board hotkey (e.g. "B", "F6"), or null when the hotkey is off.</summary>
    public string? Hotkey { get; private set; } = DefaultHotkey;

    /// <summary>
    /// Where the player dragged the bingo icon, as a fraction of the screen size (so it survives
    /// resolution changes). Null keeps the default spot next to the run timer.
    /// </summary>
    public (float X, float Y)? IconPosition { get; private set; }

    /// <summary>Bingo on or off for new runs. Remembered between runs.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>The co-op mode last picked as host. Remembered between runs.</summary>
    public BingoMode Mode { get; private set; } = BingoMode.Versus;

    /// <summary>
    /// What's in the bingo-seed box. Not saved: it's cleared once a run starts with it, so the
    /// next run doesn't quietly reuse the same board.
    /// </summary>
    public string? SeedEntry { get; private set; }

    /// <summary>This game's choice for the next run (in co-op, only the host's counts).</summary>
    public BingoSetup Setup => new(Enabled, SeedEntry, Mode);

    public static string FilePath => Path.Combine(DataPaths.Root, "settings.json");

    public static BingoSettings Load()
    {
        var settings = new BingoSettings();
        try
        {
            if (!File.Exists(FilePath))
            {
                settings.Save();
                return settings;
            }
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject root)
            {
                return settings;
            }
            if (root.TryGetPropertyValue("hotkey", out var hotkey))
            {
                var text = hotkey?.GetValueKind() == JsonValueKind.String ? hotkey.GetValue<string>().Trim() : null;
                settings.Hotkey = string.IsNullOrEmpty(text) || text.Equals("off", StringComparison.OrdinalIgnoreCase) ? null : text;
            }
            if (root["enabled"]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                settings.Enabled = root["enabled"]!.GetValue<bool>();
            }
            if (root["mode"]?.GetValueKind() == JsonValueKind.String
                && Enum.TryParse<BingoMode>(root["mode"]!.GetValue<string>(), ignoreCase: true, out var mode))
            {
                settings.Mode = mode;
            }
            if (root["iconPosition"] is JsonArray { Count: 2 } pos
                && pos[0]?.GetValueKind() == JsonValueKind.Number && pos[1]?.GetValueKind() == JsonValueKind.Number)
            {
                settings.IconPosition = (Math.Clamp(pos[0]!.GetValue<float>(), 0f, 1f), Math.Clamp(pos[1]!.GetValue<float>(), 0f, 1f));
            }
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't read {FilePath}, using defaults: {ex.Message}");
        }
        return settings;
    }

    public void SetIconPosition((float X, float Y)? position)
    {
        IconPosition = position;
        Save();
    }

    public void SetSetup(bool enabled, string? seedEntry, BingoMode mode)
    {
        SeedEntry = string.IsNullOrWhiteSpace(seedEntry) ? null : seedEntry.Trim();
        if (Enabled != enabled || Mode != mode)
        {
            Enabled = enabled;
            Mode = mode;
            Save();
        }
    }

    /// <summary>A run started with the seed entry: clear it for the next one.</summary>
    public void ClearSeedEntry() => SeedEntry = null;

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataPaths.Root);
            var root = new JsonObject
            {
                ["enabled"] = Enabled,
                ["mode"] = Mode.ToString(),
                ["hotkey"] = Hotkey ?? "off",
                ["iconPosition"] = IconPosition is { } p
                    ? new JsonArray(Math.Round(p.X, 4), Math.Round(p.Y, 4))
                    : null,
                ["_help"] = "hotkey: a key name like \"B\" or \"F6\", or \"off\". iconPosition: set by dragging the bingo icon; right-click the icon to reset it.",
            };
            File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't save {FilePath}: {ex.Message}");
        }
    }
}
