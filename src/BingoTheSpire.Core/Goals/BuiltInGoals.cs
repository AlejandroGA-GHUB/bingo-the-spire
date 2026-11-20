using BingoTheSpire.Core.Serialization;

namespace BingoTheSpire.Core.Goals;

/// <summary>The goal list shipped inside the mod (goals.json, embedded under a fixed name).</summary>
public static class BuiltInGoals
{
    public const string ResourceName = "BingoTheSpire.goals.json";

    private static readonly Lazy<GoalList> Cached = new(Load);

    public static GoalList List => Cached.Value;

    private static GoalList Load()
    {
        using var stream = typeof(BuiltInGoals).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource {ResourceName} is missing");
        using var reader = new StreamReader(stream);
        var list = BingoJson.DeserializeGoalList(reader.ReadToEnd())
            ?? throw new InvalidOperationException("goals.json is empty");
        var errors = list.Validate();
        return errors.Count == 0
            ? list
            : throw new InvalidOperationException("goals.json is invalid: " + string.Join("; ", errors));
    }
}
