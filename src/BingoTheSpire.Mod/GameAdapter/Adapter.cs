using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Recording;

namespace BingoTheSpire.Mod.GameAdapter;

/// <summary>
/// The only place that reads game objects. Turns hook calls into Core <see cref="GameEvent"/>s,
/// tracks which run and combat we're in, and reports which event sources work on this game version.
/// </summary>
internal static class Adapter
{
    private static readonly HashSet<string> LoggedFailures = new();

    private static EventBus? _bus;
    private static RunHookListener? _runListener;
    private static CombatHookListener? _combatListener;
    private static bool _listenersMissingLogged;

    private static readonly List<SourceStatus> HookSources = new();
    private static readonly Dictionary<string, SourceStatus> PatchSources = new();

    private static RunState? _run;
    private static CombatState? _combat;
    private static bool _inCombat;
    private static bool _runEnded;

    /// <summary>Raised after a new run is attached, before its <see cref="RunAttached"/> event is published.</summary>
    public static event Action? RunAttaching;

    /// <summary>Every event source (hooks and patches) and whether it works on this game version.</summary>
    public static IReadOnlyList<SourceStatus> Sources => HookSources.Concat(PatchSources.Values).ToList();

    public static void Install(EventBus bus)
    {
        _bus = bus;
        HookSources.AddRange(BuildHookReport());
        ModHelper.SubscribeForRunStateHooks(ModEntry.ModId + ".run", OnIterateRunListeners);
        ModHelper.SubscribeForCombatStateHooks(ModEntry.ModId + ".combat", OnIterateCombatListeners);
        LogSummary("hooks", HookSources);
    }

    /// <summary>Called by each Harmony patch's Prepare: records whether its target exists on this version.</summary>
    public static void ReportPatch(string source, bool found, params string[] events)
    {
        PatchSources[source] = new SourceStatus(source, found, found ? null : "target method not found", events);
    }

    public static void LogPatchSummary() => LogSummary("patches", PatchSources.Values.ToList());

    private static void LogSummary(string what, IReadOnlyList<SourceStatus> sources)
    {
        var missing = sources.Where(s => !s.Connected).ToList();
        BingoLog.Info($"{what}: {sources.Count - missing.Count}/{sources.Count} connected"
            + (missing.Count == 0 ? "" : "; not available: " + string.Join(", ", missing.Select(s => $"{s.Source} ({s.Reason})"))));
    }

    // ---- Subscription delegates (called by the game every time it gathers hook listeners) ----

    private static IEnumerable<AbstractModel> OnIterateRunListeners(RunState runState)
    {
        AttachRun(runState);
        return ResolveListeners() ? new AbstractModel[] { _runListener! } : Array.Empty<AbstractModel>();
    }

    private static IEnumerable<AbstractModel> OnIterateCombatListeners(CombatState combatState)
    {
        if (combatState.RunState is RunState runState)
        {
            AttachRun(runState);
        }
        _combat = combatState;
        return ResolveListeners() ? new AbstractModel[] { _combatListener! } : Array.Empty<AbstractModel>();
    }

    /// <summary>The game creates the registered listener instances itself; look them up once.</summary>
    private static bool ResolveListeners()
    {
        if (_runListener != null && _combatListener != null)
        {
            return true;
        }
        try
        {
            _runListener ??= ModelDb.GetByIdOrNull<RunHookListener>(ModelDb.GetId<RunHookListener>());
            _combatListener ??= ModelDb.GetByIdOrNull<CombatHookListener>(ModelDb.GetId<CombatHookListener>());
        }
        catch (Exception ex)
        {
            LogOnce("resolve", $"looking up hook listeners failed: {ex}");
        }
        if (_runListener == null || _combatListener == null)
        {
            if (!_listenersMissingLogged)
            {
                _listenersMissingLogged = true;
                BingoLog.Error("hook listeners are not registered in the model database; no events will be recorded");
            }
            return false;
        }
        return true;
    }

    private static void AttachRun(RunState runState)
    {
        if (ReferenceEquals(runState, _run))
        {
            return;
        }
        _run = runState;
        _combat = null;
        _inCombat = false;
        _runEnded = false;
        Safe("RunAttached", () =>
        {
            RunAttaching?.Invoke();
            Publish(new RunAttached(
                Seed: runState.Rng.StringSeed,
                GameMode: runState.GameMode.ToString(),
                IsCustom: IsCustomRun(runState),
                Ascension: runState.AscensionLevel,
                Players: runState.Players.Select(p => new PlayerInfo(p.NetId, p.Character.Id.Entry)).ToList(),
                LocalPlayerId: LocalContext.NetId,
                StartTime: RunStartTime()));
        });
    }

    private static readonly FieldInfo? StartTimeField =
        typeof(RunManager).GetField("_startTime", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly PropertyInfo? StateProperty =
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

    /// <summary>Whether the game's current run is <paramref name="run"/>. True if that can't be checked.</summary>
    private static bool IsCurrentRun(RunState run) =>
        StateProperty?.PropertyType != typeof(RunState) || ReferenceEquals(StateProperty.GetValue(RunManager.Instance), run);

    /// <summary>
    /// When the run was first started, kept by the game across Continue. It's a private field
    /// (same name on main and beta), so it's read by name; 0 if it's missing.
    /// </summary>
    private static long RunStartTime() =>
        StartTimeField?.FieldType == typeof(long) && RunManager.Instance is { } manager
            ? (long)StartTimeField.GetValue(manager)!
            : 0;

    /// <summary>
    /// Main marks custom runs with GameMode "Custom"; beta has a separate IsCustomGame flag instead.
    /// Read both by name so one build works on both.
    /// </summary>
    private static bool IsCustomRun(RunState runState)
    {
        var flag = typeof(RunState).GetProperty("IsCustomGame", BindingFlags.Public | BindingFlags.Instance);
        if (flag?.PropertyType == typeof(bool))
        {
            return (bool)flag.GetValue(runState)!;
        }
        return runState.GameMode.ToString() == "Custom";
    }

    // ---- Publishing ----

    public static void Publish(GameEvent evt)
    {
        _bus?.Publish(evt with { At = CurrentPosition() });
    }

    private static RunPosition? CurrentPosition()
    {
        if (_run == null)
        {
            return null;
        }
        return new RunPosition(_run.CurrentActIndex, _run.ActFloor, _run.TotalFloor, _inCombat ? _combat?.RoundNumber : null);
    }

    /// <summary>Runs a hook body; any exception is logged once per hook and never reaches the game.</summary>
    public static void Safe(string hook, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            LogOnce(hook, $"{hook} failed (further failures of this hook are not logged): {ex}");
        }
    }

    private static void LogOnce(string key, string message)
    {
        if (LoggedFailures.Add(key))
        {
            BingoLog.Error(message);
        }
    }

    // ---- Translations from game objects ----

    public static void OnActEntered()
    {
        if (_run != null)
        {
            Publish(new ActEntered(_run.CurrentActIndex, _run.Act.Id.Entry));
        }
    }

    public static void OnRoomEntered(AbstractRoom room)
    {
        _inCombat = false;
        Publish(new RoomEntered(room.RoomType.ToString(), room.ModelId?.Entry));
    }

    public static void OnCombatStarting()
    {
        _inCombat = true;
        Publish(new CombatStarting());
    }

    public static void OnCombatWon(CombatRoom room) =>
        Publish(new CombatWon(room.RoomType.ToString(), room.Encounter?.Id.Entry));

    public static void OnCombatEnded(CombatRoom room)
    {
        Publish(new CombatEnded(room.RoomType.ToString(), room.Encounter?.Id.Entry));
        _inCombat = false;
    }

    /// <summary>The game can call its end-of-run code more than once; report each run's end once.</summary>
    /// <summary>
    /// The game is saving the current run. Co-op clients reach this too (only the host writes the
    /// file), at the same points, so every player gets the same checkpoints. Saves made while no
    /// run of ours is live, e.g. when a new run is being set up, are ignored.
    /// </summary>
    public static void OnGameSaving()
    {
        if (_run != null && !_runEnded && IsCurrentRun(_run))
        {
            Publish(new GameSaved());
        }
    }

    public static void OnRunEnded(bool isVictory)
    {
        if (_runEnded || _run == null)
        {
            return;
        }
        _runEnded = true;
        Publish(new RunEnded(isVictory, RunManager.Instance.IsAbandoned));
    }

    public static void OnCardPlayed(CardPlay play) =>
        Publish(new CardPlayed(
            play.Player.NetId, play.Card.Id.Entry, play.Card.Type.ToString(),
            play.Resources.EnergySpent, play.Resources.StarsSpent, play.IsAutoPlay, KindOf(play.Target)));

    public static void OnBlockGained(Creature creature, decimal amount, CardModel? cardSource) =>
        Publish(new BlockGained(PlayerIdOf(creature), KindOf(creature), creature.CombatId, ToInt(amount), cardSource?.Id.Entry));

    public static void OnDamageGiven(Creature? dealer, DamageResult result, Creature target, CardModel? cardSource) =>
        Publish(new DamageDealt(
            DealerPlayerId: PlayerIdOf(dealer),
            DealerKind: KindOf(dealer),
            TargetKind: KindOf(target),
            TargetPlayerId: PlayerIdOf(target),
            TargetId: target.Monster?.Id.Entry,
            TargetCombatId: target.CombatId,
            Unblocked: result.UnblockedDamage,
            Blocked: result.BlockedDamage,
            Overkill: result.OverkillDamage,
            TargetDied: target.IsDead,
            CardId: cardSource?.Id.Entry));

    public static void OnDamageReceived(Creature target, DamageResult result, Creature? dealer)
    {
        if (target.IsPlayer && target.Player is { } player)
        {
            Publish(new DamageTaken(player.NetId, result.UnblockedDamage, result.BlockedDamage, KindOf(dealer)));
        }
    }

    public static void OnHpChanged(Creature creature, decimal delta)
    {
        if (creature.IsPlayer && creature.Player is { } player)
        {
            Publish(new HpChanged(player.NetId, ToInt(delta), creature.CurrentHp, creature.MaxHp));
        }
    }

    public static void OnPowerChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        var owner = power.Owner;
        Publish(new PowerChanged(
            PowerId: power.Id.Entry,
            PowerType: power.Type.ToString(),
            Delta: ToInt(amount),
            Amount: power.DisplayAmount,
            OwnerKind: KindOf(owner),
            OwnerPlayerId: PlayerIdOf(owner),
            OwnerId: owner?.Monster?.Id.Entry,
            OwnerCombatId: owner?.CombatId,
            ApplierPlayerId: PlayerIdOf(applier),
            CardId: cardSource?.Id.Entry));
    }

    private static string KindOf(Creature? creature)
    {
        if (creature == null)
        {
            return CreatureKind.None;
        }
        if (creature.IsPet)
        {
            return CreatureKind.Pet;
        }
        if (creature.IsPlayer)
        {
            return CreatureKind.Player;
        }
        return creature.IsEnemy ? CreatureKind.Enemy : CreatureKind.Ally;
    }

    /// <summary>The player a creature is or belongs to: pets (e.g. Necrobinder's Osty) report their owner.</summary>
    private static ulong? PlayerIdOf(Creature? creature) => (creature?.Player ?? creature?.PetOwner)?.NetId;

    private static int ToInt(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // ---- Startup report ----

    /// <summary>
    /// A hook override only counts as connected if it really overrides the game's method on this
    /// version. If the game changed the signature, the runtime treats our method as a new one and
    /// it would never be called.
    /// </summary>
    private static IReadOnlyList<SourceStatus> BuildHookReport()
    {
        var report = new List<SourceStatus>();
        foreach (var type in new[] { typeof(RunHookListener), typeof(CombatHookListener) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<EmitsAttribute>() is not { } emits)
                {
                    continue;
                }
                var connected = method.GetBaseDefinition().DeclaringType == typeof(AbstractModel);
                report.Add(new SourceStatus(method.Name, connected, connected ? null : "no matching game hook", emits.Events));
            }
        }
        return report;
    }
}
