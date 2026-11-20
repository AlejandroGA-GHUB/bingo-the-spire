using System.Text.RegularExpressions;

namespace BingoTheSpire.Core.Goals;

/// <summary>Fills goal templates like "Gain {n}+ Block in a single turn".</summary>
public static partial class GoalText
{
    [GeneratedRegex(@"\{([a-zA-Z][a-zA-Z0-9]*)\}")]
    private static partial Regex PlaceholderRegex();

    public static IEnumerable<string> Placeholders(string template) =>
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct();

    public static string Render(string template, IReadOnlyDictionary<string, int>? parameters) =>
        PlaceholderRegex().Replace(template, m =>
            parameters != null && parameters.TryGetValue(m.Groups[1].Value, out var value)
                ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : m.Value);
}
