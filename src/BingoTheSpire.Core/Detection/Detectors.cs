using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Core.Detection;

/// <summary>An amount a player added toward a goal, or null if the event doesn't count.</summary>
public readonly record struct Contribution(ulong PlayerId, int Amount);

/// <summary>
/// Who a pooled goal counts for. In Team mode, goals that count or add up amounts pool every
/// player's share (spec section 4.3): progress shows on everyone's board, and the player whose
/// share reaches the target completes it.
/// </summary>
internal static class Pool
{
    /// <summary>Key the team's shared total is kept under. Never a real player id.</summary>
    public const ulong Team = ulong.MaxValue;

    /// <summary>Whose board shows progress kept under <paramref name="key"/>.</summary>
    public static IEnumerable<ulong> Members(DetectionContext context, ulong key, ulong? actor = null)
    {
        if (key != Team)
        {
            return [key];
        }
        return actor is { } a && !context.Players.Contains(a) ? context.Players.Append(a) : context.Players;
    }

    public static string Evidence(bool pooled, string evidence) => pooled ? evidence + " (team total)" : evidence;
}

/// <summary>
/// "Do X {n} times within a turn/combat/room/run". Counts per player (or for the team when
/// pooled) and starts again from 0 whenever the scope changes. Covers most goals.
/// </summary>
public sealed class CounterDetector(
    Scope scope, int target, Func<GameEvent, DetectionContext, IEnumerable<Contribution>> count, Func<int, string> evidence,
    bool pooled = false) : IDetector
{
    private readonly Dictionary<ulong, (long Key, int Count)> _counts = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        if (context.ScopeKey(scope) is not { } key)
        {
            return;
        }

        // Show counters that belong to an earlier turn/combat as 0 again.
        foreach (var (who, state) in _counts.ToList())
        {
            if (state.Key != key)
            {
                _counts[who] = (key, 0);
                foreach (var player in Pool.Members(context, who))
                {
                    sink.Progress(player, 0, target);
                }
            }
        }

        foreach (var c in count(evt, context))
        {
            if (c.Amount <= 0)
            {
                continue;
            }
            var who = pooled ? Pool.Team : c.PlayerId;
            var total = (_counts.TryGetValue(who, out var s) && s.Key == key ? s.Count : 0) + c.Amount;
            _counts[who] = (key, total);
            foreach (var player in Pool.Members(context, who, c.PlayerId))
            {
                sink.Progress(player, Math.Min(total, target), target);
            }
            if (total >= target)
            {
                sink.Complete(c.PlayerId, Pool.Evidence(pooled, evidence(total)));
            }
        }
    }
}

/// <summary>
/// "Reach {n} in one go": completes when a single measured value reaches the target. Progress
/// shows the best so far in the current fight.
/// </summary>
public sealed class ThresholdDetector(
    int target, Func<GameEvent, DetectionContext, Contribution?> measure, Func<int, GameEvent, string> evidence) : IDetector
{
    private readonly Dictionary<ulong, int> _best = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        if (evt is CombatStarting)
        {
            _best.Clear();
            return;
        }
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
            sink.Complete(m.PlayerId, evidence(m.Amount, evt));
        }
    }
}

/// <summary>
/// "Have {n}+ gold at once". Pooled, it's the team's gold added together. The game only reports
/// gold going up, so shop spending is taken off by hand; other losses are missed until the next gain.
/// </summary>
/// <summary>
/// "Have {n}+ gold at once". Progress shows the gold held right now (pooled: the team's total),
/// so it goes down when gold is spent.
/// </summary>
public sealed class GoldHeldDetector(int target, bool pooled = false) : IDetector
{
    private readonly Dictionary<ulong, int> _gold = new();

    public void Handle(GameEvent evt, DetectionContext context, IDetectionSink sink)
    {
        switch (evt)
        {
            case GoldGained g:
                _gold[g.PlayerId] = g.Gold;
                Check(g.PlayerId, context, sink);
                break;
            case ItemPurchased { Gold: > 0 } i:
                _gold[i.PlayerId] = Math.Max(0, _gold.GetValueOrDefault(i.PlayerId) - i.Gold);
                Check(i.PlayerId, context, sink);
                break;
        }
    }

    private void Check(ulong actor, DetectionContext context, IDetectionSink sink)
    {
        var who = pooled ? Pool.Team : actor;
        var held = pooled ? _gold.Values.Sum() : _gold[actor];
        foreach (var player in Pool.Members(context, who, actor))
        {
            sink.Progress(player, Math.Min(held, target), target);
        }
        if (held >= target)
        {
            sink.Complete(actor, Pool.Evidence(pooled, $"held {held} gold"));
        }
    }
}

/// <summary>
/// "Win {n} fights of these room types without losing HP". A player is clean in a combat until
/// their HP goes down; HP lost to their own cards counts too. Pooled (Team), every clean player
/// adds one to the team's count, so a fight where both players stay clean counts twice.
/// </summary>
public sealed class FlawlessWinsDetector(IReadOnlySet<string> roomTypes, int target, string what, bool pooled = false) : IDetector
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
                    var who = pooled ? Pool.Team : player;
                    var wins = _wins.GetValueOrDefault(who) + 1;
                    _wins[who] = wins;
                    foreach (var member in Pool.Members(context, who, player))
                    {
                        sink.Progress(member, Math.Min(wins, target), target);
                    }
                    if (wins >= target)
                    {
                        var evidence = target == 1 ? $"{what} without losing HP" : $"{wins} {what} fights without losing HP";
                        sink.Complete(player, Pool.Evidence(pooled && target > 1, evidence));
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
                        sink.Complete(played.PlayerId, $"{DisplayNames.Of(played.CardName, played.CardId, "a card")} hit {hit.Count} enemies");
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
/// count, so two players' debuffs are never added together, unless the goal is pooled (Team).
/// </summary>
public sealed class DebuffVarietyDetector(int target, bool pooled = false) : IDetector
{
    // (enemy, debuff) → who applied some of it (players, or the team). Removed when the debuff runs out.
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
                var who = pooled ? Pool.Team : player;
                if (!_appliers.TryGetValue(key, out var set))
                {
                    _appliers[key] = set = [];
                }
                set.Add(who);
                var count = _appliers.Count(kv => kv.Key.Enemy == enemy && kv.Value.Contains(who));
                foreach (var member in Pool.Members(context, who, player))
                {
                    sink.Progress(member, Math.Min(count, target), target);
                }
                if (count >= target)
                {
                    sink.Complete(player, Pool.Evidence(pooled, $"{count} debuffs on {DisplayNames.Of(power.OwnerName, power.OwnerId, "an enemy")}"));
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
/// two players stacking the same debuff never add up, unless the goal is pooled (Team).
/// </summary>
/// <param name="powerId">Only this debuff, or null for any.</param>
public sealed class DebuffStacksDetector(int target, string? powerId, bool pooled = false) : IDetector
{
    private readonly Dictionary<(uint Enemy, string Power, ulong Who), int> _share = new();

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

        // Stacks going down (decay, removal) shrink every share to at most what's left.
        foreach (var key in _share.Keys.Where(k => k.Enemy == enemy && k.Power == power.PowerId).ToList())
        {
            _share[key] = Math.Min(_share[key], Math.Max(power.Amount, 0));
        }
        if (power.Delta <= 0 || power.ApplierPlayerId is not { } player)
        {
            return;
        }

        var who = pooled ? Pool.Team : player;
        var mine = (enemy, power.PowerId, who);
        var share = Math.Min(_share.GetValueOrDefault(mine) + power.Delta, power.Amount);
        _share[mine] = share;
        foreach (var member in Pool.Members(context, who, player))
        {
            sink.Progress(member, Math.Min(share, target), target);
        }
        if (share >= target)
        {
            var debuff = DisplayNames.Of(power.PowerName, power.PowerId, "a debuff");
            var enemyName = DisplayNames.Of(power.OwnerName, power.OwnerId, "an enemy");
            sink.Complete(player, Pool.Evidence(pooled, $"{share} {debuff} on {enemyName}"));
        }
    }
}
