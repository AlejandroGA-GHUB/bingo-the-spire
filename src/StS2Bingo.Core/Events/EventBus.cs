namespace StS2Bingo.Core.Events;

/// <summary>
/// Delivers <see cref="GameEvent"/>s to subscribers in the order they were published, and stamps
/// each one with a sequence number and a time. A failing subscriber is reported and skipped,
/// so one broken detector can't stop the others or reach the game.
/// </summary>
public sealed class EventBus
{
    private readonly List<Action<GameEvent>> _subscribers = new();
    private readonly Func<long> _clockMs;
    private readonly Action<string, Exception>? _onSubscriberError;
    private long _nextSeq = 1;
    private long _startMs;

    /// <param name="clockMs">Monotonic milliseconds (e.g. a Stopwatch).</param>
    /// <param name="onSubscriberError">Called when a subscriber throws.</param>
    public EventBus(Func<long> clockMs, Action<string, Exception>? onSubscriberError = null)
    {
        _clockMs = clockMs;
        _onSubscriberError = onSubscriberError;
        _startMs = clockMs();
    }

    public void Subscribe(Action<GameEvent> subscriber) => _subscribers.Add(subscriber);

    public void Unsubscribe(Action<GameEvent> subscriber) => _subscribers.Remove(subscriber);

    /// <summary>Restarts numbering and the clock, e.g. when a new run is attached.</summary>
    public void Reset()
    {
        _nextSeq = 1;
        _startMs = _clockMs();
    }

    /// <returns>The event as delivered, with <see cref="GameEvent.Seq"/> and <see cref="GameEvent.Ms"/> set.</returns>
    public GameEvent Publish(GameEvent evt)
    {
        var stamped = evt with { Seq = _nextSeq++, Ms = _clockMs() - _startMs };
        foreach (var subscriber in _subscribers.ToArray())
        {
            try
            {
                subscriber(stamped);
            }
            catch (Exception ex)
            {
                _onSubscriberError?.Invoke(stamped.GetType().Name, ex);
            }
        }
        return stamped;
    }
}
