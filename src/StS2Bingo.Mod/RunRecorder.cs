using System.Globalization;
using System.Text;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using StS2Bingo.Core.Events;
using StS2Bingo.Core.Recording;
using StS2Bingo.Mod.GameAdapter;

namespace StS2Bingo.Mod;

/// <summary>
/// Writes every event of a run to its own JSON-lines file under <see cref="DataPaths.Recordings"/>.
/// A new file starts each time a run is attached (new run or Continue).
/// </summary>
internal sealed class RunRecorder
{
    private EventRecorder? _current;
    private bool _disabled;

    public void OnEvent(GameEvent evt)
    {
        if (_disabled)
        {
            return;
        }
        try
        {
            if (evt is RunAttached attached)
            {
                StartFile(attached);
            }
            _current?.Record(evt);
        }
        catch (Exception ex)
        {
            _disabled = true;
            _current = null;
            BingoLog.Error($"event recording failed and is off for this session: {ex}");
        }
    }

    private void StartFile(RunAttached run)
    {
        _current?.Dispose();
        Directory.CreateDirectory(DataPaths.Recordings);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(DataPaths.Recordings, $"{stamp}_{SafeName(run.Seed)}.jsonl");

        var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _current = new EventRecorder(writer);
        _current.WriteHeader(new RecordingHeader(
            Format: RecordingHeader.CurrentFormat,
            ModVersion: ModEntry.Version,
            GameVersion: NGame.GetGameVersion(),
            GameBranch: PlatformUtil.GetPlatformBranch().ToString(),
            StartedUtc: DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Sources: Adapter.Sources));
        BingoLog.Info($"recording run to {path}");
    }

    private static string SafeName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "noseed" : cleaned;
    }
}
