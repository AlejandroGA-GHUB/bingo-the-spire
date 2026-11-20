using BingoTheSpire.Core.Boards;
using BingoTheSpire.Core.Detection;
using BingoTheSpire.Core.Events;
using BingoTheSpire.Core.Goals;

namespace BingoTheSpire.Core.Tests.Detection;

/// <summary>
/// Detectors fed hand-made event streams. <see cref="Script"/> plays the role of the game
/// adapter: it stamps every event with the current round, like the real adapter does.
/// </summary>
public class DetectorTests
{
    private const ulong Alex = 1;
    private const ulong Sam = 2;

    /// <summary>Runs one goal tier through a tracker and records what it reports.</summary>
    private sealed class Script
    {
        private readonly GoalTracker _tracker;
        private int? _round;
        private long _seq;

        public Script(string detector, int? n = null, params ulong[] players)
        {
            var parameters = n is { } v ? new Dictionary<string, int> { ["n"] = v } : new Dictionary<string, int>();
            var goals = new GoalList(1, [new GoalDefinition("goal", "Goal", detector, [new GoalTier(1, parameters)])]);
            var board = new BingoBoard(1, "TEST", 1, BingoMode.Versus, BoardDifficulty.Medium, 0,
                [new BoardSquare(0, "goal", 1, parameters, "Goal")]);
            _tracker = new GoalTracker(board, goals, (id, ex) => throw new Xunit.Sdk.XunitException($"{id} threw: {ex}"));
            _tracker.Completed += c => Completions.Add(c);
            _tracker.Progressed += p => Progress[p.PlayerId] = p.Current;
            var ids = players.Length == 0 ? [Alex] : players;
            Send(new RunAttached("TEST", "Standard", false, 0, ids.Select(id => new PlayerInfo(id, "IRONCLAD")).ToList(), ids[0]));
        }

        public List<GoalCompletion> Completions { get; } = [];

        public Dictionary<ulong, int> Progress { get; } = new();

        public IEnumerable<ulong> Winners => Completions.Select(c => c.PlayerId);

        public Script Send(GameEvent evt)
        {
            _tracker.Handle(evt with { Seq = ++_seq, At = new RunPosition(0, 1, 1, _round) });
            return this;
        }

        public Script Room(string type = "Monster") => Send(new RoomEntered(type, null));

        /// <summary>Enters a combat room and starts the fight on round 1.</summary>
        public Script Fight(string type = "Monster")
        {
            Room(type);
            _round = 1;
            return Send(new CombatStarting());
        }

        public Script NextRound()
        {
            _round++;
            return this;
        }

        /// <summary>Ends the fight the way the game does: CombatEnded, then CombatWon outside the combat.</summary>
        public Script Win(string type = "Monster")
        {
            Send(new CombatEnded(type, null));
            _round = null;
            return Send(new CombatWon(type, null));
        }

        public Script Play(ulong player, string type = "Attack", int energy = 1, string card = "STRIKE") =>
            Send(new CardPlayed(player, card, type, energy, 0, false, CreatureKind.Enemy));

        public Script Hit(ulong player, uint enemy, int unblocked, int blocked = 0, int overkill = 0, bool died = false,
            string? card = "STRIKE", string dealerKind = CreatureKind.Player) =>
            Send(new DamageDealt(player, dealerKind, CreatureKind.Enemy, null, "ENEMY", enemy, unblocked, blocked, overkill, died, card));

        public Script Block(ulong player, int amount) => Send(new BlockGained(player, CreatureKind.Player, 0, amount, "DEFEND"));

        public Script Hp(ulong player, int delta, int current) => Send(new HpChanged(player, delta, current, 80));

        public Script Debuff(ulong? applier, uint enemy, string power, int delta, int amount) =>
            Send(new PowerChanged(power, "Debuff", delta, amount, CreatureKind.Enemy, null, "ENEMY", enemy, applier, null));
    }

    [Fact]
    public void CardsPerTurn_CountsWithinOneRound_AndRestarts()
    {
        var s = new Script("CardsPlayedInTurn", 3).Fight();
        s.Play(Alex).Play(Alex);
        s.NextRound().Play(Alex).Play(Alex);
        Assert.Empty(s.Completions);
        s.Play(Alex);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void CardsPerTurn_ProgressDropsBackToZeroOnANewTurn()
    {
        var s = new Script("CardsPlayedInTurn", 5).Fight();
        s.Play(Alex).Play(Alex);
        Assert.Equal(2, s.Progress[Alex]);
        s.NextRound().Send(new CardDrawn(Alex, "STRIKE", true));
        Assert.Equal(0, s.Progress[Alex]);
    }

    [Fact]
    public void CoOp_PlayersAreCountedSeparately_NeverAddedTogether()
    {
        var s = new Script("BlockInTurn", 50, Alex, Sam).Fight();
        s.Block(Alex, 30).Block(Sam, 30).Block(Alex, 15).Block(Sam, 19);
        Assert.Empty(s.Completions); // 45 + 49, not 94
        s.Block(Sam, 1);
        Assert.Equal([Sam], s.Winners);
        Assert.Equal(45, s.Progress[Alex]);
    }

    [Fact]
    public void Completion_IsReportedOncePerPlayer()
    {
        var s = new Script("CardsPlayedInTurn", 1).Fight();
        s.Play(Alex).Play(Alex).NextRound().Play(Alex);
        Assert.Single(s.Completions);
    }

    [Fact]
    public void Completion_CarriesWhereItHappened()
    {
        var s = new Script("CardsPlayedInTurn", 2).Fight().NextRound().NextRound();
        s.Play(Alex).Play(Alex);
        var c = Assert.Single(s.Completions);
        Assert.Equal(3, c.At!.Round);
        Assert.Equal("2 cards in one turn", c.Evidence);
    }

    [Fact]
    public void BlockFromEnemies_DoesNotCount()
    {
        var s = new Script("BlockInTurn", 10).Fight();
        s.Send(new BlockGained(null, CreatureKind.Enemy, 3, 20, null));
        Assert.Empty(s.Completions);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ExactKills_NeedExactlyZeroOverkill(int overkill, bool counts)
    {
        var s = new Script("ExactKills", 1).Fight();
        s.Hit(Alex, 1, 10, overkill: overkill, died: true);
        Assert.Equal(counts, s.Completions.Count == 1);
    }

    [Fact]
    public void ExactKills_IgnoreHitsThatDontKill()
    {
        var s = new Script("ExactKills", 1).Fight();
        s.Hit(Alex, 1, 10);
        Assert.Empty(s.Completions);
    }

    [Fact]
    public void BiggestHit_CountsBlockAndOverkill()
    {
        var s = new Script("BiggestHit", 40).Fight();
        s.Hit(Alex, 1, unblocked: 20, blocked: 10, overkill: 9, died: true);
        Assert.Empty(s.Completions);
        s.Hit(Alex, 2, unblocked: 20, blocked: 10, overkill: 10, died: true);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void PetDamage_CountsForItsOwner()
    {
        var s = new Script("PetDamageInCombat", 15).Fight();
        s.Hit(Alex, 1, 10, dealerKind: CreatureKind.Pet).Hit(Alex, 1, 10, dealerKind: CreatureKind.Player);
        Assert.Empty(s.Completions);
        s.Hit(Alex, 1, 5, dealerKind: CreatureKind.Pet);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void CardHits_GathersTargetsUntilTheCardIsPlayed()
    {
        // The game reports a card's damage before the card itself.
        var s = new Script("CardHits", 3).Fight();
        s.Hit(Alex, 1, 5, card: "THUNDERCLAP").Hit(Alex, 2, 5, card: "THUNDERCLAP").Hit(Alex, 2, 5, card: "THUNDERCLAP");
        s.Play(Alex, card: "THUNDERCLAP");
        Assert.Empty(s.Completions); // two different enemies, one of them hit twice
        s.Hit(Alex, 1, 5, card: "THUNDERCLAP").Hit(Alex, 2, 5, card: "THUNDERCLAP").Hit(Alex, 3, 5, card: "THUNDERCLAP");
        s.Play(Alex, card: "THUNDERCLAP");
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void FlawlessElite_FailsWhenHpIsLost_EvenToYourOwnCards()
    {
        var s = new Script("FlawlessElite").Fight("Elite");
        s.Hp(Alex, -3, 77).Win("Elite");
        Assert.Empty(s.Completions);

        s.Fight("Elite").Block(Alex, 10).Win("Elite");
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void FlawlessElite_InCoOp_OnlyTheUnhurtPlayerGetsIt()
    {
        var s = new Script("FlawlessElite", null, Alex, Sam).Fight("Elite");
        s.Hp(Sam, -6, 60).Win("Elite");
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void FlawlessElite_IgnoresNormalFights()
    {
        var s = new Script("FlawlessElite").Fight().Win();
        Assert.Empty(s.Completions);
    }

    [Fact]
    public void QuickWin_UsesTheFinalRound_EvenThoughVictoryArrivesAfterTheCombat()
    {
        var slow = new Script("QuickWin", 2).Fight().NextRound().NextRound().Win();
        Assert.Empty(slow.Completions);
        var fast = new Script("QuickWin", 2).Fight().NextRound().Win();
        Assert.Equal([Alex], fast.Winners);
    }

    [Fact]
    public void LowHpWin_NeedsToBeAliveAndLow()
    {
        var s = new Script("LowHpWin", 5).Fight();
        s.Hp(Alex, -70, 10).Win();
        Assert.Empty(s.Completions);
        s.Fight().Hp(Alex, -6, 4).Win();
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void ElitesDefeated_CountForEveryPlayerInTheFight()
    {
        var s = new Script("ElitesDefeated", 2, Alex, Sam);
        s.Fight("Elite").Win("Elite").Fight("Elite").Win("Elite");
        Assert.Equal([Alex, Sam], s.Winners.Order());
    }

    [Fact]
    public void ShopSpree_CountsOneVisitOnly()
    {
        var s = new Script("ShopItemsInVisit", 3).Room("Shop");
        s.Send(new ItemPurchased(Alex, "MerchantCardEntry", 50)).Send(new ItemPurchased(Alex, "MerchantRelicEntry", 150));
        s.Room("Shop").Send(new ItemPurchased(Alex, "MerchantCardEntry", 50));
        Assert.Empty(s.Completions);
        s.Send(new ItemPurchased(Alex, "MerchantCardRemovalEntry", 75)).Send(new ItemPurchased(Alex, "MerchantPotionEntry", 50));
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void DebuffVariety_CountsOnlyYourOwnDebuffs_StillActive()
    {
        var s = new Script("DebuffVariety", 3, Alex, Sam).Fight();
        s.Debuff(Alex, 1, "VULNERABLE_POWER", 2, 2).Debuff(Alex, 1, "WEAK_POWER", 1, 1).Debuff(Sam, 1, "POISON_POWER", 5, 5);
        Assert.Empty(s.Completions); // 2 of Alex's + 1 of Sam's
        s.Debuff(null, 1, "WEAK_POWER", -1, 0); // Weak wears off
        s.Debuff(Alex, 1, "FRAIL_POWER", 1, 1);
        Assert.Empty(s.Completions);
        s.Debuff(Alex, 1, "POISON_POWER", 3, 8);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void DebuffVariety_NeedsThemOnTheSameEnemy()
    {
        var s = new Script("DebuffVariety", 2).Fight();
        s.Debuff(Alex, 1, "VULNERABLE_POWER", 2, 2).Debuff(Alex, 2, "WEAK_POWER", 1, 1);
        Assert.Empty(s.Completions);
    }

    [Fact]
    public void DebuffStacks_TwoPlayersStackingTheSameDebuff_DontAddUp()
    {
        var s = new Script("PoisonStacks", 10, Alex, Sam).Fight();
        s.Debuff(Alex, 1, "POISON_POWER", 6, 6).Debuff(Sam, 1, "POISON_POWER", 6, 12);
        Assert.Empty(s.Completions); // 12 on the enemy, but 6 each
        s.Debuff(Alex, 1, "POISON_POWER", 4, 16);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void DebuffStacks_DecayShrinksYourShare()
    {
        var s = new Script("DebuffStacks", 10).Fight();
        s.Debuff(Alex, 1, "POISON_POWER", 8, 8).Debuff(null, 1, "POISON_POWER", -6, 2).Debuff(Alex, 1, "POISON_POWER", 7, 9);
        Assert.Empty(s.Completions); // 2 left + 7 = 9
        s.Debuff(Alex, 1, "POISON_POWER", 1, 10);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void BuffVariety_ForgetsBuffsThatRunOut()
    {
        var s = new Script("BuffVariety", 3).Fight();
        void Buff(string id, int amount) =>
            s.Send(new PowerChanged(id, "Buff", amount, amount, CreatureKind.Player, Alex, null, 0, Alex, null));
        Buff("STRENGTH_POWER", 2);
        Buff("VIGOR_POWER", 3);
        Buff("VIGOR_POWER", 0);
        Buff("DEXTERITY_POWER", 1);
        Assert.Empty(s.Completions);
        Buff("PLATED_ARMOR_POWER", 4);
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void SurviveHit_DoesNotCountTheKillingBlow()
    {
        var s = new Script("SurviveHit", 15).Fight();
        s.Send(new DamageDealt(null, CreatureKind.Enemy, CreatureKind.Player, Alex, null, 0, 20, 0, 5, true, null));
        Assert.Empty(s.Completions);
        s.Send(new DamageDealt(null, CreatureKind.Enemy, CreatureKind.Player, Alex, null, 0, 16, 0, 0, false, null));
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void PotionsInCombat_IgnoresPotionsOutsideFights()
    {
        var s = new Script("PotionsInCombat", 2).Room("Shop");
        s.Send(new PotionUsed(Alex, "FIRE_POTION"));
        s.Fight().Send(new PotionUsed(Alex, "FIRE_POTION"));
        Assert.Empty(s.Completions);
        s.Send(new PotionUsed(Alex, "BLOOD_POTION"));
        Assert.Equal([Alex], s.Winners);
    }

    [Fact]
    public void MissingParameter_FailsWhenTheTrackerIsCreated_NotMidRun()
    {
        var goals = new GoalList(1, [new GoalDefinition("bad", "Bad", "CardsPlayedInTurn", [new GoalTier(1)])]);
        var board = new BingoBoard(1, "TEST", 1, BingoMode.Versus, BoardDifficulty.Medium, 0,
            [new BoardSquare(0, "bad", 1, new Dictionary<string, int>(), "Bad")]);
        Assert.Throws<KeyNotFoundException>(() => new GoalTracker(board, goals));
    }
}
