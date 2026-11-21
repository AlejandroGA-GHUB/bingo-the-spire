using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Core.Detection;

/// <summary>How long a counter keeps counting before it starts again from 0.</summary>
public enum Scope
{
    /// <summary>One combat round: the player's turn and the enemy turn after it.</summary>
    Turn,
    Combat,
    /// <summary>One map room, combat or not.</summary>
    Room,
    Run,
}

/// <summary>
/// Where the run is, as seen from the event stream. The tracker updates it; detectors read it.
/// Start events (room, combat) apply before detectors see them and end events after, so a
/// detector handling <see cref="CombatEnded"/> still sees the combat as running.
/// </summary>
public sealed class DetectionContext
{
    private readonly List<ulong> _players = [];
    private readonly Dictionary<(uint Enemy, string Power), ulong> _lastApplier = new();
    private readonly Dictionary<(uint Enemy, string Power), int> _stacks = new();

    /// <summary>Every player in the run, from the last <see cref="RunAttached"/>.</summary>
    public IReadOnlyList<ulong> Players => _players;

    /// <summary>Increases by one at every <see cref="CombatStarting"/>. 0 before the first combat.</summary>
    public int CombatIndex { get; private set; }

    public bool InCombat { get; private set; }

    /// <summary>Increases by one at every <see cref="RoomEntered"/>.</summary>
    public int RoomIndex { get; private set; }

    public string? RoomType { get; private set; }

    /// <summary>Round of the current event, while a combat is running.</summary>
    public int? Round { get; private set; }

    /// <summary>
    /// The last round seen in the current or most recent combat. Kept after the combat ends,
    /// because the game reports the victory after the combat is over.
    /// </summary>
    public int LastCombatRound { get; private set; }

    public RunPosition? At { get; private set; }

    /// <summary>
    /// The player who last added this power to this enemy in the current combat, e.g. to credit a
    /// Poison or Doom kill. Kept after the power runs out.
    /// </summary>
    public ulong? LastApplier(uint enemy, string powerId) =>
        _lastApplier.TryGetValue((enemy, powerId), out var player) ? player : null;

    /// <summary>Stacks of a power on an enemy right now, in the current combat.</summary>
    public int Stacks(uint enemy, string powerId) => _stacks.GetValueOrDefault((enemy, powerId));

    /// <summary>Key that changes whenever a counter with this scope should restart, or null if the scope isn't active.</summary>
    public long? ScopeKey(Scope scope) => scope switch
    {
        Scope.Turn => InCombat && Round is { } round ? ((long)CombatIndex << 20) | (uint)round : null,
        Scope.Combat => InCombat ? CombatIndex : null,
        Scope.Room => RoomIndex,
        _ => 0,
    };

    internal void Before(GameEvent evt)
    {
        At = evt.At;
        switch (evt)
        {
            case RunAttached run:
                _players.Clear();
                _players.AddRange(run.Players.Select(p => p.PlayerId));
                InCombat = false;
                break;
            case RoomEntered room:
                RoomIndex++;
                RoomType = room.RoomType;
                InCombat = false;
                break;
            case CombatStarting:
                CombatIndex++;
                InCombat = true;
                LastCombatRound = 0;
                _lastApplier.Clear();
                _stacks.Clear();
                break;
            case PowerChanged { OwnerKind: CreatureKind.Enemy, OwnerCombatId: { } enemy } power:
                _stacks[(enemy, power.PowerId)] = Math.Max(power.Amount, 0);
                if (power.Delta > 0 && power.ApplierPlayerId is { } applier)
                {
                    _lastApplier[(enemy, power.PowerId)] = applier;
                }
                break;
        }
        Round = InCombat ? evt.At?.Round : null;
        if (Round is { } r)
        {
            LastCombatRound = r;
        }
    }

    internal void After(GameEvent evt)
    {
        if (evt is CombatEnded or RunEnded)
        {
            InCombat = false;
        }
    }
}
