using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Core.Detection;

/// <summary>An amount a player added toward a goal, or null if the event doesn't count.</summary>
public readonly record struct Contribution(ulong PlayerId, int Amount);

/// <summary>
/// "Do X {n} times within a turn/combat/room/run". Counts per player and starts again from 0
/// whenever the scope changes. Covers most goals.
/// </summary>
public sealed class CounterDetector(
    Scope scope, int target, Func<GameEvent, DetectionContext, IEnumerable<Contribution>> count, Func<int, string> evidence) : IDetector
{
    private readonly Dictionary<ulong, (long Key, int Count)> _counts = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        if (context.ScopeKey(scope) is not { } key)
        {
            return;
        }
        // Show counters that belong to an earlier turn/combat as 0 again.
        foreach (var (player, state) in _counts.ToList())
        {
            if (state.Key != key)
            {
                _counts[player] = (key, 0);
                sink.Progress(player, 0, target);
            }
        }
        foreach (var c in count(evt, context))
        {
            if (c.Amount <= 0)
            {
                continue;
            }
            var total = (_counts.TryGetValue(c.PlayerId, out var s) && s.Key == key ? s.Count : 0) + c.Amount;
            _counts[c.PlayerId] = (key, total);
            sink.Progress(c.PlayerId, Math.Min(total, target), target);
            if (total >= target)
            {
                sink.Complete(c.PlayerId, evidence(total));
            }
        }
    }
}

/// <summary>"Reach {n} in one go": completes when a single measured value reaches the target. Progress shows the best so far.</summary>
public sealed class ThresholdDetector(
    int target, Func<GameEvent, DetectionContext, Contribution?> measure, Func<int, string> evidence) : IDetector
{
    private readonly Dictionary<ulong, int> _best = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        if (measure(evt, context) is not { } m)
        {
            return;
        }
        if (!_best.TryGetValue(m.PlayerId, out var best) || m.Amount > best)
        {
            _best[m.PlayerId] = m.Amount;
            sink.Progress(m.PlayerId, Math.Min(m.Amount, target), target);
        }
        if (m.Amount >= target)
        {
            sink.Complete(m.PlayerId, evidence(m.Amount));
        }
    }
}

/// <summary>
/// "Win {n} fights of these room types without losing HP". A player is clean in a combat until
/// their HP goes down; HP lost to their own cards counts too.
/// </summary>
public sealed class FlawlessWinsDetector(IReadOnlySet<string> roomTypes, int target, string what) : IDetector
{
    private readonly HashSet<ulong> _hurt = [];
    private readonly Dictionary<ulong, int> _wins = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        switch (evt)
        {
            case CombatStarting:
                _hurt.Clear();
                break;
            case HpChanged { Delta: < 0 } hp when context.InCombat:
                _hurt.Add(hp.PlayerId);
                break;
            case CombatWon won when roomTypes.Contains(won.RoomType):
                foreach (var player in context.Players.Where(p => !_hurt.Contains(p)))
                {
                    var wins = _wins.GetValueOrDefault(player) + 1;
                    _wins[player] = wins;
                    sink.Progress(player, Math.Min(wins, target), target);
                    if (wins >= target)
                    {
                        sink.Complete(player, target == 1 ? $"{what} without losing HP" : $"{wins} {what} fights without losing HP");
                    }
                }
                break;
        }
    }
}

/// <summary>"Win a fight by round {n}". Every player in the fight gets it.</summary>
public sealed class QuickWinDetector(int maxRound) : IDetector
{
    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        if (evt is CombatWon && context.LastCombatRound is > 0 and var round && round <= maxRound)
        {
            foreach (var player in context.Players)
            {
                sink.Complete(player, $"won a fight on round {round}");
            }
        }
    }
}

/// <summary>"Win a fight with {n} HP or less" (and still alive).</summary>
public sealed class LowHpWinDetector(int maxHp) : IDetector
{
    private readonly Dictionary<ulong, int> _hp = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        switch (evt)
        {
            case HpChanged hp:
                _hp[hp.PlayerId] = hp.CurrentHp;
                break;
            case CombatWon:
                foreach (var (player, hp) in _hp)
                {
                    if (hp > 0 && hp <= maxHp)
                    {
                        sink.Complete(player, $"won a fight on {hp} HP");
                    }
                }
                break;
        }
    }
}

/// <summary>
/// "Hit {n} different enemies with a single card". A card's damage arrives before the card's own
/// play event, so targets are gathered until that player's next <see cref="CardPlayed"/>.
/// </summary>
public sealed class CardHitsDetector(int target) : IDetector
{
    private readonly Dictionary<ulong, HashSet<uint>> _targets = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        switch (evt)
        {
            case DamageDealt { DealerPlayerId: { } player, DealerKind: CreatureKind.Player, TargetKind: CreatureKind.Enemy, TargetCombatId: { } enemy, CardId: not null }:
                if (!_targets.TryGetValue(player, out var set))
                {
                    _targets[player] = set = [];
                }
                set.Add(enemy);
                break;
            case CardPlayed played:
                if (_targets.Remove(played.PlayerId, out var hit))
                {
                    sink.Progress(played.PlayerId, Math.Min(hit.Count, target), target);
                    if (hit.Count >= target)
                    {
                        sink.Complete(played.PlayerId, $"{played.CardId} hit {hit.Count} enemies");
                    }
                }
                break;
            case CombatStarting:
                _targets.Clear();
                break;
        }
    }
}

/// <summary>
/// "Have {n} different debuffs from you on one enemy at once". Only debuffs the player applied
/// count, so two players' debuffs are never added together.
/// </summary>
public sealed class DebuffVarietyDetector(int target) : IDetector
{
    // (enemy, debuff) → players who applied some of it. Removed when the debuff runs out.
    private readonly Dictionary<(uint Enemy, string Power), HashSet<ulong>> _appliers = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        switch (evt)
        {
            case CombatStarting:
                _appliers.Clear();
                break;
            case PowerChanged { OwnerKind: CreatureKind.Enemy, OwnerCombatId: { } enemy, PowerType: "Debuff" } power:
                var key = (enemy, power.PowerId);
                if (power.Amount <= 0)
                {
                    _appliers.Remove(key);
                    return;
                }
                if (power.Delta <= 0 || power.ApplierPlayerId is not { } player)
                {
                    return;
                }
                if (!_appliers.TryGetValue(key, out var set))
                {
                    _appliers[key] = set = [];
                }
                set.Add(player);
                var count = _appliers.Count(kv => kv.Key.Enemy == enemy && kv.Value.Contains(player));
                sink.Progress(player, Math.Min(count, target), target);
                if (count >= target)
                {
                    sink.Complete(player, $"{count} debuffs on {power.OwnerId ?? "an enemy"}");
                }
                break;
        }
    }
}

/// <summary>"Have {n} different buffs at once". Counted within a combat.</summary>
public sealed class BuffVarietyDetector(int target) : IDetector
{
    private readonly Dictionary<ulong, HashSet<string>> _buffs = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        switch (evt)
        {
            case CombatStarting:
                _buffs.Clear();
                break;
            case PowerChanged { OwnerKind: CreatureKind.Player, OwnerPlayerId: { } player, PowerType: "Buff" } power:
                if (!_buffs.TryGetValue(player, out var set))
                {
                    _buffs[player] = set = [];
                }
                if (power.Amount <= 0)
                {
                    set.Remove(power.PowerId);
                    return;
                }
                set.Add(power.PowerId);
                sink.Progress(player, Math.Min(set.Count, target), target);
                if (set.Count >= target)
                {
                    sink.Complete(player, $"{set.Count} buffs at once");
                }
                break;
        }
    }
}

/// <summary>
/// "Put {n}+ stacks of a debuff on one enemy". Tracks each player's own share of the stacks, so
/// two players stacking the same debuff never add up.
/// </summary>
public sealed class DebuffStacksDetector(int target, string? powerId, string what) : IDetector
{
    private readonly Dictionary<(uint Enemy, string Power, ulong Player), int> _share = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        if (evt is CombatStarting)
        {
            _share.Clear();
            return;
        }
        if (evt is not PowerChanged { OwnerKind: CreatureKind.Enemy, OwnerCombatId: { } enemy, PowerType: "Debuff" } power
            || (powerId != null && power.PowerId != powerId))
        {
            return;
        }
        // Stacks going down (decay, removal) shrink every player's share to at most what's left.
        foreach (var key in _share.Keys.Where(k => k.Enemy == enemy && k.Power == power.PowerId).ToList())
        {
            _share[key] = Math.Min(_share[key], Math.Max(power.Amount, 0));
        }
        if (power.Delta <= 0 || power.ApplierPlayerId is not { } player)
        {
            return;
        }
        var mine = (enemy, power.PowerId, player);
        var share = Math.Min(_share.GetValueOrDefault(mine) + power.Delta, power.Amount);
        _share[mine] = share;
        sink.Progress(player, Math.Min(share, target), target);
        if (share >= target)
        {
            sink.Complete(player, $"{share} {what} on {power.OwnerId ?? "an enemy"}");
        }
    }
}
