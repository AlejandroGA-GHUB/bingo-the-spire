namespace BingoTheSpire.Core.Goals;

/// <summary>
/// One goal in the goal list: a text template, the detector that checks it, and the parameter
/// values (tiers) the board builder can pick from. See spec section 6.1.
/// </summary>
/// <param name="Id">Stable id. Never reuse an id for a different goal: board codes depend on it.</param>
/// <param name="Text">Template shown on the square, e.g. "Have {n}+ Block at once".</param>
/// <param name="Detector">Name of the detector type that checks this goal.</param>
/// <param name="Tiers">Parameter values and how hard each is. At least one, even for goals without parameters.</param>
/// <param name="Tags">Topics. Goals sharing a tag are kept out of the same line when possible.</param>
/// <param name="Exclusions">Ids of goals too similar to this one to be on the same board.</param>
/// <param name="ExcludeGameModes">Board game-mode classes this goal is left out of (see <see cref="Boards.BoardRules"/>).</param>
public sealed record GoalDefinition(
    string Id,
    string Text,
    string Detector,
    IReadOnlyList<GoalTier> Tiers,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<string>? Exclusions = null,
    GoalRequirements? Requires = null,
    IReadOnlyList<string>? ExcludeGameModes = null);

/// <param name="Difficulty">1 (happens in most runs) to 5 (rare, needs planning and luck).</param>
/// <param name="Params">Values filled into the template and passed to the detector. Empty for goals without parameters.</param>
/// <param name="Team">
/// Hand-tuned values for a Team board of 2 players, in place of the usual +75% (spec 4.3). Each
/// further player adds the same step again (solo 4, team 6: 3 players get 8). Only pooled goals use it.
/// </param>
public sealed record GoalTier(int Difficulty, IReadOnlyDictionary<string, int>? Params = null, IReadOnlyDictionary<string, int>? Team = null);

/// <param name="Events">Event types the detector needs. The goal is left out if any is unavailable on this game version.</param>
/// <param name="Character">Game character id, for character-specific goals. Null for general goals.</param>
public sealed record GoalRequirements(IReadOnlyList<string>? Events = null, string? Character = null);

/// <param name="Version">Bumped whenever goals change in a way that changes boards. Part of the board seed and board codes.</param>
public sealed record GoalList(int Version, IReadOnlyList<GoalDefinition> Goals)
{
    public const int MinDifficulty = 1;
    public const int MaxDifficulty = 5;

    /// <summary>Problems that would make boards wrong or unstable. Empty when the list is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var goal in Goals)
        {
            if (string.IsNullOrWhiteSpace(goal.Id) || !ids.Add(goal.Id))
            {
                errors.Add($"missing or duplicate id '{goal.Id}'");
            }
            if (string.IsNullOrWhiteSpace(goal.Detector))
            {
                errors.Add($"{goal.Id}: no detector");
            }
            if (goal.Tiers.Count == 0)
            {
                errors.Add($"{goal.Id}: no tiers");
            }
            foreach (var tier in goal.Tiers)
            {
                if (tier.Difficulty is < MinDifficulty or > MaxDifficulty)
                {
                    errors.Add($"{goal.Id}: difficulty {tier.Difficulty} is outside {MinDifficulty}-{MaxDifficulty}");
                }
                foreach (var name in GoalText.Placeholders(goal.Text))
                {
                    if (tier.Params == null || !tier.Params.ContainsKey(name))
                    {
                        errors.Add($"{goal.Id}: a tier has no value for {{{name}}}");
                    }
                }
            }
        }
        foreach (var goal in Goals)
        {
            foreach (var other in goal.Exclusions ?? [])
            {
                if (!ids.Contains(other))
                {
                    errors.Add($"{goal.Id}: excludes unknown goal '{other}'");
                }
            }
        }
        return errors;
    }
}
