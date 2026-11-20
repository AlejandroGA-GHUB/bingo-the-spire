using BingoTheSpire.Core.Randomness;

namespace BingoTheSpire.Core.Tests.Randomness;

public class SeededRandomTests
{
    [Fact]
    public void SplitMix64_MatchesReferenceOutput()
    {
        ulong state = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, SeededRandom.SplitMix64(ref state));
    }

    [Fact]
    public void Xoshiro256StarStar_MatchesReferenceOutput()
    {
        // Reference implementation (prng.di.unimi.it) started from state {1, 2, 3, 4}.
        var rng = new SeededRandom(1, 2, 3, 4);
        Assert.Equal(11520UL, rng.NextULong());
        Assert.Equal(0UL, rng.NextULong());
        Assert.Equal(1509978240UL, rng.NextULong());
        Assert.Equal(1215971899390074240UL, rng.NextULong());
    }

    [Fact]
    public void SameSeed_GivesSameSequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
        }
    }

    [Fact]
    public void Next_StaysInRange_AndHitsEveryValue()
    {
        var rng = new SeededRandom(7);
        var seen = new int[6];
        for (var i = 0; i < 6000; i++)
        {
            seen[rng.Next(6)]++;
        }
        Assert.All(seen, count => Assert.InRange(count, 800, 1200));
    }

    [Fact]
    public void Shuffle_KeepsEveryItem()
    {
        var list = Enumerable.Range(0, 50).ToList();
        new SeededRandom(3).Shuffle(list);
        Assert.Equal(Enumerable.Range(0, 50), list.Order());
        Assert.NotEqual(Enumerable.Range(0, 50), list);
    }
}
