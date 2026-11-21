using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Recording;

namespace BingoTheSpire.Core.Tests.Detection;

/// <summary>
/// Replays real runs recorded in game (tests/.../Fixtures) through every goal at every tier.
/// The fixtures are main v0.111.1, solo Ironclad. In the first, most normal fights were skipped
/// with the dev console, so they end on round 1 without damage taken.
/// </summary>
public class ReplayTests
{
    /// <summary>"goalId:n" → evidence, for every goal tier the run completed.</summary>
    private static Dictionary<string, string> Replay(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        using var reader = new StreamReader(path);
        var (_, events) = EventRecorder.Read(reader);

        var squares = BuiltInGoals.List.Goals
            .SelectMany(g => g.Tiers.Select(t => (Goal: g, Tier: t)))
            .Select((x, i) => new BoardSquare(i, x.Goal.Id, x.Tier.Difficulty, x.Tier.Params ?? new Dictionary<string, int>(),
                GoalText.Render(x.Goal.Text, x.Tier.Params)))
            .ToList();
        var board = new BingoBoard(1, "TEST", BuiltInGoals.List.Version, BingoMode.Versus, BoardDifficulty.Medium, 0, squares);
        var tracker = new GoalTracker(board, BuiltInGoals.List, (id, ex) => throw new Xunit.Sdk.XunitException($"{id} threw: {ex}"));

        var done = new Dictionary<string, string>();
        tracker.Completed += c =>
        {
            var n = squares[c.Square].Params.TryGetValue("n", out var v) ? v.ToString() : "-";
            done[$"{c.GoalId}:{n}"] = c.Evidence;
        };
        foreach (var evt in events)
        {
            tracker.Handle(evt);
        }
        return done;
    }

    /// <summary>
    /// Every tier this run completes, checked by hand against the log: e.g. 10 draws in one turn
    /// came from Gambler's Brew, 5 energy from Bloodletting, and the two exact kills are
    /// Phantasmal Gardeners taking 8 with 0 overkill, and the card removal is at the floor 13
    /// shop. The quick and flawless wins are the
    /// console-skipped fights, which count like any other win.
    /// </summary>
    [Fact]
    public void Act1Run_CompletesExactlyWhatHappened()
    {
        var done = Replay("main-ironclad-act1.jsonl");
        Assert.Equal(
        [
            "attacks_in_turn:4", "card_removals:1", "draw_in_turn:10", "draw_in_turn:8", "elites_defeated:2", "energy_in_turn:5",
            "exact_kills:2", "flawless_elite:-", "flawless_hallways:3", "gold_held:250", "gold_held:400",
            "quick_win:1", "quick_win:2", "shop_spree:2", "shop_spree:3", "shuffles_in_combat:2", "shuffles_in_combat:3",
        ], done.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("bought 3 items in one shop visit", done["shop_spree:3"]);
        Assert.Equal("held 408 gold", done["gold_held:400"]);
    }

    /// <summary>Continued at floor 13, used two potions in an elite, then died to it.</summary>
    [Fact]
    public void ContinuedRun_EndingInDeath_CompletesExactlyWhatHappened()
    {
        var done = Replay("main-ironclad-continue-death.jsonl");
        Assert.Equal(["potions_in_combat:2", "survive_hit:10"], done.Keys.Order(StringComparer.Ordinal));
    }
}
