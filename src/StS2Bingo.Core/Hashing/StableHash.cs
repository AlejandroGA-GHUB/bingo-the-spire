using System.Text;

namespace StS2Bingo.Core.Hashing;

/// <summary>
/// A 64-bit FNV-1a hash that gives the same result on every machine, process and .NET version.
/// Board generation depends on this. Never use <see cref="string.GetHashCode()"/> for anything
/// that has to be deterministic: .NET randomizes it on every launch.
/// </summary>
public static class StableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Fnv1a64(ReadOnlySpan<byte> data)
    {
        var hash = OffsetBasis;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= Prime;
        }
        return hash;
    }

    /// <summary>Hashes the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static ulong Fnv1a64(string text) => Fnv1a64(Encoding.UTF8.GetBytes(text));
}
