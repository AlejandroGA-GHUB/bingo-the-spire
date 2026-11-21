using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using BingoTheSpire.Core.Events;

namespace BingoTheSpire.Mod.GameAdapter;

/// <summary>
/// Hooks the game sends only through the combat state. Subscribed with the game's combat-state
/// hook subscription.
/// </summary>
public sealed class CombatHookListener : HookListener
{
    [Emits(nameof(SideTurnStarted))]
    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        Adapter.Safe(nameof(AfterSideTurnStart), () => Adapter.Publish(new SideTurnStarted(side.ToString())));
        return Task.CompletedTask;
    }

    [Emits(nameof(SideTurnEnded))]
    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        Adapter.Safe(nameof(AfterSideTurnEnd), () => Adapter.Publish(new SideTurnEnded(side.ToString())));
        return Task.CompletedTask;
    }

    [Emits(nameof(PlayerTurnStarted))]
    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        Adapter.Safe(nameof(AfterPlayerTurnStart), () => Adapter.Publish(new PlayerTurnStarted(player.NetId)));
        return Task.CompletedTask;
    }

    [Emits(nameof(CardPlayed))]
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Adapter.Safe(nameof(AfterCardPlayed), () => Adapter.OnCardPlayed(cardPlay));
        return Task.CompletedTask;
    }

    [Emits(nameof(CardDrawn))]
    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        Adapter.Safe(nameof(AfterCardDrawn), () => Adapter.Publish(new CardDrawn(card.Owner.NetId, card.Id.Entry, fromHandDraw)));
        return Task.CompletedTask;
    }

    [Emits(nameof(CardDiscarded))]
    public override Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        Adapter.Safe(nameof(AfterCardDiscarded), () => Adapter.Publish(new CardDiscarded(card.Owner.NetId, card.Id.Entry)));
        return Task.CompletedTask;
    }

    [Emits(nameof(CardExhausted))]
    public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        Adapter.Safe(nameof(AfterCardExhausted), () => Adapter.Publish(new CardExhausted(card.Owner.NetId, card.Id.Entry, causedByEthereal)));
        return Task.CompletedTask;
    }

    [Emits(nameof(CardGenerated))]
    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        Adapter.Safe(nameof(AfterCardGeneratedForCombat), () => Adapter.Publish(new CardGenerated(creator?.NetId, card.Id.Entry)));
        return Task.CompletedTask;
    }

    [Emits(nameof(EnergySpent))]
    public override Task AfterEnergySpent(CardModel card, int amount)
    {
        Adapter.Safe(nameof(AfterEnergySpent), () => Adapter.Publish(new EnergySpent(card.Owner.NetId, card.Id.Entry, amount)));
        return Task.CompletedTask;
    }

    [Emits(nameof(Shuffled))]
    public override Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
    {
        Adapter.Safe(nameof(AfterShuffle), () => Adapter.Publish(new Shuffled(shuffler.NetId)));
        return Task.CompletedTask;
    }

    [Emits(nameof(BlockGained))]
    public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    {
        Adapter.Safe(nameof(AfterBlockGained), () => Adapter.OnBlockGained(creature, amount, cardSource));
        return Task.CompletedTask;
    }

    [Emits(nameof(DamageDealt))]
    public override Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        Adapter.Safe(nameof(AfterDamageGiven), () => Adapter.OnDamageGiven(dealer, result, target, cardSource));
        return Task.CompletedTask;
    }

    [Emits(nameof(DoomKilled))]
    public override Task AfterDiedToDoom(PlayerChoiceContext choiceContext, IReadOnlyList<Creature> creatures)
    {
        Adapter.Safe(nameof(AfterDiedToDoom), () => Adapter.OnDiedToDoom(creatures));
        return Task.CompletedTask;
    }

    [Emits(nameof(PowerChanged))]
    public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        Adapter.Safe(nameof(AfterPowerAmountChanged), () => Adapter.OnPowerChanged(power, amount, applier, cardSource));
        return Task.CompletedTask;
    }

    [Emits(nameof(OrbChanneled))]
    public override Task AfterOrbChanneled(PlayerChoiceContext choiceContext, Player player, OrbModel orb)
    {
        Adapter.Safe(nameof(AfterOrbChanneled), () => Adapter.Publish(new OrbChanneled(player.NetId, orb.Id.Entry)));
        return Task.CompletedTask;
    }

    [Emits(nameof(OrbEvoked))]
    public override Task AfterOrbEvoked(PlayerChoiceContext choiceContext, OrbModel orb, IEnumerable<Creature> targets)
    {
        Adapter.Safe(nameof(AfterOrbEvoked), () => Adapter.Publish(new OrbEvoked(orb.Owner.NetId, orb.Id.Entry)));
        return Task.CompletedTask;
    }
}
