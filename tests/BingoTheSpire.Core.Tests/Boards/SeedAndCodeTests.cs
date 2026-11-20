using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Goals;
using BingoTheSpire.Core.Serialization;

namespace BingoTheSpire.Core.Tests.Boards;

public class SeedAndCodeTests
{
    [Theory]
    [InlineData("mp482sy6lrhh", "MP482SY6LRHH")]
    [InlineData("  ABC  ", "ABC")]
    [InlineData("OIoi", "0101")]
    public void Canonicalize_MatchesTheGame(string typed, string expected) =>
        Assert.Equal(expected, BingoSeed.Canonicalize(typed));

    [Theory]
    [InlineData("MP482SY6LRHH", true)]
    [InlineData("A", true)]
    [InlineData("", false)]
    [InlineData("AB CD", false)]
    [InlineData("ABC!", false)]
    [InlineData("O", false)] // not canonical: the game never shows O
    public void IsValid(string seed, bool expected) => Assert.Equal(expected, BingoSeed.IsValid(seed));

    [Fact]
    public void BoardCode_RoundTrips()
    {
        var board = BoardBuilder.Build(TestGoals.Pool(), new BoardRequest("7K2M9QX4ZB1C", BingoMode.Lockout, "Blitz", ["IRONCLAD"]));
        Assert.Equal("7K2M9QX4ZB1C-1L4M", board.Code);

        Assert.True(BoardCode.TryParse(board.Code.ToLowerInvariant(), out var parsed));
        Assert.Equal(new BoardCode.Parsed("7K2M9QX4ZB1C", 1, BingoMode.Lockout, 4, BoardDifficulty.Medium), parsed);

        var rebuilt = BoardBuilder.Build(TestGoals.Pool(),
            new BoardRequest(parsed!.SeedText, parsed.Mode, "Blitz", ["IRONCLAD"], parsed.Difficulty, parsed.Size));
        Assert.Equal(board.ContentHash(), rebuilt.ContentHash());
    }

    [Theory]
    [InlineData("7K2M9QX4ZB1C")]
    [InlineData("7K2M9QX4ZB1C-")]
    [InlineData("7K2M9QX4ZB1C-1X5M")]
    [InlineData("7K2M9QX4ZB1C-1V6M")]
    [InlineData("7K2M9QX4ZB1C-1V5Q")]
    [InlineData("-1V5M")]
    [InlineData("7K2M9QX4ZB1C-V5M")]
    public void BoardCode_RejectsMalformedCodes(string text) => Assert.False(BoardCode.TryParse(text, out _));

    [Fact]
    public void GoalList_LoadsFromCommentedJson()
    {
        const string json = """
            {
              // hand-edited
              "version": 3,
              "goals": [
                {
                  "id": "block_in_turn",
                  "text": "Gain {n}+ Block in a single turn",
                  "detector": "BlockInTurn",
                  "tiers": [ { "difficulty": 1, "params": { "n": 30 } }, { "difficulty": 3, "params": { "n": 60 } } ],
                  "tags": ["block"],
                  "requires": { "events": ["BlockGained"] },
                },
              ],
            }
            """;
        var list = BingoJson.DeserializeGoalList(json)!;
        Assert.Equal(3, list.Version);
        var goal = Assert.Single(list.Goals);
        Assert.Equal(60, goal.Tiers[1].Params!["n"]);
        Assert.Equal(["BlockGained"], goal.Requires!.Events!);
        Assert.Empty(list.Validate());
    }

    [Fact]
    public void GoalList_ValidationCatchesMistakes()
    {
        var list = new GoalList(1,
        [
            new GoalDefinition("a", "Do {n} things", "Test", [new GoalTier(2)]),
            new GoalDefinition("a", "Dup", "Test", [new GoalTier(9)]),
            new GoalDefinition("c", "C", "", [], Exclusions: ["missing"]),
        ]);
        var errors = list.Validate();
        Assert.Contains(errors, e => e.Contains("{n}"));
        Assert.Contains(errors, e => e.Contains("duplicate"));
        Assert.Contains(errors, e => e.Contains("difficulty 9"));
        Assert.Contains(errors, e => e.Contains("no detector"));
        Assert.Contains(errors, e => e.Contains("no tiers"));
        Assert.Contains(errors, e => e.Contains("unknown goal 'missing'"));
    }
}
