using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Core.Detection;

/// <summary>
/// Every detector a goal can name, with the events it needs. A goal whose detector needs an event
/// this game version can't provide is left off the board (spec section 9).
/// General detectors never check content ids, so modded cards, powers and enemies count
/// (spec section 6.4). The few that name a vanilla id belong to character-specific goals.
/// </summary>
public static class DetectorCatalog
{
    private sealed record Entry(string[] Events, Func<IReadOnlyDictionary<string, int>, IDetector> Create);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        // ---- Per turn -----------------------------------------------------------------------
        ["CardsPlayedInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed c ? Mine(c.PlayerId) : None, n => $"{n} cards in one turn"),
        ["AttacksPlayedInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed { CardType: "Attack" } c ? Mine(c.PlayerId) : None, n => $"{n} Attacks in one turn"),
        ["SkillsPlayedInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed { CardType: "Skill" } c ? Mine(c.PlayerId) : None, n => $"{n} Skills in one turn"),
        ["BlockInTurn"] = Counter(Scope.Turn, [nameof(BlockGained)],
            (e, _) => e is BlockGained { CreatureKind: CreatureKind.Player, PlayerId: { } p } b ? Mine(p, b.Amount) : None,
            n => $"{n} Block in one turn"),
        ["DamageInTurn"] = Counter(Scope.Turn, [nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { } d ? Mine(d.DealerPlayerId!.Value, Total(d)) : None, n => $"{n} damage in one turn"),
        ["KillsInTurn"] = Counter(Scope.Turn, [nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { TargetDied: true } d ? Mine(d.DealerPlayerId!.Value) : None, n => $"{n} kills in one turn"),
        ["CardsDrawnInTurn"] = Counter(Scope.Turn, [nameof(CardDrawn)],
            (e, _) => e is CardDrawn c ? Mine(c.PlayerId) : None, n => $"drew {n} cards in one turn"),
        ["EnergyInTurn"] = Counter(Scope.Turn, [nameof(EnergySpent)],
            (e, _) => e is EnergySpent s ? Mine(s.PlayerId, s.Amount) : None, n => $"spent {n} energy in one turn"),

        // ---- Per combat ---------------------------------------------------------------------
        ["PowersPlayedInCombat"] = Counter(Scope.Combat, [nameof(CardPlayed), nameof(CombatStarting)],
            (e, _) => e is CardPlayed { CardType: "Power" } c ? Mine(c.PlayerId) : None, n => $"{n} Powers in one fight"),
        ["CardsExhaustedInCombat"] = Counter(Scope.Combat, [nameof(CardExhausted), nameof(CombatStarting)],
            (e, _) => e is CardExhausted c ? Mine(c.PlayerId) : None, n => $"exhausted {n} cards in one fight"),
        ["ShufflesInCombat"] = Counter(Scope.Combat, [nameof(Shuffled), nameof(CombatStarting)],
            (e, _) => e is Shuffled s ? Mine(s.PlayerId) : None, n => $"shuffled {n} times in one fight"),
        ["CardsGeneratedInCombat"] = Counter(Scope.Combat, [nameof(CardGenerated), nameof(CombatStarting)],
            (e, _) => e is CardGenerated { PlayerId: { } p } ? Mine(p) : None, n => $"created {n} cards in one fight"),
        ["PotionsInCombat"] = Counter(Scope.Combat, [nameof(PotionUsed), nameof(CombatStarting)],
            (e, _) => e is PotionUsed p ? Mine(p.PlayerId) : None, n => $"{n} potions in one fight"),
        ["OrbsInCombat"] = Counter(Scope.Combat, [nameof(OrbChanneled), nameof(CombatStarting)],
            (e, _) => e is OrbChanneled o ? Mine(o.PlayerId) : None, n => $"channeled {n} orbs in one fight"),
        ["PetDamageInCombat"] = Counter(Scope.Combat, [nameof(DamageDealt), nameof(CombatStarting)],
            (e, _) => PlayerHitOnEnemy(e) is { DealerKind: CreatureKind.Pet } d ? Mine(d.DealerPlayerId!.Value, Total(d)) : None,
            n => $"pet dealt {n} damage in one fight"),

        // ---- Per room / run -----------------------------------------------------------------
        ["ShopItemsInVisit"] = Counter(Scope.Room, [nameof(ItemPurchased), nameof(RoomEntered)],
            (e, _) => e is ItemPurchased i ? Mine(i.PlayerId) : None, n => $"bought {n} items in one shop visit"),
        ["RelicsBought"] = Counter(Scope.Run, [nameof(ItemPurchased)],
            (e, _) => e is ItemPurchased i && i.ItemKind.Contains("Relic", StringComparison.Ordinal) ? Mine(i.PlayerId) : None,
            n => $"bought {n} relics"),
        ["CardsRemovedAtShop"] = Counter(Scope.Run, [nameof(ItemPurchased)],
            (e, _) => e is ItemPurchased i && i.ItemKind.Contains("Removal", StringComparison.Ordinal) ? Mine(i.PlayerId) : None,
            n => $"removed {n} cards at shops"),
        ["PotionsUsed"] = Counter(Scope.Run, [nameof(PotionUsed)],
            (e, _) => e is PotionUsed p ? Mine(p.PlayerId) : None, n => $"used {n} potions"),
        ["ElitesDefeated"] = Counter(Scope.Run, [nameof(CombatWon)],
            (e, ctx) => e is CombatWon { RoomType: "Elite" } ? Everyone(ctx) : None, n => $"defeated {n} elites"),
        ["BossesDefeated"] = Counter(Scope.Run, [nameof(CombatWon)],
            (e, ctx) => e is CombatWon { RoomType: "Boss" } ? Everyone(ctx) : None, n => $"defeated {n} bosses"),
        ["RestSmiths"] = Counter(Scope.Run, [nameof(RestSmithed)],
            (e, _) => e is RestSmithed r ? Mine(r.PlayerId) : None, n => $"upgraded at {n} rest sites"),
        ["RestHeals"] = Counter(Scope.Run, [nameof(RestHealed)],
            (e, _) => e is RestHealed r ? Mine(r.PlayerId) : None, n => $"rested at {n} rest sites"),
        ["ExactKills"] = Counter(Scope.Run, [nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { TargetDied: true, Overkill: 0 } d ? Mine(d.DealerPlayerId!.Value) : None,
            n => $"{n} exactly lethal kills"),

        // ---- Single values ------------------------------------------------------------------
        ["BiggestHit"] = Threshold([nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { } d ? One(d.DealerPlayerId!.Value, Total(d)) : null, n => $"a {n}-damage hit"),
        ["Overkill"] = Threshold([nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { TargetDied: true } d ? One(d.DealerPlayerId!.Value, d.Overkill) : null,
            n => $"overkilled by {n}"),
        ["CardCost"] = Threshold([nameof(CardPlayed)],
            (e, _) => e is CardPlayed c ? One(c.PlayerId, c.EnergySpent) : null, n => $"played a {n}-energy card"),
        ["GoldHeld"] = Threshold([nameof(GoldGained)],
            (e, _) => e is GoldGained g ? One(g.PlayerId, g.Gold) : null, n => $"held {n} gold"),
        ["SurviveHit"] = Threshold([nameof(DamageDealt)],
            (e, _) => e is DamageDealt { TargetKind: CreatureKind.Player, TargetPlayerId: { } p, TargetDied: false } d ? One(p, d.Unblocked) : null,
            n => $"survived a hit for {n} HP"),
        ["BuffStacks"] = Threshold([nameof(PowerChanged)],
            (e, _) => e is PowerChanged { OwnerKind: CreatureKind.Player, OwnerPlayerId: { } p, PowerType: "Buff" } pc ? One(p, pc.Amount) : null,
            n => $"{n} stacks of a buff"),
        ["StrengthStacks"] = Threshold([nameof(PowerChanged)],
            (e, _) => e is PowerChanged { OwnerKind: CreatureKind.Player, OwnerPlayerId: { } p, PowerId: "STRENGTH_POWER" } pc ? One(p, pc.Amount) : null,
            n => $"{n} Strength"),
        ["StarsInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed c ? Mine(c.PlayerId, c.StarsSpent) : None, n => $"spent {n} stars in one turn"),

        // ---- Their own logic ----------------------------------------------------------------
        ["FlawlessElite"] = new([nameof(HpChanged), nameof(CombatWon), nameof(CombatStarting)],
            _ => new FlawlessWinsDetector(new HashSet<string> { "Elite" }, 1, "an elite")),
        ["FlawlessBoss"] = new([nameof(HpChanged), nameof(CombatWon), nameof(CombatStarting)],
            _ => new FlawlessWinsDetector(new HashSet<string> { "Boss" }, 1, "a boss")),
        ["FlawlessHallways"] = new([nameof(HpChanged), nameof(CombatWon), nameof(CombatStarting)],
            p => new FlawlessWinsDetector(new HashSet<string> { "Monster" }, N(p), "normal")),
        ["QuickWin"] = new([nameof(CombatWon), nameof(CombatStarting)], p => new QuickWinDetector(N(p))),
        ["LowHpWin"] = new([nameof(HpChanged), nameof(CombatWon)], p => new LowHpWinDetector(N(p))),
        ["CardHits"] = new([nameof(DamageDealt), nameof(CardPlayed)], p => new CardHitsDetector(N(p))),
        ["DebuffVariety"] = new([nameof(PowerChanged), nameof(CombatStarting)], p => new DebuffVarietyDetector(N(p))),
        ["BuffVariety"] = new([nameof(PowerChanged), nameof(CombatStarting)], p => new BuffVarietyDetector(N(p))),
        ["DebuffStacks"] = new([nameof(PowerChanged), nameof(CombatStarting)], p => new DebuffStacksDetector(N(p), null, "stacks of a debuff")),
        ["PoisonStacks"] = new([nameof(PowerChanged), nameof(CombatStarting)], p => new DebuffStacksDetector(N(p), "POISON_POWER", "Poison")),
    };

    public static IReadOnlyCollection<string> Names => Entries.Keys;

    public static IReadOnlyList<string> RequiredEvents(string detector) => Entries[detector].Events;

    /// <summary>Detectors whose events are all available. Pass the result as <c>DetectionCapabilities.Detectors</c>.</summary>
    public static IReadOnlySet<string> Available(IReadOnlySet<string>? availableEvents) =>
        Entries.Where(kv => availableEvents == null || kv.Value.Events.All(availableEvents.Contains))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

    public static IDetector Create(string detector, IReadOnlyDictionary<string, int> parameters) =>
        Entries.TryGetValue(detector, out var entry)
            ? entry.Create(parameters)
            : throw new KeyNotFoundException($"unknown detector '{detector}'");

    // ---- Helpers ----

    private static IEnumerable<Contribution> None => [];

    private static int N(IReadOnlyDictionary<string, int> p) =>
        p.TryGetValue("n", out var n) ? n : throw new KeyNotFoundException("goal tier has no value for 'n'");

    private static Contribution One(ulong player, int amount) => new(player, amount);

    private static IEnumerable<Contribution> Mine(ulong player, int amount = 1) => [new(player, amount)];

    private static IEnumerable<Contribution> Everyone(DetectionContext ctx) => ctx.Players.Select(p => new Contribution(p, 1));

    /// <summary>Damage dealt to an enemy by a player or their pet.</summary>
    private static DamageDealt? PlayerHitOnEnemy(GameEvent e) =>
        e is DamageDealt { DealerPlayerId: not null, DealerKind: CreatureKind.Player or CreatureKind.Pet, TargetKind: CreatureKind.Enemy } d ? d : null;

    /// <summary>The full hit as shown in game: HP lost, Block removed, and overkill.</summary>
    private static int Total(DamageDealt d) => d.Unblocked + d.Blocked + d.Overkill;

    private static Entry Counter(Scope scope, string[] events, Func<GameEvent, DetectionContext, IEnumerable<Contribution>> count, Func<int, string> evidence) =>
        new(events, p => new CounterDetector(scope, N(p), count, evidence));

    private static Entry Threshold(string[] events, Func<GameEvent, DetectionContext, Contribution?> measure, Func<int, string> evidence) =>
        new(events, p => new ThresholdDetector(N(p), measure, evidence));
}
