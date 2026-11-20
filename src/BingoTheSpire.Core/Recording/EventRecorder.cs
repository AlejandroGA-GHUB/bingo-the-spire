using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Serialization;

namespace BingoTheSpire.Core.Recording;

/// <summary>Whether a game event source could be connected on this game version.</summary>
/// <param name="Source">Hook or patch name.</param>
/// <param name="Events">Event types it produces.</param>
public sealed record SourceStatus(string Source, bool Connected, string? Reason, IReadOnlyList<string> Events);

/// <summary>The first line of every recording.</summary>
public sealed record RecordingHeader(
    string Format, string ModVersion, string GameVersion, string GameBranch, string StartedUtc,
    IReadOnlyList<SourceStatus> Sources)
{
    public const string CurrentFormat = "bingothespire-events/1";
}

/// <summary>
/// Writes a run's events as JSON lines: a <see cref="RecordingHeader"/> line, then one line per event.
/// These files are what detector tests replay.
/// </summary>
public sealed class EventRecorder : IDisposable
{
    private readonly TextWriter _writer;
    private bool _headerWritten;

    public EventRecorder(TextWriter writer)
    {
        _writer = writer;
    }

    public void WriteHeader(RecordingHeader header)
    {
        if (_headerWritten)
        {
            throw new InvalidOperationException("Header already written.");
        }
        _writer.WriteLine(BingoJson.Serialize(header));
        _writer.Flush();
        _headerWritten = true;
    }

    public void Record(GameEvent evt)
    {
        _writer.WriteLine(BingoJson.Serialize(evt));
        _writer.Flush(); // a crash mid-run should still leave a usable file
    }

    public void Dispose() => _writer.Dispose();

    /// <summary>Reads a recording back: the header (if present) and every event.</summary>
    public static (RecordingHeader? Header, List<GameEvent> Events) Read(TextReader reader)
    {
        RecordingHeader? header = null;
        var events = new List<GameEvent>();
        var first = true;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            if (first && line.Contains("\"format\"", StringComparison.Ordinal))
            {
                header = BingoJson.DeserializeHeader(line);
            }
            else if (BingoJson.DeserializeEvent(line) is { } evt)
            {
                events.Add(evt);
            }
            first = false;
        }
        return (header, events);
    }
}
