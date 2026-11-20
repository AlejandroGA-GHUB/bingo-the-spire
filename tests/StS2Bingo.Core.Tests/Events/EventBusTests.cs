using StS2Bingo.Core.Events;

namespace StS2Bingo.Core.Tests.Events;

public class EventBusTests
{
    private long _now;

    private EventBus NewBus(Action<string, Exception>? onError = null) => new(() => _now, onError);

    [Fact]
    public void Publish_StampsSequenceAndTime()
    {
        var bus = NewBus();
        var seen = new List<GameEvent>();
        bus.Subscribe(seen.Add);

        _now = 100;
        bus.Publish(new CombatStarting());
        _now = 250;
        bus.Publish(new PlayerTurnStarted(7));

        Assert.Equal(new long[] { 1, 2 }, seen.Select(e => e.Seq));
        Assert.Equal(new long[] { 100, 250 }, seen.Select(e => e.Ms));
    }

    [Fact]
    public void Reset_RestartsSequenceAndClock()
    {
        var bus = NewBus();
        var seen = new List<GameEvent>();
        bus.Subscribe(seen.Add);

        bus.Publish(new CombatStarting());
        _now = 1000;
        bus.Reset();
        _now = 1005;
        bus.Publish(new CombatStarting());

        Assert.Equal(1, seen[1].Seq);
        Assert.Equal(5, seen[1].Ms);
    }

    [Fact]
    public void ThrowingSubscriber_IsReported_AndOthersStillReceive()
    {
        var errors = new List<string>();
        var bus = NewBus((name, _) => errors.Add(name));
        var seen = new List<GameEvent>();
        bus.Subscribe(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe(seen.Add);

        bus.Publish(new Shuffled(1));

        Assert.Single(seen);
        Assert.Equal(new[] { nameof(Shuffled) }, errors);
    }
}
