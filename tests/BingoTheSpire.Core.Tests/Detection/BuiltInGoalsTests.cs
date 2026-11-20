using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;

namespace BingoTheSpire.Core.Tests.Detection;

public class BuiltInGoalsTests
{
    private static readonly string[] Characters = ["IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT", "SOME_MODDED_HERO"];

    [Fact]
    public void LoadsAndIsValid()
    {
        var list = BuiltInGoals.List;
        Assert.Empty(list.Validate());
        Assert.InRange(list.Goals.Count, 40, 60);
    }

    [Fact]
    public void EveryGoal_UsesAKnownDetector_WithItsParameters()
    {
        foreach (var goal in BuiltInGoals.List.Goals)
        {
            Assert.Contains(goal.Detector, DetectorCatalog.Names);
            foreach (var tier in goal.Tiers)
            {
                DetectorCatalog.Create(goal.Detector, tier.Params ?? new Dictionary<string, int>());
            }
        }
    }

    [Fact]
    public void EveryDetector_NeedsOnlyEventsThatExist()
    {
        var eventTypes = typeof(GameEvent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(GameEvent)))
            .Select(t => t.Name)
            .ToHashSet();
        foreach (var detector in DetectorCatalog.Names)
        {
            Assert.All(DetectorCatalog.RequiredEvents(detector), e => Assert.Contains(e, eventTypes));
        }
    }

    [Fact]
    public void BoardsBuild_ForEveryCharacterModeAndSize()
    {
        foreach (var character in Characters)
        {
            foreach (var mode in Enum.GetValues<BingoMode>())
            {
                foreach (var gameMode in new[] { "Standard", "Blitz" })
                {
                    foreach (var difficulty in Enum.GetValues<BoardDifficulty>())
                    {
                        var board = BoardBuilder.Build(BuiltInGoals.List,
                            new BoardRequest("MP482SY6LRHH", mode, gameMode, [character], difficulty));
                        Assert.Equal(board.Size * board.Size, board.Squares.Select(s => s.GoalId).Distinct().Count());
                        _ = new GoalTracker(board, BuiltInGoals.List);
                    }
                }
            }
        }
    }

    [Fact]
    public void MediumBoards_HitTheirTargetDifficulties()
    {
        // With the real goal list every medium board should match its layout exactly, not fall
        // back to the nearest difficulty, so every line sums to the same total.
        for (var i = 0; i < 200; i++)
        {
            var seed = $"SEED{i:D8}";
            foreach (var gameMode in new[] { "Standard", "Blitz" })
            {
                var board = BoardBuilder.Build(BuiltInGoals.List, new BoardRequest(seed, BingoMode.Versus, gameMode, ["IRONCLAD"]));
                var totals = BoardRules.Lines(board.Size).Select(l => l.Sum(c => board.Squares[c].Difficulty)).Distinct().ToList();
                Assert.Single(totals);
            }
        }
    }

    [Fact]
    public void ModdedCharacter_GetsOnlyGeneralGoals()
    {
        var board = BoardBuilder.Build(BuiltInGoals.List, new BoardRequest("MP482SY6LRHH", BingoMode.Versus, "Standard", ["TIPHERA"]));
        var general = BuiltInGoals.List.Goals.Where(g => g.Requires?.Character == null).Select(g => g.Id).ToHashSet();
        Assert.All(board.Squares, s => Assert.Contains(s.GoalId, general));
    }

    [Fact]
    public void MissingEvents_TakeTheirGoalsOffTheBoard()
    {
        var all = typeof(GameEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(GameEvent))).Select(t => t.Name);
        var withoutPowers = all.Where(e => e != nameof(PowerChanged)).ToHashSet();
        var capabilities = new DetectionCapabilities(withoutPowers, DetectorCatalog.Available(withoutPowers));
        var board = BoardBuilder.Build(BuiltInGoals.List, new BoardRequest("MP482SY6LRHH", BingoMode.Versus, "Standard", ["IRONCLAD"]), capabilities);

        var powerGoals = BuiltInGoals.List.Goals
            .Where(g => DetectorCatalog.RequiredEvents(g.Detector).Contains(nameof(PowerChanged)))
            .Select(g => g.Id)
            .ToHashSet();
        Assert.NotEmpty(powerGoals);
        Assert.DoesNotContain(board.Squares, s => powerGoals.Contains(s.GoalId));
    }
}
