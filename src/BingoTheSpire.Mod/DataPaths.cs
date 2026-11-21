using Godot;

namespace BingoTheSpire.Mod;

/// <summary>Where the mod keeps its files: a folder inside the game's own user data folder.</summary>
internal static class DataPaths
{
    public static string Root => ProjectSettings.GlobalizePath("user://bingo_the_spire");

    public static string Recordings => System.IO.Path.Combine(Root, "recordings");

    /// <summary>One journal per run in progress, so Continue can bring bingo progress back.</summary>
    public static string Runs => System.IO.Path.Combine(Root, "runs");

    /// <summary>One line per finished bingo run (spec 8).</summary>
    public static string History => System.IO.Path.Combine(Root, BingoTheSpire.Core.History.RunHistory.FileName);
}
