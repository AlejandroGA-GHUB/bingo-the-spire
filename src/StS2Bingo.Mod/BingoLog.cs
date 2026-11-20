using MegaCrit.Sts2.Core.Logging;

namespace StS2Bingo.Mod;

/// <summary>Writes to the game's own log with a [StS2Bingo] prefix.</summary>
internal static class BingoLog
{
    private const string Prefix = "[StS2Bingo] ";

    public static void Info(string text) => Log.Info(Prefix + text, 3);

    public static void Warn(string text) => Log.Warn(Prefix + text, 3);

    public static void Error(string text) => Log.Error(Prefix + text, 3);
}
