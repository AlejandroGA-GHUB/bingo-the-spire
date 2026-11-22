using System.Text.RegularExpressions;

namespace BingoTheSpire.Core.Goals;

/// <summary>
/// Fills goal templates like "Have {n}+ Block at once". <c>{s}</c> is a plural ending that
/// follows <c>{n}</c>: "Remove {n} card{s}" reads "Remove 1 card" and "Remove 2 cards".
/// </summary>
public static partial class GoalText
{
    private const string Plural = "s";

    [GeneratedRegex(@"\{([a-zA-Z][a-zA-Z0-9]*)\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>The parameters a template needs ({s} needs {n}).</summary>
    public static IEnumerable<string> Placeholders(string template) =>
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value == Plural ? "n" : m.Groups[1].Value).Distinct();

    public static string Render(string template, IReadOnlyDictionary<string, int>? parameters) =>
        PlaceholderRegex().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            if (name == Plural)
            {
                return parameters != null && parameters.TryGetValue("n", out var n) ? (n == 1 ? "" : "s") : m.Value;
            }
            return parameters != null && parameters.TryGetValue(name, out var value)
                ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : m.Value;
        });
}
