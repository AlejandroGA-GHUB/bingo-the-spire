using System.Numerics;

namespace BingoTheSpire.Core.Randomness;

/// <summary>
/// xoshiro256** seeded through SplitMix64. Gives the same sequence for the same seed on every
/// machine and .NET version, unlike <see cref="System.Random"/>, whose algorithm is not guaranteed.
/// Never seed it from the clock or from the game's random streams.
/// </summary>
public sealed class SeededRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public SeededRandom(ulong seed)
    {
        var x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>Starts from a raw state, for checking against the reference implementation.</summary>
    internal SeededRandom(ulong s0, ulong s1, ulong s2, ulong s3) => (_s0, _s1, _s2, _s3) = (s0, s1, s2, s3);

    public static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextULong()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>A uniform integer in [0, <paramref name="maxExclusive"/>), without modulo bias.</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "must be positive");
        }
        var bound = (ulong)maxExclusive;
        var threshold = (0UL - bound) % bound;
        while (true)
        {
            var r = NextULong();
            if (r >= threshold)
            {
                return (int)(r % bound);
            }
        }
    }

    /// <summary>Fisher-Yates shuffle in place.</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];
}
