using BingoTheSpire.Core.Boards;

namespace BingoTheSpire.Core.Tests.Boards;

public class OneAwayTests
{
    [Fact]
    public void ASquareThatWouldFinishALine_IsOneAway()
    {
        // 3x3: top row has 0 and 1, so 2 finishes it; the diagonal 0,4,8 still needs two.
        var oneAway = BoardRules.OneAway(3, new HashSet<int> { 0, 1 });
        Assert.Equal([2], oneAway);
    }

    [Fact]
    public void SeveralLines_CanShareTheMissingSquare_AndFinishedLinesDontCount()
    {
        // Row 0 is done; column 0 (0,3,6) and the diagonal (0,4,8) both need 6 / 8.
        var oneAway = BoardRules.OneAway(3, new HashSet<int> { 0, 1, 2, 3, 4 });
        Assert.Equal(new HashSet<int> { 5, 6, 8, 7 }, oneAway.ToHashSet());
    }

    [Fact]
    public void ALineWithABlockedSquare_IsNeverOneAway()
    {
        var oneAway = BoardRules.OneAway(3, new HashSet<int> { 0, 1 }, blocked: new HashSet<int> { 2 });
        Assert.Empty(oneAway);
    }
}
