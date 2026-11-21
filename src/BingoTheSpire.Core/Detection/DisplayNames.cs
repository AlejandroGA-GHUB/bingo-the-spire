using System.Globalization;

namespace BingoTheSpire.Core.Detection;

/// <summary>Names for evidence text: the game's own name when the adapter sent one, otherwise a readable form of the id.</summary>
public static class DisplayNames
{
    /// <summary>"FUZZY_WURM_CRAWLER" → "Fuzzy Wurm Crawler", "WEAK_POWER" → "Weak".</summary>
    public static string Pretty(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }
        var text = id.EndsWith("_POWER", StringComparison.Ordinal) ? id[..^"_POWER".Length] : id;
        var words = text.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }

    public static string Of(string? name, string? id, string fallback) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim() : Pretty(id) is { Length: > 0 } pretty ? pretty : fallback;

    internal static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
