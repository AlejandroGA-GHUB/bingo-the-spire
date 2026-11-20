namespace BingoTheSpire.Core.Boards;

/// <summary>
/// Bingo seeds use the game's own seed format, so a run seed can be used as a bingo seed and
/// players can type either in the same way.
/// </summary>
public static class BingoSeed
{
    /// <summary>The game's seed characters. O and I are left out because they look like 0 and 1.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Length of the seeds the game generates.</summary>
    public const int DefaultLength = 12;

    public const int MaxLength = 32;

    /// <summary>Cleans up a typed seed exactly like the game does: uppercase, O→0, I→1, trim.</summary>
    public static string Canonicalize(string seed) =>
        seed.ToUpperInvariant().Replace('O', '0').Replace('I', '1').Trim();

    /// <summary>True for a canonical seed made only of the game's seed characters.</summary>
    public static bool IsValid(string canonical) =>
        canonical.Length is > 0 and <= MaxLength && canonical.All(c => Alphabet.Contains(c));
}
