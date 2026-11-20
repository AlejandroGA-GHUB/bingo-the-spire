using StS2Bingo.Core.Hashing;

namespace StS2Bingo.Core.Tests.Hashing;

public class StableHashTests
{
    // Published FNV-1a 64 test vectors. If these change, every board for every seed changes too.
    [Theory]
    [InlineData("", 0xcbf29ce484222325UL)]
    [InlineData("a", 0xaf63dc4c8601ec8cUL)]
    [InlineData("foobar", 0x85944171f73967e8UL)]
    public void Fnv1a64_MatchesKnownVectors(string input, ulong expected)
    {
        Assert.Equal(expected, StableHash.Fnv1a64(input));
    }

    [Fact]
    public void Fnv1a64_IsCaseSensitive()
    {
        Assert.NotEqual(StableHash.Fnv1a64("7K2M9QX4ZB1C"), StableHash.Fnv1a64("7k2m9qx4zb1c"));
    }
}
