using System.Globalization;
using System.Text;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Hashing;
using BingoTheSpire.Core.Randomness;

namespace BingoTheSpire.Core.Boards;

public sealed class BoardBuildException(string message) : Exception(message);

/// <summary>
/// Builds a board from a seed, like Bingosync's balanced generator (spec section 6.2):
/// <list type="number">
/// <item>A difficulty layout gives every line one cell of each difficulty band, so lines are equally hard.</item>
/// <item>Cells are filled hardest first. Each gets a random goal from the closest difficulty,
/// preferring goals that share no tag with goals already in its lines.</item>
/// <item>No goal appears twice (not even with other parameters), and exclusions apply board-wide.</item>
/// </list>
/// The same request, goal list and capabilities always give the same board.
/// </summary>
public static class BoardBuilder
{
    public static BingoBoard Build(GoalList goals, BoardRequest request, DetectionCapabilities? capabilities = null)
    {
        var seedText = BingoSeed.Canonicalize(request.SeedText);
        if (!BingoSeed.IsValid(seedText))
        {
            throw new BoardBuildException($"'{request.SeedText}' is not a valid seed");
        }
        var modeClass = BoardRules.GameModeClass(request.GameMode);
        var size = request.Size ?? BoardRules.DefaultSize(modeClass);
        if (!BoardRules.IsSupportedSize(size))
        {
            throw new BoardBuildException($"board size {size} is not supported");
        }

        var boardSeed = BoardSeed(seedText, goals.Version, request.Mode, modeClass, size, request.Difficulty, request.CharacterIds);
        var rng = new SeededRandom(boardSeed);

        var pool = EligibleGoals(goals, request, modeClass, capabilities ?? DetectionCapabilities.All);
        if (pool.Count < size * size)
        {
            throw new BoardBuildException($"only {pool.Count} goals are available for this run; a {size}×{size} board needs {size * size}");
        }

        var bands = BoardRules.DifficultyBands(size, request.Difficulty);
        var layout = DifficultyLayout.Create(size, rng);
        var targets = layout.Select(band => bands[band]).ToArray();
        var lines = BoardRules.Lines(size);
        var linesOfCell = Enumerable.Range(0, size * size)
            .Select(cell => lines.Where(line => line.Contains(cell)).ToArray())
            .ToArray();

        // Hardest cells first, since fewer goals fit them. Ties in a random order.
        var order = Enumerable.Range(0, size * size).ToList();
        rng.Shuffle(order);
        order = order.OrderByDescending(cell => targets[cell]).ToList();

        var placed = new (GoalDefinition Goal, GoalTier Tier)?[size * size];
        var used = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in order)
        {
            var lineTags = linesOfCell[cell]
                .SelectMany(line => line)
                .Where(other => other != cell && placed[other] != null)
                .SelectMany(other => placed[other]!.Value.Goal.Tags ?? [])
                .ToHashSet(StringComparer.Ordinal);

            var best = new List<(GoalDefinition Goal, GoalTier Tier)>();
            var bestScore = (Distance: int.MaxValue, Conflicts: int.MaxValue);
            foreach (var goal in pool)
            {
                if (used.Contains(goal.Id) || excluded.Contains(goal.Id)
                    || (goal.Exclusions ?? []).Any(used.Contains))
                {
                    continue;
                }
                var conflicts = (goal.Tags ?? []).Count(lineTags.Contains);
                foreach (var tier in goal.Tiers)
                {
                    var score = (Distance: Math.Abs(tier.Difficulty - targets[cell]), Conflicts: conflicts);
                    if (score.CompareTo(bestScore) < 0)
                    {
                        bestScore = score;
                        best.Clear();
                    }
                    if (score == bestScore)
                    {
                        best.Add((goal, tier));
                    }
                }
            }
            if (best.Count == 0)
            {
                throw new BoardBuildException("ran out of goals that fit the board's exclusions");
            }

            var pick = rng.Pick(best);
            placed[cell] = pick;
            used.Add(pick.Goal.Id);
            foreach (var other in pick.Goal.Exclusions ?? [])
            {
                excluded.Add(other);
            }
        }

        var squares = placed.Select((p, index) =>
        {
            var (goal, tier) = p!.Value;
            var parameters = tier.Params ?? new Dictionary<string, int>();
            return new BoardSquare(index, goal.Id, tier.Difficulty, parameters, GoalText.Render(goal.Text, parameters));
        }).ToList();

        return new BingoBoard(size, seedText, goals.Version, request.Mode, request.Difficulty, boardSeed, squares);
    }

    /// <summary>The board's random seed. Exposed for tests and for showing in debug output.</summary>
    public static ulong BoardSeed(
        string seedText, int goalListVersion, BingoMode mode, string gameModeClass, int size,
        BoardDifficulty difficulty, IReadOnlyList<string> characterIds)
    {
        var characters = characterIds
            .Select(id => id.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);
        // Enum values are written as fixed strings, so renaming an enum member can't change boards.
        var text = new StringBuilder("bingothespire")
            .Append('|').Append(seedText)
            .Append('|').Append(goalListVersion.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(ModeKey(mode))
            .Append('|').Append(gameModeClass)
            .Append('|').Append(size.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(DifficultyKey(difficulty))
            .Append('|').AppendJoin(',', characters)
            .ToString();
        return StableHash.Fnv1a64(text);
    }

    /// <summary>
    /// Goals this run can use, sorted by id so the result never depends on file order.
    /// In Versus and Lockout a character goal needs every player on that character; in Team, any one.
    /// </summary>
    public static List<GoalDefinition> EligibleGoals(
        GoalList goals, BoardRequest request, string gameModeClass, DetectionCapabilities capabilities) =>
        goals.Goals
            .Where(g => !(g.ExcludeGameModes ?? []).Contains(gameModeClass, StringComparer.OrdinalIgnoreCase))
            .Where(g => capabilities.Detectors == null || capabilities.Detectors.Contains(g.Detector))
            .Where(g => capabilities.Events == null || (g.Requires?.Events ?? []).All(capabilities.Events.Contains))
            .Where(g => CharacterAllows(g.Requires?.Character, request))
            .OrderBy(g => g.Id, StringComparer.Ordinal)
            .ToList();

    private static bool CharacterAllows(string? character, BoardRequest request)
    {
        if (character == null)
        {
            return true;
        }
        bool Plays(string id) => string.Equals(id, character, StringComparison.OrdinalIgnoreCase);
        return request.Mode == BingoMode.Team
            ? request.CharacterIds.Any(Plays)
            : request.CharacterIds.Count > 0 && request.CharacterIds.All(Plays);
    }

    private static string ModeKey(BingoMode mode) => mode switch
    {
        BingoMode.Versus => "versus",
        BingoMode.Lockout => "lockout",
        BingoMode.Team => "team",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static string DifficultyKey(BoardDifficulty difficulty) => difficulty switch
    {
        BoardDifficulty.Easy => "easy",
        BoardDifficulty.Medium => "medium",
        BoardDifficulty.Hard => "hard",
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };
}
