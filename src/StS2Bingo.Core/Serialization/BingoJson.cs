using System.Text.Json;
using System.Text.Json.Serialization;
using StS2Bingo.Core.Events;
using StS2Bingo.Core.Recording;

namespace StS2Bingo.Core.Serialization;

/// <summary>
/// Source-generated JSON for everything the mod writes to disk. No runtime reflection, so it
/// behaves the same inside the game as in tests.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GameEvent))]
[JsonSerializable(typeof(RecordingHeader))]
internal sealed partial class BingoJsonContext : JsonSerializerContext;

public static class BingoJson
{
    public static string Serialize(GameEvent evt) => JsonSerializer.Serialize(evt, BingoJsonContext.Default.GameEvent);

    public static GameEvent? DeserializeEvent(string json) => JsonSerializer.Deserialize(json, BingoJsonContext.Default.GameEvent);

    public static string Serialize(RecordingHeader header) => JsonSerializer.Serialize(header, BingoJsonContext.Default.RecordingHeader);

    public static RecordingHeader? DeserializeHeader(string json) => JsonSerializer.Deserialize(json, BingoJsonContext.Default.RecordingHeader);
}
