using MegaCrit.Sts2.Core.Models;

namespace BingoTheSpire.Mod.GameAdapter;

/// <summary>
/// Marks a hook override with the Core event types it produces, so the startup report can say
/// which events are available on this game version.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class EmitsAttribute(params string[] events) : Attribute
{
    public string[] Events { get; } = events;
}

/// <summary>
/// Base for the mod's hook listeners. The game registers every concrete <see cref="AbstractModel"/>
/// in a mod automatically, and we hand those registered instances to the game's hook subscription.
///
/// Rules for subclasses:
/// - Only watch: never change game state, always return <see cref="Task.CompletedTask"/>.
/// - Never throw: every override goes through <see cref="Adapter.Safe"/>.
/// - Only override hooks the game sends through the matching state (run vs combat), otherwise
///   a hook can arrive twice during combat.
/// - Don't override hooks whose signature differs between branches (e.g. AfterDeath).
/// </summary>
public abstract class HookListener : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;
}
