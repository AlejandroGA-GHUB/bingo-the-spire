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
    /// <param name="Create">Builds the detector from the tier's values; the flag says whether to pool (Team).</param>
    /// <param name="Lasting">Builds up over the whole run, so its progress stays on the board between fights.</param>
    /// <param name="Pools">In Team mode, every player's share adds up (and the target scales with the team size).</param>
    /// <param name="Shared">Every player completes it at the same moment (e.g. winning a fight), so it's left off Lockout boards.</param>
    private sealed record Entry(
        string[] Events, Func<IReadOnlyDictionary<string, int>, bool, IDetector> Create, bool Lasting = false, bool Pools = false,
        bool Shared = false);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        // ---- Per turn -----------------------------------------------------------------------
        ["CardsPlayedInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed c ? Mine(c.PlayerId) : None, n => $"{n} cards in one turn"),
        ["AttacksPlayedInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed { CardType: "Attack" } c ? Mine(c.PlayerId) : None, n => $"{n} Attacks in one turn"),
        ["SkillsPlayedInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed { CardType: "Skill" } c ? Mine(c.PlayerId) : None, n => $"{n} Skills in one turn"),
        ["DamageInTurn"] = Counter(Scope.Turn, [nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { } d ? Mine(d.DealerPlayerId!.Value, Total(d)) : None, n => $"{n} damage in one turn"),
        // Over the whole run: every enemy killed on the first turn of its fight counts once.
        ["FirstTurnKills"] = Counter(Scope.Run, [nameof(DamageDealt)],
            (e, ctx) => ctx.Round == 1 && KillBy(e, ctx) is { } k ? Mine(k.Player) : None, n => $"{n} enemies killed on turn 1"),
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
            n => $"removed {n} card{(n == 1 ? "" : "s")} at shops"),
        ["PotionsUsed"] = Counter(Scope.Run, [nameof(PotionUsed)],
            (e, _) => e is PotionUsed p ? Mine(p.PlayerId) : None, n => $"used {n} potions"),
        // Everyone in the fight gets these at once, so they never pool and stay off Lockout boards.
        ["ElitesDefeated"] = Counter(Scope.Run, [nameof(CombatWon)],
            (e, ctx) => e is CombatWon { RoomType: "Elite" } ? Everyone(ctx) : None, n => $"defeated {n} elites", pools: false, shared: true),
        ["BossesDefeated"] = Counter(Scope.Run, [nameof(CombatWon)],
            (e, ctx) => e is CombatWon { RoomType: "Boss" } ? Everyone(ctx) : None, n => $"defeated {n} bosses", pools: false, shared: true),
        ["RestSmiths"] = Counter(Scope.Run, [nameof(RestSmithed)],
            (e, _) => e is RestSmithed r ? Mine(r.PlayerId) : None, n => $"upgraded at {n} rest sites"),
        ["RestHeals"] = Counter(Scope.Run, [nameof(RestHealed)],
            (e, _) => e is RestHealed r ? Mine(r.PlayerId) : None, n => $"rested at {n} rest sites"),
        // Near-exact kills: up to 2 damage past the enemy's last HP (Poison and Doom included).
        ["ExactKills"] = Counter(Scope.Run, [nameof(DamageDealt)],
            (e, ctx) => KillBy(e, ctx) is { Overkill: <= 2 } k ? Mine(k.Player) : None,
            n => $"{n} kills with 2 or less overkill"),

        // ---- Single values ------------------------------------------------------------------
        ["BiggestHit"] = Threshold([nameof(DamageDealt)],
            (e, _) => PlayerHitOnEnemy(e) is { } d ? One(d.DealerPlayerId!.Value, Total(d)) : null, n => $"a {n}-damage hit"),
        ["Overkill"] = Threshold([nameof(DamageDealt)],
            (e, ctx) => KillBy(e, ctx) is { } k ? One(k.Player, k.Overkill) : null,
            n => $"overkilled by {n}"),
        // Energy actually paid: X-cost cards count what was spent, cheapened cards count less.
        ["ExpensiveCardsInCombat"] = Counter(Scope.Combat, [nameof(CardPlayed), nameof(CombatStarting)],
            (e, ctx) => e is CardPlayed { EnergySpent: >= 3 } c ? Mine(c.PlayerId) : None, n => $"{n} cards of 3+ energy in one fight"),
        ["CardCost"] = Threshold([nameof(CardPlayed)],
            (e, _) => e is CardPlayed c ? One(c.PlayerId, c.EnergySpent) : null, n => $"played a {n}-energy card"),
        ["GoldHeld"] = new([nameof(GoldGained), nameof(ItemPurchased)], (p, pooled) => new GoldHeldDetector(N(p), pooled), Lasting: true, Pools: true),
        ["SurviveHit"] = Threshold([nameof(DamageDealt)],
            (e, _) => e is DamageDealt { TargetKind: CreatureKind.Player, TargetPlayerId: { } p, TargetDied: false } d ? One(p, d.Unblocked) : null,
            n => $"survived a hit for {n} HP"),
        ["BuffStacks"] = Threshold([nameof(PowerChanged)],
            (e, _) => e is PowerChanged { OwnerKind: CreatureKind.Player, OwnerPlayerId: { } p, PowerType: "Buff" } pc ? One(p, pc.Amount) : null,
            (n, e) => $"{n} {DisplayNames.Of((e as PowerChanged)?.PowerName, (e as PowerChanged)?.PowerId, "stacks of a buff")}"),
        ["StrengthStacks"] = Threshold([nameof(PowerChanged)],
            (e, _) => e is PowerChanged { OwnerKind: CreatureKind.Player, OwnerPlayerId: { } p, PowerId: "STRENGTH_POWER" } pc ? One(p, pc.Amount) : null,
            n => $"{n} Strength"),
        ["StarsInTurn"] = Counter(Scope.Turn, [nameof(CardPlayed)],
            (e, _) => e is CardPlayed c ? Mine(c.PlayerId, c.StarsSpent) : None, n => $"spent {n} stars in one turn"),

        // ---- Their own logic ----------------------------------------------------------------
        ["FlawlessElite"] = new([nameof(HpChanged), nameof(CombatWon), nameof(CombatStarting)],
            (_, pooled) => new FlawlessWinsDetector(new HashSet<string> { "Elite" }, 1, "an elite", pooled), Pools: true),
        ["FlawlessBoss"] = new([nameof(HpChanged), nameof(CombatWon), nameof(CombatStarting)],
            (_, pooled) => new FlawlessWinsDetector(new HashSet<string> { "Boss" }, 1, "a boss", pooled), Pools: true),
        ["FlawlessHallways"] = new([nameof(HpChanged), nameof(CombatWon), nameof(CombatStarting)],
            (p, pooled) => new FlawlessWinsDetector(new HashSet<string> { "Monster" }, N(p), "normal", pooled), Lasting: true, Pools: true),
        ["QuickWin"] = new([nameof(CombatWon), nameof(CombatStarting)], (p, _) => new QuickWinDetector(N(p)), Shared: true),
        ["LowHpWin"] = new([nameof(HpChanged), nameof(CombatWon)], (p, _) => new LowHpWinDetector(N(p))),
        ["CardHits"] = new([nameof(DamageDealt), nameof(CardPlayed)], (p, _) => new CardHitsDetector(N(p))),
        ["BlockHeld"] = new([nameof(BlockGained), nameof(CombatStarting)], (p, pooled) => new BlockHeldDetector(N(p), pooled), Pools: true),
        ["CardKills"] = new([nameof(DamageDealt), nameof(CardPlayed)], (p, _) => new CardHitsDetector(N(p), killsOnly: true)),
        ["DebuffVariety"] = new([nameof(PowerChanged), nameof(CombatStarting)],
            (p, pooled) => new DebuffVarietyDetector(N(p), pooled), Pools: true),
        ["BuffVariety"] = new([nameof(PowerChanged), nameof(CombatStarting)], (p, _) => new BuffVarietyDetector(N(p))),
        ["DebuffStacks"] = new([nameof(PowerChanged), nameof(CombatStarting)],
            (p, pooled) => new DebuffStacksDetector(N(p), null, pooled), Pools: true),
        ["PoisonStacks"] = new([nameof(PowerChanged), nameof(CombatStarting)],
            (p, pooled) => new DebuffStacksDetector(N(p), Poison, pooled), Pools: true),
    };

    private const string Poison = "POISON_POWER";
    private const string Doom = "DOOM_POWER";

    public static IReadOnlyCollection<string> Names => Entries.Keys;

    /// <summary>True if this detector's progress builds up over the run and stays shown between fights.</summary>
    public static bool Lasting(string detector) => Entries.TryGetValue(detector, out var e) && e.Lasting;

    /// <summary>True if, in Team mode, this detector adds up every player's share.</summary>
    public static bool Pools(string detector) => Entries.TryGetValue(detector, out var e) && e.Pools;

    /// <summary>True if every player completes this detector's goals at the same moment (spec 4.3).</summary>
    public static bool Shared(string detector) => Entries.TryGetValue(detector, out var e) && e.Shared;

    public static IReadOnlyList<string> RequiredEvents(string detector) => Entries[detector].Events;

    /// <summary>Detectors whose events are all available. Pass the result as <c>DetectionCapabilities.Detectors</c>.</summary>
    public static IReadOnlySet<string> Available(IReadOnlySet<string>? availableEvents) =>
        Entries.Where(kv => availableEvents == null || kv.Value.Events.All(availableEvents.Contains))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <param name="team">Team mode: detectors that pool add up every player's share.</param>
    public static IDetector Create(string detector, IReadOnlyDictionary<string, int> parameters, bool team = false) =>
        Entries.TryGetValue(detector, out var entry)
            ? entry.Create(parameters, team && entry.Pools)
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

    /// <summary>
    /// A kill and who gets it: the player whose hit (or pet's) killed, or, for Poison and Doom,
    /// the player who last added it to that enemy.
    /// </summary>
    private static (ulong Player, int Overkill)? KillBy(GameEvent e, DetectionContext ctx) => e switch
    {
        DamageDealt { TargetDied: true } d when PlayerHitOnEnemy(d) != null => (d.DealerPlayerId!.Value, d.Overkill),
        DamageDealt { TargetDied: true, DealerKind: CreatureKind.None, TargetKind: CreatureKind.Enemy, TargetCombatId: { } enemy, CardId: null } d
            when ctx.Stacks(enemy, Poison) > 0 && ctx.LastApplier(enemy, Poison) is { } poisoner => (poisoner, d.Overkill),
        DoomKilled { TargetCombatId: { } enemy } k when ctx.LastApplier(enemy, Doom) is { } doomer => (doomer, Math.Max(0, k.Doom - k.HpBefore)),
        _ => null,
    };

    /// <summary>The full hit as shown in game: HP lost, Block removed, and overkill.</summary>
    private static int Total(DamageDealt d) => d.Unblocked + d.Blocked + d.Overkill;

    /// <summary>Counters pool in Team unless told not to; run-long ones stay shown between fights.</summary>
    private static Entry Counter(
        Scope scope, string[] events, Func<GameEvent, DetectionContext, IEnumerable<Contribution>> count, Func<int, string> evidence,
        bool pools = true, bool shared = false) =>
        new(events, (p, pooled) => new CounterDetector(scope, N(p), count, evidence, pooled), Lasting: scope == Scope.Run, Pools: pools,
            Shared: shared);

    /// <summary>Single moments for one player: never pooled, shown only during the fight.</summary>
    private static Entry Threshold(string[] events, Func<GameEvent, DetectionContext, Contribution?> measure, Func<int, string> evidence) =>
        Threshold(events, measure, (n, _) => evidence(n));

    private static Entry Threshold(string[] events, Func<GameEvent, DetectionContext, Contribution?> measure, Func<int, GameEvent, string> evidence) =>
        new(events, (p, _) => new ThresholdDetector(N(p), measure, evidence));
}
