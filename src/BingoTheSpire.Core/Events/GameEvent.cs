using System.Text.Json.Serialization;

namespace BingoTheSpire.Core.Events;

/// <summary>
/// Something that happened in the run, translated out of game types by the game adapter.
/// Detectors and the recorder only ever see these. Game enums arrive as their names
/// (e.g. "Elite", "Debuff"), never as numbers, because the numbers change between branches.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(RunAttached), nameof(RunAttached))]
[JsonDerivedType(typeof(RunEnded), nameof(RunEnded))]
[JsonDerivedType(typeof(GameSaved), nameof(GameSaved))]
[JsonDerivedType(typeof(ActEntered), nameof(ActEntered))]
[JsonDerivedType(typeof(RoomEntered), nameof(RoomEntered))]
[JsonDerivedType(typeof(CombatStarting), nameof(CombatStarting))]
[JsonDerivedType(typeof(CombatWon), nameof(CombatWon))]
[JsonDerivedType(typeof(CombatEnded), nameof(CombatEnded))]
[JsonDerivedType(typeof(SideTurnStarted), nameof(SideTurnStarted))]
[JsonDerivedType(typeof(SideTurnEnded), nameof(SideTurnEnded))]
[JsonDerivedType(typeof(PlayerTurnStarted), nameof(PlayerTurnStarted))]
[JsonDerivedType(typeof(CardPlayed), nameof(CardPlayed))]
[JsonDerivedType(typeof(CardDrawn), nameof(CardDrawn))]
[JsonDerivedType(typeof(CardDiscarded), nameof(CardDiscarded))]
[JsonDerivedType(typeof(CardExhausted), nameof(CardExhausted))]
[JsonDerivedType(typeof(CardGenerated), nameof(CardGenerated))]
[JsonDerivedType(typeof(EnergySpent), nameof(EnergySpent))]
[JsonDerivedType(typeof(Shuffled), nameof(Shuffled))]
[JsonDerivedType(typeof(BlockGained), nameof(BlockGained))]
[JsonDerivedType(typeof(DamageDealt), nameof(DamageDealt))]
[JsonDerivedType(typeof(DamageTaken), nameof(DamageTaken))]
[JsonDerivedType(typeof(HpChanged), nameof(HpChanged))]
[JsonDerivedType(typeof(PowerChanged), nameof(PowerChanged))]
[JsonDerivedType(typeof(OrbChanneled), nameof(OrbChanneled))]
[JsonDerivedType(typeof(OrbEvoked), nameof(OrbEvoked))]
[JsonDerivedType(typeof(PotionProcured), nameof(PotionProcured))]
[JsonDerivedType(typeof(PotionUsed), nameof(PotionUsed))]
[JsonDerivedType(typeof(PotionDiscarded), nameof(PotionDiscarded))]
[JsonDerivedType(typeof(ItemPurchased), nameof(ItemPurchased))]
[JsonDerivedType(typeof(GoldGained), nameof(GoldGained))]
[JsonDerivedType(typeof(RestHealed), nameof(RestHealed))]
[JsonDerivedType(typeof(RestSmithed), nameof(RestSmithed))]
[JsonDerivedType(typeof(RewardTaken), nameof(RewardTaken))]
public abstract record GameEvent
{
    /// <summary>Order of the event within the run, assigned when published.</summary>
    public long Seq { get; init; }

    /// <summary>Milliseconds since the run was attached, assigned when published.</summary>
    public long Ms { get; init; }

    /// <summary>Where in the run it happened, if known.</summary>
    public RunPosition? At { get; init; }
}

/// <param name="Act">0-based act index.</param>
/// <param name="ActFloor">Floor within the act.</param>
/// <param name="Floor">Floor counted across the whole run.</param>
/// <param name="Round">Combat round, only while a combat is running.</param>
public sealed record RunPosition(int Act, int ActFloor, int Floor, int? Round);

/// <summary>Who caused or received something. Players are told apart by <c>PlayerId</c>.</summary>
public static class CreatureKind
{
    public const string Player = "player";
    public const string Pet = "pet";
    public const string Enemy = "enemy";
    public const string Ally = "ally";
    public const string None = "none";
}

public sealed record PlayerInfo(ulong PlayerId, string CharacterId);

// ---- Run and map -------------------------------------------------------------------------

/// <summary>The mod started following a run: a new run, or one loaded with Continue.</summary>
/// <param name="StartTime">
/// When the run was first started (the game's own value, kept across Continue), or 0 if unknown.
/// Seed and start time together tell runs apart, so a Continue finds its saved bingo progress.
/// </param>
public sealed record RunAttached(
    string Seed, string GameMode, bool IsCustom, int Ascension,
    IReadOnlyList<PlayerInfo> Players, ulong? LocalPlayerId, long StartTime = 0) : GameEvent;

/// <summary>The run is over: won, lost, or abandoned (abandoning counts as a loss).</summary>
public sealed record RunEnded(bool Victory, bool Abandoned) : GameEvent;

/// <summary>
/// The game saved the run (or, for a co-op client, reached the point where the host saves it).
/// Continue resumes from here, so it's the checkpoint bingo progress is restored to.
/// </summary>
public sealed record GameSaved : GameEvent;

public sealed record ActEntered(int ActIndex, string ActId) : GameEvent;

/// <param name="RoomType">"Monster", "Elite", "Boss", "Treasure", "Shop", "Event", "RestSite", …</param>
public sealed record RoomEntered(string RoomType, string? RoomId) : GameEvent;

// ---- Combat flow -------------------------------------------------------------------------

public sealed record CombatStarting : GameEvent;

public sealed record CombatWon(string RoomType, string? EncounterId) : GameEvent;

public sealed record CombatEnded(string RoomType, string? EncounterId) : GameEvent;

/// <param name="Side">"Player" or "Enemy".</param>
public sealed record SideTurnStarted(string Side) : GameEvent;

public sealed record SideTurnEnded(string Side) : GameEvent;

public sealed record PlayerTurnStarted(ulong PlayerId) : GameEvent;

// ---- Cards -------------------------------------------------------------------------------

/// <param name="CardType">"Attack", "Skill", "Power", "Status", "Curse", …</param>
/// <param name="TargetKind">A <see cref="CreatureKind"/> value.</param>
public sealed record CardPlayed(
    ulong PlayerId, string CardId, string CardType, int EnergySpent, int StarsSpent,
    bool IsAutoPlay, string TargetKind) : GameEvent;

public sealed record CardDrawn(ulong PlayerId, string CardId, bool FromHandDraw) : GameEvent;

public sealed record CardDiscarded(ulong PlayerId, string CardId) : GameEvent;

public sealed record CardExhausted(ulong PlayerId, string CardId, bool ByEthereal) : GameEvent;

public sealed record CardGenerated(ulong? PlayerId, string CardId) : GameEvent;

public sealed record EnergySpent(ulong PlayerId, string CardId, int Amount) : GameEvent;

public sealed record Shuffled(ulong PlayerId) : GameEvent;

// ---- Damage, Block, HP -------------------------------------------------------------------

// Player ids on creatures: a pet reports its owner, with kind "pet", so goals can count or skip pets.
// Creatures in a combat have a CombatId that is unique within that combat, so two enemies of the
// same kind (e.g. four Phantasmal Gardeners) can be told apart. Ids restart in every combat.

/// <param name="PlayerId">The player who gained Block, or whose pet did (see <c>CreatureKind</c>).</param>
public sealed record BlockGained(ulong? PlayerId, string CreatureKind, uint? CombatId, int Amount, string? CardId) : GameEvent;

/// <param name="Overkill">Damage beyond what was needed to kill. 0 on an exact kill.</param>
/// <remarks>A card's effects (damage, Block, powers) arrive before its <see cref="CardPlayed"/>.</remarks>
public sealed record DamageDealt(
    ulong? DealerPlayerId, string DealerKind, string TargetKind, ulong? TargetPlayerId, string? TargetId, uint? TargetCombatId,
    int Unblocked, int Blocked, int Overkill, bool TargetDied, string? CardId) : GameEvent;

public sealed record DamageTaken(ulong PlayerId, int Unblocked, int Blocked, string DealerKind) : GameEvent;

public sealed record HpChanged(ulong PlayerId, int Delta, int CurrentHp, int MaxHp) : GameEvent;

/// <param name="PowerType">"Buff", "Debuff" or "None".</param>
/// <param name="Delta">Change in amount (negative when reduced).</param>
public sealed record PowerChanged(
    string PowerId, string PowerType, int Delta, int Amount,
    string OwnerKind, ulong? OwnerPlayerId, string? OwnerId, uint? OwnerCombatId, ulong? ApplierPlayerId, string? CardId) : GameEvent;

public sealed record OrbChanneled(ulong PlayerId, string OrbId) : GameEvent;

public sealed record OrbEvoked(ulong PlayerId, string OrbId) : GameEvent;

// ---- Items and economy -------------------------------------------------------------------

public sealed record PotionProcured(ulong PlayerId, string PotionId) : GameEvent;

public sealed record PotionUsed(ulong PlayerId, string PotionId) : GameEvent;

public sealed record PotionDiscarded(ulong PlayerId, string PotionId) : GameEvent;

/// <param name="ItemKind">Kind of shop entry (card, relic, potion, removal, …).</param>
public sealed record ItemPurchased(ulong PlayerId, string ItemKind, int Gold) : GameEvent;

/// <param name="Gold">Gold total after the gain.</param>
public sealed record GoldGained(ulong PlayerId, int Gold) : GameEvent;

public sealed record RestHealed(ulong PlayerId) : GameEvent;

public sealed record RestSmithed(ulong PlayerId) : GameEvent;

public sealed record RewardTaken(ulong PlayerId, string RewardKind) : GameEvent;
