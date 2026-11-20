using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using StS2Bingo.Core.Events;

namespace StS2Bingo.Mod.GameAdapter;

/// <summary>
/// Hooks the game sends through the run state (in and out of combat). Subscribed with the game's
/// run-state hook subscription.
/// </summary>
public sealed class RunHookListener : HookListener
{
    [Emits(nameof(ActEntered))]
    public override Task AfterActEntered()
    {
        Adapter.Safe(nameof(AfterActEntered), Adapter.OnActEntered);
        return Task.CompletedTask;
    }

    [Emits(nameof(RoomEntered))]
    public override Task AfterRoomEntered(AbstractRoom room)
    {
        Adapter.Safe(nameof(AfterRoomEntered), () => Adapter.OnRoomEntered(room));
        return Task.CompletedTask;
    }

    [Emits(nameof(CombatStarting))]
    public override Task BeforeCombatStart()
    {
        Adapter.Safe(nameof(BeforeCombatStart), Adapter.OnCombatStarting);
        return Task.CompletedTask;
    }

    [Emits(nameof(CombatWon))]
    public override Task AfterCombatVictory(CombatRoom room)
    {
        Adapter.Safe(nameof(AfterCombatVictory), () => Adapter.OnCombatWon(room));
        return Task.CompletedTask;
    }

    [Emits(nameof(CombatEnded))]
    public override Task AfterCombatEnd(CombatRoom room)
    {
        Adapter.Safe(nameof(AfterCombatEnd), () => Adapter.OnCombatEnded(room));
        return Task.CompletedTask;
    }

    [Emits(nameof(DamageTaken))]
    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Adapter.Safe(nameof(AfterDamageReceived), () => Adapter.OnDamageReceived(target, result, dealer));
        return Task.CompletedTask;
    }

    [Emits(nameof(HpChanged))]
    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        Adapter.Safe(nameof(AfterCurrentHpChanged), () => Adapter.OnHpChanged(creature, delta));
        return Task.CompletedTask;
    }

    [Emits(nameof(GoldGained))]
    public override Task AfterGoldGained(Player player)
    {
        Adapter.Safe(nameof(AfterGoldGained), () => Adapter.Publish(new GoldGained(player.NetId, player.Gold)));
        return Task.CompletedTask;
    }

    [Emits(nameof(ItemPurchased))]
    public override Task AfterItemPurchased(Player player, MerchantEntry itemPurchased, int goldSpent)
    {
        Adapter.Safe(nameof(AfterItemPurchased), () => Adapter.Publish(new ItemPurchased(player.NetId, itemPurchased.GetType().Name, goldSpent)));
        return Task.CompletedTask;
    }

    [Emits(nameof(PotionProcured))]
    public override Task AfterPotionProcured(PotionModel potion)
    {
        Adapter.Safe(nameof(AfterPotionProcured), () => Adapter.Publish(new PotionProcured(potion.Owner.NetId, potion.Id.Entry)));
        return Task.CompletedTask;
    }

    [Emits(nameof(PotionUsed))]
    public override Task AfterPotionUsed(PotionModel potion, Creature? target)
    {
        Adapter.Safe(nameof(AfterPotionUsed), () => Adapter.Publish(new PotionUsed(potion.Owner.NetId, potion.Id.Entry)));
        return Task.CompletedTask;
    }

    [Emits(nameof(PotionDiscarded))]
    public override Task AfterPotionDiscarded(PotionModel potion)
    {
        Adapter.Safe(nameof(AfterPotionDiscarded), () => Adapter.Publish(new PotionDiscarded(potion.Owner.NetId, potion.Id.Entry)));
        return Task.CompletedTask;
    }

    [Emits(nameof(RestHealed))]
    public override Task AfterRestSiteHeal(Player player, bool isMimicked)
    {
        Adapter.Safe(nameof(AfterRestSiteHeal), () => Adapter.Publish(new RestHealed(player.NetId)));
        return Task.CompletedTask;
    }

    [Emits(nameof(RestSmithed))]
    public override Task AfterRestSiteSmith(Player player)
    {
        Adapter.Safe(nameof(AfterRestSiteSmith), () => Adapter.Publish(new RestSmithed(player.NetId)));
        return Task.CompletedTask;
    }

    [Emits(nameof(RewardTaken))]
    public override Task AfterRewardTaken(Player player, Reward reward)
    {
        Adapter.Safe(nameof(AfterRewardTaken), () => Adapter.Publish(new RewardTaken(player.NetId, reward.GetType().Name)));
        return Task.CompletedTask;
    }
}
