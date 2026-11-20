using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Serialization;

namespace BingoTheSpire.Core.Recording;

/// <summary>First line of a run journal: which run and which board it belongs to.</summary>
public sealed record JournalHeader(string Format, string Seed, long StartTime, string BoardCode, string BoardHash)
{
    public const string CurrentFormat = "bingothespire-journal/1";
}

/// <summary>
/// The events of one run, kept so Continue can bring bingo progress back (spec section 8).
/// Instead of saving each detector's state, the journal keeps the events themselves: replaying
/// them into a fresh session rebuilds every square, line and in-progress counter exactly.
/// <para>
/// The game resumes a run from its last save and plays everything after it again, so only the
/// events up to the last <see cref="GameSaved"/> are replayed.
/// </para>
/// </summary>
public sealed class RunJournal : IDisposable
{
    private readonly TextWriter _writer;

    private RunJournal(TextWriter writer)
    {
        _writer = writer;
    }

    /// <summary>Starts a journal: writes the header and the events kept from before a Continue.</summary>
    public static RunJournal Start(TextWriter writer, JournalHeader header, IEnumerable<GameEvent> kept)
    {
        var journal = new RunJournal(writer);
        writer.WriteLine(BingoJson.Serialize(header));
        foreach (var evt in kept)
        {
            writer.WriteLine(BingoJson.Serialize(evt));
        }
        writer.Flush();
        return journal;
    }

    /// <summary>Keeps adding to a journal already started with <see cref="Start"/>.</summary>
    public static RunJournal Reopen(TextWriter writer) => new(writer);

    public void Append(GameEvent evt)
    {
        _writer.WriteLine(BingoJson.Serialize(evt));
        _writer.Flush(); // quitting or crashing at any moment must still leave a usable journal
    }

    public void Dispose() => _writer.Dispose();

    /// <summary>Reads a journal. Lines that can't be read (e.g. cut off by a crash) are skipped.</summary>
    public static (JournalHeader? Header, List<GameEvent> Events) Read(TextReader reader)
    {
        JournalHeader? header = null;
        var events = new List<GameEvent>();
        var first = true;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                if (first)
                {
                    header = BingoJson.DeserializeJournalHeader(line);
                }
                else if (BingoJson.DeserializeEvent(line) is { } evt)
                {
                    events.Add(evt);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A partly written last line: everything before it is still good.
            }
            first = false;
        }
        return (header, events);
    }

    /// <summary>The events a Continue keeps: everything up to and including the last <see cref="GameSaved"/>.</summary>
    public static IReadOnlyList<GameEvent> UpToLastSave(IReadOnlyList<GameEvent> events)
    {
        for (var i = events.Count - 1; i >= 0; i--)
        {
            if (events[i] is GameSaved)
            {
                return events.Take(i + 1).ToList();
            }
        }
        return [];
    }

    /// <summary>File name for a run's journal, from its seed and start time.</summary>
    public static string FileName(string seed, long startTime)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(seed.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return $"{(safe.Length == 0 ? "noseed" : safe)}_{startTime}.jsonl";
    }
}
