using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.Tests.Session;

public class BingoSetupTests
{
    private static readonly string[] Characters = ["IRONCLAD"];

    private static BoardRequest Request(string? entry, BingoMode mode = BingoMode.Lockout, bool coop = true) =>
        new BingoSetup(true, entry, mode).RequestFor("RUNSEED12345", "Standard", Characters, coop, goalListVersion: 1);

    [Fact]
    public void EmptyEntry_UsesTheRunSeed()
    {
        var request = Request("  ");
        Assert.Equal(("RUNSEED12345", BingoMode.Lockout), (request.SeedText, request.Mode));
    }

    [Fact]
    public void Seed_IsCleanedUpLikeTheGameDoes()
    {
        Assert.Equal("B1NG0SEED", Request(" bingoseed ").SeedText);
    }

    [Fact]
    public void Solo_AlwaysPlaysVersus()
    {
        Assert.Equal(BingoMode.Versus, Request(null, BingoMode.Lockout, coop: false).Mode);
    }

    [Fact]
    public void BoardCode_ReproducesItsBoard_ModeSizeAndDifficultyIncluded()
    {
        var request = Request("7k2m9qx4zb1c-1t4h", BingoMode.Versus, coop: false);
        Assert.Equal(("7K2M9QX4ZB1C", BingoMode.Team, 4, BoardDifficulty.Hard),
            (request.SeedText, request.Mode, request.Size, request.Difficulty));
    }

    [Fact]
    public void BoardCode_ForAnotherGoalListVersion_IsRefusedClearly()
    {
        var ex = Assert.Throws<BoardBuildException>(() => Request("7K2M9QX4ZB1C-2V5M"));
        Assert.Contains("goal list v2", ex.Message);
    }

    [Theory]
    [InlineData("not a seed!")]
    [InlineData("ABC-XYZ")]
    public void BadEntries_AreRefusedClearly(string entry)
    {
        Assert.Equal(SeedEntryKind.Invalid, SeedEntryParser.Parse(entry).Kind);
        Assert.Throws<BoardBuildException>(() => Request(entry));
    }
}
