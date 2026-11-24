using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Core.Detection;

/// <summary>
/// Checks one goal on the board. Detectors are plain state machines over game events (spec
/// section 6.3). They track every player separately and never add values up across players.
/// </summary>
public interface IDetector
{
    void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink);
}

/// <summary>Where a detector reports. The tracker ignores reports for players who already completed the goal.</summary>
public interface IDetectionSink
{
    /// <param name="evidence">Short description of what did it, e.g. "53 Block at once".</param>
    void Complete(ulong playerId, string evidence);

    /// <summary>Progress toward the goal, for the board's "7/10" counters.</summary>
    void Progress(ulong playerId, int current, int target);
}

/// <param name="Square">Index of the square on the board.</param>
/// <param name="Moment">The shared moment of the event that completed it (see <see cref="GameEvent.Moment"/>).</param>
public sealed record GoalCompletion(int Square, string GoalId, ulong PlayerId, string Evidence, RunPosition? At, long Seq, string? Moment = null);

public sealed record GoalProgress(int Square, ulong PlayerId, int Current, int Target);
