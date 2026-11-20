using BingoTheSpire.Core.Goals;

namespace BingoTheSpire.Core.Tests.Boards;

/// <summary>Synthetic goal lists for builder tests, independent of the real goal list.</summary>
internal static class TestGoals
{
    public static readonly string[] TagNames = ["block", "cards", "potions", "gold", "powers", "elites", "hp", "orbs"];

    /// <summary>
    /// <paramref name="perDifficulty"/> single-tier goals for each difficulty 1-5, with rotating tags
    /// (<paramref name="tagCount"/> distinct ones, named from <see cref="TagNames"/> then numbered).
    /// </summary>
    public static GoalList Pool(int perDifficulty = 12, int version = 1, int tagCount = 8)
    {
        var goals = new List<GoalDefinition>();
        for (var d = 1; d <= 5; d++)
        {
            for (var i = 0; i < perDifficulty; i++)
            {
                var t = (d * 7 + i) % tagCount;
                var tag = t < TagNames.Length ? TagNames[t] : $"tag{t}";
                goals.Add(new GoalDefinition($"g{d}_{i:00}", $"Goal {d}.{i}", "Test", [new GoalTier(d)], Tags: [tag]));
            }
        }
        return new GoalList(version, goals);
    }

    public static GoalList With(GoalList list, params GoalDefinition[] extra) =>
        list with { Goals = list.Goals.Concat(extra).ToList() };
}
