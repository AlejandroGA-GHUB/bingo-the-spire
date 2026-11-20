using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Randomness;

namespace BingoTheSpire.Core.Tests.Boards;

public class BoardBuilderTests
{
    private static readonly string[] Seeds =
        ["7K2M9QX4ZB1C", "MP482SY6LRHH", "000000000000", "ZZZZZZZZZZZZ", "A", "1234567890AB", "QWERTY", "HJKLMNPQRSTU"];

    private static BoardRequest Request(string seed = "7K2M9QX4ZB1C", BingoMode mode = BingoMode.Versus,
        string gameMode = "Standard", params string[] characters) =>
        new(seed, mode, gameMode, characters.Length == 0 ? ["IRONCLAD"] : characters);

    private static IEnumerable<string> Ids(BingoBoard board) => board.Squares.Select(s => s.GoalId);

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Layout_PutsEachBandOnceInEveryLine(int size)
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var layout = DifficultyLayout.Create(size, new SeededRandom(seed));
            foreach (var line in BoardRules.Lines(size))
            {
                Assert.Equal(Enumerable.Range(0, size), line.Select(cell => layout[cell]).Order());
            }
        }
    }

    [Fact]
    public void SameInputs_GiveSameBoard()
    {
        var goals = TestGoals.Pool();
        foreach (var seed in Seeds)
        {
            var a = BoardBuilder.Build(goals, Request(seed));
            var b = BoardBuilder.Build(goals, Request(seed));
            Assert.Equal(Ids(a), Ids(b));
            Assert.Equal(a.ContentHash(), b.ContentHash());
        }
    }

    [Fact]
    public void GoalFileOrder_DoesNotChangeBoard()
    {
        var goals = TestGoals.Pool();
        var reversed = goals with { Goals = goals.Goals.Reverse().ToList() };
        Assert.Equal(Ids(BoardBuilder.Build(goals, Request())), Ids(BoardBuilder.Build(reversed, Request())));
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentBoards()
    {
        var goals = TestGoals.Pool();
        var boards = Seeds.Select(s => string.Join(",", Ids(BoardBuilder.Build(goals, Request(s))))).ToList();
        Assert.Equal(boards.Count, boards.Distinct().Count());
    }

    [Fact]
    public void TypedSeed_IsCleanedUpLikeTheGame()
    {
        var goals = TestGoals.Pool();
        Assert.Equal(Ids(BoardBuilder.Build(goals, Request("MP482SY6LRHH"))),
            Ids(BoardBuilder.Build(goals, Request("  mp482sy6lrhh "))));
        Assert.Equal(Ids(BoardBuilder.Build(goals, Request("1000"))), Ids(BoardBuilder.Build(goals, Request("iOoo"))));
    }

    [Fact]
    public void CustomAndDailyRuns_ShareTheStandardBoard_ButBlitzDoesNot()
    {
        var goals = TestGoals.Pool();
        var standard = Ids(BoardBuilder.Build(goals, Request(gameMode: "Standard")));
        Assert.Equal(standard, Ids(BoardBuilder.Build(goals, Request(gameMode: "Custom"))));
        Assert.Equal(standard, Ids(BoardBuilder.Build(goals, Request(gameMode: "Daily"))));
        Assert.Equal(standard, Ids(BoardBuilder.Build(goals, Request(gameMode: "Dread"))));

        var blitz = BoardBuilder.Build(goals, Request(gameMode: "Blitz"));
        Assert.Equal(4, blitz.Size);
        Assert.Equal(16, blitz.Squares.Count);
    }

    [Fact]
    public void ModeAndCharacters_ChangeTheBoard()
    {
        var goals = TestGoals.Pool();
        var versus = Ids(BoardBuilder.Build(goals, Request(mode: BingoMode.Versus)));
        Assert.NotEqual(versus, Ids(BoardBuilder.Build(goals, Request(mode: BingoMode.Lockout))));
        Assert.NotEqual(versus, Ids(BoardBuilder.Build(goals, Request(characters: "SILENT"))));
        // Player order doesn't matter.
        Assert.Equal(Ids(BoardBuilder.Build(goals, Request(characters: ["IRONCLAD", "SILENT"]))),
            Ids(BoardBuilder.Build(goals, Request(characters: ["SILENT", "IRONCLAD"]))));
    }

    [Theory]
    [InlineData("Standard")]
    [InlineData("Blitz")]
    public void EveryLine_HasTheSameTotalDifficulty(string gameMode)
    {
        var goals = TestGoals.Pool();
        foreach (var seed in Seeds)
        {
            var board = BoardBuilder.Build(goals, Request(seed, gameMode: gameMode));
            var totals = BoardRules.Lines(board.Size).Select(line => line.Sum(cell => board.Squares[cell].Difficulty)).Distinct();
            Assert.Single(totals);
        }
    }

    [Fact]
    public void NoGoalAppearsTwice_EvenWithOtherParameters()
    {
        var multiTier = Enumerable.Range(0, 30).Select(i => new GoalDefinition(
            $"tiered_{i}", "Do {n} things", "Test",
            [new GoalTier(1, new Dictionary<string, int> { ["n"] = 1 }),
             new GoalTier(2, new Dictionary<string, int> { ["n"] = 2 }),
             new GoalTier(3, new Dictionary<string, int> { ["n"] = 3 }),
             new GoalTier(4, new Dictionary<string, int> { ["n"] = 4 })])).ToList();
        var goals = new GoalList(1, multiTier);
        foreach (var seed in Seeds)
        {
            var board = BoardBuilder.Build(goals, Request(seed));
            Assert.Equal(25, Ids(board).Distinct().Count());
            Assert.All(board.Squares, s => Assert.Equal($"Do {s.Params["n"]} things", s.Text));
        }
    }

    [Fact]
    public void Exclusions_AreRespectedBoardWide()
    {
        // Only 26 goals, and two of them exclude each other: every board must contain exactly one of the pair.
        var goals = TestGoals.Pool(perDifficulty: 5);
        var trimmed = goals with
        {
            Goals = goals.Goals.Take(24).Concat(
            [
                new GoalDefinition("a", "A", "Test", [new GoalTier(3)], Exclusions: ["b"]),
                new GoalDefinition("b", "B", "Test", [new GoalTier(3)]),
            ]).ToList(),
        };
        foreach (var seed in Seeds)
        {
            var ids = Ids(BoardBuilder.Build(trimmed, Request(seed))).ToHashSet();
            Assert.True(ids.Contains("a") ^ ids.Contains("b"));
        }
    }

    [Fact]
    public void TagsDoNotRepeatWithinALine_WhenThePoolAllowsIt()
    {
        // A center cell shares a line with 16 others, so the pool needs more than 16 tags at every difficulty.
        var goals = TestGoals.Pool(perDifficulty: 24, tagCount: 24);
        var tagOf = goals.Goals.ToDictionary(g => g.Id, g => g.Tags![0]);
        foreach (var seed in Seeds)
        {
            var board = BoardBuilder.Build(goals, Request(seed));
            foreach (var line in BoardRules.Lines(5))
            {
                var tags = line.Select(cell => tagOf[board.Squares[cell].GoalId]).ToList();
                Assert.Equal(tags.Count, tags.Distinct().Count());
            }
        }
    }

    [Fact]
    public void CharacterGoals_NeedEveryPlayer_ExceptInTeam()
    {
        // Pool with exactly 25 general goals + one Silent goal at difficulty 1 that must be used when allowed.
        var general = TestGoals.Pool(perDifficulty: 5);
        var silent = new GoalDefinition("silent_only", "Silent thing", "Test", [new GoalTier(1)],
            Requires: new GoalRequirements(Character: "SILENT"));
        var goals = TestGoals.With(general, silent);

        List<GoalDefinition> Eligible(BoardRequest r) => BoardBuilder.EligibleGoals(goals, r, "Standard", DetectionCapabilities.All);

        Assert.Contains(silent, Eligible(Request(characters: "SILENT")));
        Assert.DoesNotContain(silent, Eligible(Request(characters: ["SILENT", "IRONCLAD"])));
        Assert.DoesNotContain(silent, Eligible(Request(mode: BingoMode.Lockout, characters: ["SILENT", "IRONCLAD"])));
        Assert.Contains(silent, Eligible(Request(mode: BingoMode.Team, characters: ["SILENT", "IRONCLAD"])));
        // A modded character only gets general goals.
        Assert.DoesNotContain(silent, Eligible(Request(characters: "TIPHERA")));
    }

    [Fact]
    public void GoalsThatCannotBeDetected_AreLeftOut()
    {
        var general = TestGoals.Pool();
        var needsOrbs = new GoalDefinition("orbs", "Channel orbs", "Test", [new GoalTier(1)],
            Requires: new GoalRequirements(Events: ["OrbChanneled"]));
        var otherDetector = new GoalDefinition("future", "Future goal", "NotWrittenYet", [new GoalTier(1)]);
        var goals = TestGoals.With(general, needsOrbs, otherDetector);
        var caps = new DetectionCapabilities(new HashSet<string> { "CardPlayed" }, new HashSet<string> { "Test" });

        var eligible = BoardBuilder.EligibleGoals(goals, Request(), "Standard", caps);
        Assert.DoesNotContain(needsOrbs, eligible);
        Assert.DoesNotContain(otherDetector, eligible);
        Assert.Contains(needsOrbs, BoardBuilder.EligibleGoals(goals, Request(), "Standard", DetectionCapabilities.All));
    }

    [Fact]
    public void BlitzExcludedGoals_StayOffBlitzBoards()
    {
        var general = TestGoals.Pool(perDifficulty: 5);
        var longGoal = new GoalDefinition("act3", "Beat the Act 3 boss", "Test", [new GoalTier(1)], ExcludeGameModes: ["Blitz"]);
        var goals = TestGoals.With(general, longGoal);
        Assert.DoesNotContain(longGoal, BoardBuilder.EligibleGoals(goals, Request(gameMode: "Blitz"), "Blitz", DetectionCapabilities.All));
        Assert.Contains(longGoal, BoardBuilder.EligibleGoals(goals, Request(), "Standard", DetectionCapabilities.All));
    }

    [Fact]
    public void TooFewGoals_FailsClearly()
    {
        var goals = TestGoals.Pool(perDifficulty: 4);
        var ex = Assert.Throws<BoardBuildException>(() => BoardBuilder.Build(goals, Request()));
        Assert.Contains("20 goals", ex.Message);
    }

    [Fact]
    public void InvalidSeed_FailsClearly() =>
        Assert.Throws<BoardBuildException>(() => BoardBuilder.Build(TestGoals.Pool(), Request("not a seed!")));

    /// <summary>
    /// Pins exact boards. If this fails, the hash, random number generator or builder changed and
    /// every existing board code would now give a different board. Only update the expected
    /// values together with a goal-list version bump.
    /// </summary>
    [Fact]
    public void KnownAnswer_BoardsArePinned()
    {
        var goals = TestGoals.Pool();
        Assert.Equal(KnownAnswers.Standard, string.Join(" ", Ids(BoardBuilder.Build(goals, Request("7K2M9QX4ZB1C")))));
        Assert.Equal(KnownAnswers.Blitz, string.Join(" ", Ids(BoardBuilder.Build(goals, Request("MP482SY6LRHH", gameMode: "Blitz")))));
    }

    private static class KnownAnswers
    {
        public const string Standard =
            "g4_08 g1_11 g2_11 g3_02 g3_11 g2_03 g3_03 g3_07 g4_09 g1_08 g3_05 g4_05 g1_07 g2_02 g3_09 "
            + "g1_04 g2_09 g3_00 g3_04 g4_06 g3_01 g3_06 g4_11 g1_05 g2_07";

        public const string Blitz =
            "g3_07 g1_06 g3_11 g2_09 g2_03 g3_02 g1_05 g3_09 g1_00 g3_01 g2_07 g3_03 g3_08 g2_04 g3_06 g1_10";
    }
}
