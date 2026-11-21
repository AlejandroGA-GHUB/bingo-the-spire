using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.Net;

namespace BingoTheSpire.Mod.DevTools;

// Dev console commands for trying out the bingo UI. The game finds console commands in mods by
// itself, and only offers debug commands (the default) when its own debug commands are on.
// They fill squares through the normal claim rules, so they're saved in the run's journal like
// real completions; a Continue brings them back.

/// <summary><c>bingo 5</c> fills row 5; <c>bingo reset</c> empties the board.</summary>
public sealed class BingoConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "bingo";

    public override string Args => "<row:int> [delay:seconds] | reset";

    public override string Description =>
        "Bingo the Spire: fill every square in a row (1 = top) to see the BINGO! and the line stroke. "
        + "Waits 1 second first (or the delay given) so you can close the console. 'bingo reset' empties the board (solo only).";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (DevCommands.Board() is not { } board)
        {
            return new CmdResult(success: false, "There's no bingo board in this run.");
        }
        var (session, player) = board;
        if (args.Length >= 1 && args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            if (BingoNet.Role != NetRole.Solo)
            {
                return new CmdResult(success: false, "'bingo reset' only works in solo: in co-op it would split your board from the others'.");
            }
            session.State.Reset();
            return new CmdResult(success: true, "Board emptied. Squares come back if you Continue this run.");
        }

        var size = session.Board.Size;
        if (args.Length < 1 || !DevCommands.TryNumber(args[0], 1, size, out var row))
        {
            return new CmdResult(success: false, $"Give a row from 1 to {size}, e.g. 'bingo 1'.");
        }
        if (!DevCommands.TryDelay(args, 1, out var delay))
        {
            return new CmdResult(success: false, "The delay must be a number of seconds, e.g. 'bingo 1 2.5'.");
        }

        var squares = Enumerable.Range((row - 1) * size, size).ToList();
        if (squares.All(sq => session.State.ClaimOf(sq, player) != null))
        {
            return new CmdResult(success: false, $"Row {row} is already done. 'bingo reset' empties the board.");
        }
        DevCommands.After(delay, () =>
        {
            foreach (var square in squares)
            {
                session.ClaimSquare(square, player, DevCommands.QuietEvidence);
            }
        });
        return new CmdResult(success: true, $"Filling row {row} in {delay.ToString("0.#", CultureInfo.InvariantCulture)}s.");
    }
}

/// <summary><c>fill 2 3</c> fills the square in row 2, column 3.</summary>
public sealed class BingoFillConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "fill";

    public override string Args => "<row:int> <col:int> [delay:seconds]";

    public override string Description =>
        "Bingo the Spire: fill one square (row and column from 1, top-left) as if its goal was done. "
        + "Waits 1 second first (or the delay given) so you can close the console.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (DevCommands.Board() is not { } board)
        {
            return new CmdResult(success: false, "There's no bingo board in this run.");
        }
        var (session, player) = board;
        var size = session.Board.Size;
        if (args.Length < 2 || !DevCommands.TryNumber(args[0], 1, size, out var row) || !DevCommands.TryNumber(args[1], 1, size, out var column))
        {
            return new CmdResult(success: false, $"Give a row and a column from 1 to {size}, e.g. 'fill 2 3'.");
        }
        if (!DevCommands.TryDelay(args, 2, out var delay))
        {
            return new CmdResult(success: false, "The delay must be a number of seconds, e.g. 'fill 2 3 0'.");
        }

        var square = (row - 1) * size + (column - 1);
        if (session.State.ClaimOf(square, player) != null)
        {
            return new CmdResult(success: false, $"Row {row}, column {column} is already done.");
        }
        DevCommands.After(delay, () => session.ClaimSquare(square, player, DevCommands.Evidence));
        return new CmdResult(success: true, $"Filling \"{session.Board.Squares[square].Text}\".");
    }
}

internal static class DevCommands
{
    /// <summary>Evidence on squares filled from the console.</summary>
    public const string Evidence = "filled from the dev console";

    /// <summary>Same, for whole rows: these skip the square pop-ups so only the BINGO! shows.</summary>
    public const string QuietEvidence = "filled from the dev console (row)";

    public static (BingoSession Session, ulong Player)? Board() =>
        ModEntry.Bingo is { Session: { } session } runtime ? (session, runtime.LocalPlayerId) : null;

    public static bool TryNumber(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    public static bool TryDelay(string[] args, int index, out double delay)
    {
        delay = 1;
        return args.Length <= index
            || (double.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out delay) && delay is >= 0 and <= 30);
    }

    /// <summary>Runs <paramref name="action"/> on the main thread after a delay, logging instead of throwing.</summary>
    public static void After(double seconds, Action action)
    {
        void Run()
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                BingoLog.Error($"dev command failed: {ex}");
            }
        }

        if (seconds <= 0 || Engine.GetMainLoop() is not SceneTree tree)
        {
            Run();
            return;
        }
        tree.CreateTimer(seconds).Timeout += Run;
    }
}
