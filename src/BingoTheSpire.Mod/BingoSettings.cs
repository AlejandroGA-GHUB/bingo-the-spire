using System.Text.Json;
using System.Text.Json.Nodes;

namespace BingoTheSpire.Mod;

/// <summary>
/// Player settings, kept in <c>settings.json</c> in the mod's data folder. Read with JsonNode so
/// no reflection-based serialization runs inside the game. Until the in-game settings screen
/// exists, players edit this file.
/// </summary>
internal sealed class BingoSettings
{
    public const string DefaultHotkey = "B";

    /// <summary>Godot key name for the board hotkey (e.g. "B", "F6"), or null when the hotkey is off.</summary>
    public string? Hotkey { get; private set; } = DefaultHotkey;

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
            var root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
            if (root != null && root.TryGetPropertyValue("hotkey", out var hotkey))
            {
                var text = hotkey?.GetValueKind() == JsonValueKind.String ? hotkey.GetValue<string>().Trim() : null;
                settings.Hotkey = string.IsNullOrEmpty(text) || text.Equals("off", StringComparison.OrdinalIgnoreCase) ? null : text;
            }
        }
        catch (Exception ex)
        {
            BingoLog.Warn($"couldn't read {FilePath}, using defaults: {ex.Message}");
        }
        return settings;
    }

    private void Save()
    {
        Directory.CreateDirectory(DataPaths.Root);
        var root = new JsonObject
        {
            ["hotkey"] = Hotkey,
            ["_help"] = "hotkey: a key name like \"B\" or \"F6\", or \"off\". The Bingo button works either way.",
        };
        File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
