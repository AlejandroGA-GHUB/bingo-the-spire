using System.Text.Json;
using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Serialization;
using BingoTheSpire.Core.Session;

namespace BingoTheSpire.Core.History;

/// <summary>One player in a finished bingo run.</summary>
public sealed record HistoryPlayer(ulong Id, string Name, string Character, int Squares, int Lines, int? Place);

/// <summary>A finished line: whose, which, and when (floor and play time).</summary>
public sealed record HistoryLine(ulong Player, int Line, int Ordinal, int? Act, int? Floor, long Ms);

/// <summary>
/// One finished bingo run, one JSON line in <c>bingo_history.jsonl</c> (spec 8).
/// <paramref name="Result"/> is the run's own result ("won", "lost", "abandoned"), separate from the bingo.
/// The last four were added later and are null in older entries.
/// </summary>
/// <param name="EndAct">0-based act the run ended in.</param>
/// <param name="DifficultyTotals">Squares on the board per difficulty, 1 to 5.</param>
/// <param name="DifficultyDone">Squares you (or your team) filled per difficulty, 1 to 5.</param>
public sealed record HistoryEntry(
    int Version,
    long Date,
    string Mode,
    bool Solo,
    string Seed,
    string BoardCode,
    string Difficulty,
    int Size,
    int Squares,
    string Result,
    long PlayMs,
    ulong You,
    List<HistoryPlayer> Players,
    List<HistoryLine> Lines,
    ulong? Winner,
    string? WinReason,
    int? EndAct = null,
    int? EndFloor = null,
    int[]? DifficultyTotals = null,
    int[]? DifficultyDone = null)
{
    public const int CurrentVersion = 1;

    /// <summary>Your first line, if you (or your team) made one.</summary>
    public HistoryLine? FirstLine => Lines
        .Where(l => l.Ordinal == 1 && (Mode == nameof(BingoMode.Team) || l.Player == You))
        .OrderBy(l => l.Ms)
        .FirstOrDefault();

    public HistoryPlayer? Me => Players.FirstOrDefault(p => p.Id == You);
}

/// <summary>Personal bests across the history (spec 8).</summary>
/// <param name="FewestFloors">Earliest first line by floor.</param>
/// <param name="FastestTime">Earliest first line by play time.</param>
/// <param name="MostTeamSquares">Most squares the team filled in one Team run.</param>
public sealed record PersonalBests(HistoryEntry? FewestFloors, HistoryEntry? FastestTime, HistoryEntry? MostTeamSquares, int Runs, int Bingos);

public static class RunHistory
{
    public const string FileName = "bingo_history.jsonl";

    /// <summary>Builds the entry for a run that just ended.</summary>
    public static HistoryEntry Record(BingoSession session, RunAttached run, RunEnded ended, ulong you,
        Func<ulong, string> name, DateTimeOffset now)
    {
        var state = session.State;
        var board = session.Board;
        var team = board.Mode == BingoMode.Team;
        var players = run.Players.Select(p =>
        {
            // Players who drew share a place (spec 4.3.2).
            return new HistoryPlayer(p.PlayerId, name(p.PlayerId), p.CharacterId,
                team ? state.FilledBy(p.PlayerId).Count : state.FilledFor(p.PlayerId).Count,
                state.LinesFor(p.PlayerId).Count, state.PlaceOf(p.PlayerId));
        }).ToList();

        var lines = session.LineTimes
            .Select(kv => (kv.Key.Player, kv.Key.Line, Ms: kv.Value))
            .OrderBy(l => l.Ms)
            .ToList();
        var ordinals = new Dictionary<ulong, int>();
        var historyLines = new List<HistoryLine>();
        foreach (var (player, line, ms) in lines)
        {
            var key = team ? 0UL : player;
            ordinals[key] = ordinals.GetValueOrDefault(key) + 1;
            session.LinePositions.TryGetValue((player, line), out var position);
            historyLines.Add(new HistoryLine(player, line, ordinals[key], position?.Act, position?.Floor, ms));
        }

        return new HistoryEntry(
            HistoryEntry.CurrentVersion,
            now.ToUnixTimeMilliseconds(),
            board.Mode.ToString(),
            run.Players.Count < 2,
            board.SeedText,
            board.Code,
            board.Difficulty.ToString(),
            board.Size,
            board.Squares.Count,
            ended.Abandoned ? "abandoned" : ended.Victory ? "won" : "lost",
            session.PlayMs,
            you,
            players,
            historyLines,
            state.Winner?.PlayerId,
            state.Winner?.Reason.ToString(),
            session.Position?.Act,
            session.Position?.Floor,
            PerDifficulty(board, Enumerable.Range(0, board.Squares.Count)),
            PerDifficulty(board, state.FilledFor(you)));
    }

    private static int[] PerDifficulty(BingoBoard board, IEnumerable<int> squares)
    {
        var counts = new int[5];
        foreach (var square in squares)
        {
            counts[Math.Clamp(board.Squares[square].Difficulty, 1, 5) - 1]++;
        }
        return counts;
    }

    public static string Serialize(HistoryEntry entry) => JsonSerializer.Serialize(entry, BingoJsonContext.Default.HistoryEntry);

    /// <summary>Reads every entry it can; broken or unknown lines are skipped, never fatal.</summary>
    public static List<HistoryEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<HistoryEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                if (JsonSerializer.Deserialize(line, BingoJsonContext.Default.HistoryEntry) is { Version: >= 1 } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
            }
        }
        return entries;
    }

    public static PersonalBests Bests(IReadOnlyList<HistoryEntry> entries)
    {
        var withLine = entries.Where(e => e.FirstLine != null).ToList();
        return new PersonalBests(
            withLine.Where(e => e.FirstLine!.Floor != null).OrderBy(e => e.FirstLine!.Floor).ThenBy(e => e.FirstLine!.Ms).FirstOrDefault(),
            withLine.OrderBy(e => e.FirstLine!.Ms).FirstOrDefault(),
            entries.Where(e => e.Mode == nameof(BingoMode.Team)).OrderByDescending(e => e.Players.Sum(p => p.Squares)).FirstOrDefault(),
            entries.Count,
            withLine.Count);
    }

    /// <summary>"1:23:45" or "12:05".</summary>
    public static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
