#if DEBUG
using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using BingoTheSpire.Core.Session;
using BingoTheSpire.Mod.Net;

namespace BingoTheSpire.Mod.DevTools;

// Dev console commands for trying out the bingo UI. Debug builds only: the game turns its debug
// commands on whenever any mod is loaded, so release builds must not contain these at all.
// The game finds console commands in mods by itself.
// They fill squares through the normal claim rules, so they're saved in the run's journal like
// real completions; a Continue brings them back.

/// <summary><c>bingo 5</c> fills row 5; <c>bingo reset</c> empties the board.</summary>
public sealed class BingoConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "bingo";

    public override string Args => "<row:int> [delay:seconds] | reset | results | history";

    public override string Description =>
        "Bingo the Spire: fill every square in a row (1 = top) to see the BINGO! and the line stroke. "
        + "Waits 1 second first (or the delay given) so you can close the console. 'bingo reset' empties the board (solo only). "
        + "'bingo results' previews the end-of-run results (not saved); 'bingo history' opens past runs.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var word = args.Length >= 1 ? args[0].ToLowerInvariant() : "";
        if (word == "history")
        {
            DevCommands.After(0.3, () => Ui.BingoOverlay.Current?.ShowHistory());
            return new CmdResult(success: true, "Opening past runs.");
        }
        if (DevCommands.Board() is not { } board)
        {
            return new CmdResult(success: false, "There's no bingo board in this run.");
        }
        if (word == "results")
        {
            if (ModEntry.Bingo?.PreviewResult() is not { } entry)
            {
                return new CmdResult(success: false, "No results to show yet.");
            }
            Ui.BingoOverlay.Current?.ShowResults(entry, delay: 1);
            return new CmdResult(success: true, "Showing the results as if the run ended now (not saved).");
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

/// <summary>
/// <c>tie 2 3</c> fills a square for you, as if at a moment every player can share. When every
/// player types it for the same square, their claims tie (spec 4.3.2), through the real network path.
/// </summary>
public sealed class BingoTieConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "tie";

    public override string Args => "<row:int> <col:int> [delay:seconds]";

    public override string Description =>
        "Bingo the Spire, Lockout: fill one square for you only, at a shared test moment. Every player who types "
        + "'tie' for the same square ties for it, like completing it in the same game action. Waits 1 second first (or the delay given).";

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
            return new CmdResult(success: false, $"Give a row and a column from 1 to {size}, e.g. 'tie 2 3'.");
        }
        if (!DevCommands.TryDelay(args, 2, out var delay))
        {
            return new CmdResult(success: false, "The delay must be a number of seconds, e.g. 'tie 2 3 0'.");
        }
        var square = (row - 1) * size + (column - 1);
        if (session.State.ClaimOf(square, player) != null)
        {
            return new CmdResult(success: false, $"Row {row}, column {column} is already yours.");
        }
        var note = session.Board.Mode == BingoTheSpire.Core.Boards.BingoMode.Lockout ? "" : " (ties only happen in Lockout)";
        DevCommands.After(delay, () => session.ClaimSquare(square, player, DevCommands.Evidence, moment: $"dev:{square}"));
        return new CmdResult(success: true, $"Filling \"{session.Board.Squares[square].Text}\" at a shared test moment{note}.");
    }
}

/// <summary>
/// <c>swap 2 3 flawless_elite 3</c> puts a different goal on an empty square, on your game only.
/// Every player must type the same swap, or the host refuses claims for that square.
/// </summary>
public sealed class BingoSwapConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "swap";

    public override string Args => "<row:int> <col:int> <goal_id> [difficulty:1-5]";

    public override string Description =>
        "Bingo the Spire: put a different goal on an empty square, e.g. 'swap 3 3 flawless_elite'. On your game only: "
        + "in co-op every player must type the same swap. Not saved: a Continue brings the original board back. "
        + "'swap list' shows the goal ids.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var goals = BingoTheSpire.Core.Goals.BuiltInGoals.List.Goals;
        if (args.Length >= 1 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return new CmdResult(success: true, string.Join(", ", goals.Select(g => g.Id).OrderBy(id => id, StringComparer.Ordinal)));
        }
        if (DevCommands.Board() is not { } board)
        {
            return new CmdResult(success: false, "There's no bingo board in this run.");
        }
        var (session, player) = board;
        var size = session.Board.Size;
        if (args.Length < 3 || !DevCommands.TryNumber(args[0], 1, size, out var row) || !DevCommands.TryNumber(args[1], 1, size, out var column))
        {
            return new CmdResult(success: false, $"Give a row and a column from 1 to {size} and a goal id, e.g. 'swap 3 3 flawless_elite'. 'swap list' shows the ids.");
        }
        var goal = goals.FirstOrDefault(g => g.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase));
        if (goal == null)
        {
            return new CmdResult(success: false, $"No goal '{args[2]}'. 'swap list' shows the ids.");
        }
        var difficulty = 0;
        if (args.Length >= 4 && !DevCommands.TryNumber(args[3], 1, 5, out difficulty))
        {
            return new CmdResult(success: false, "The difficulty must be from 1 to 5.");
        }
        // The tier at that difficulty, or the closest (the first if none was given).
        var tier = difficulty == 0 ? goal.Tiers[0] : goal.Tiers.OrderBy(t => Math.Abs(t.Difficulty - difficulty)).First();
        var square = (row - 1) * size + (column - 1);
        if (session.SwapGoal(square, goal, tier) is not { } swapped)
        {
            return new CmdResult(success: false, $"Row {row}, column {column} is already filled.");
        }
        Ui.BingoOverlay.Current?.Bind(session, player);
        BingoLog.Info($"dev console: square {row},{column} is now \"{swapped.Text}\" (difficulty {swapped.Difficulty})");
        var coop = BingoNet.Role == NetRole.Solo ? "" : " Every other player must type the same swap.";
        return new CmdResult(success: true, $"Row {row}, column {column} is now \"{swapped.Text}\".{coop}");
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
#endif
