using Godot;

namespace StS2Bingo.Mod;

/// <summary>Where the mod keeps its files: a folder inside the game's own user data folder.</summary>
internal static class DataPaths
{
    public static string Root => ProjectSettings.GlobalizePath("user://sts2_bingo");

    public static string Recordings => System.IO.Path.Combine(Root, "recordings");
}
