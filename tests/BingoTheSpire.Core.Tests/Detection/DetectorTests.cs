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
            : this(BingoMode.Versus, detector, n, players)
        {
        }

        public Script(BingoMode mode, string detector, int? n, params ulong[] players)
        {
            var parameters = n is { } v ? new Dictionary<string, int> { ["n"] = v } : new Dictionary<string, int>();
            var goals = new GoalList(1, [new GoalDefinition("goal", "Goal", detector, [new GoalTier(1, parameters)])]);
            var board = new BingoBoard(1, "TEST", 1, mode, BoardDifficulty.Medium, 0,
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

        /// <summary>Damage with no dealer, the way Poison hurts an enemy.</summary>
        public Script PoisonTick(uint enemy, int amount, int overkill = 0, bool died = false) =>
            Send(new DamageDealt(null, CreatureKind.None, CreatureKind.Enemy, null, "ENEMY", enemy, amount, 0, overkill, died, null));
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
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void ExactKills_AllowUpToTwoOverkill(int overkill, bool counts)
    {
        var s = new Script("ExactKills", 1).Fight();
        s.Hit(Alex, 1, 10, overkill: overkill, died: true);
        Assert.Equal(counts, s.Completions.Count == 1);
    }

    [Fact]
    public void FirstTurnKills_CountOnlyTurnOneKills_AcrossTheRun()
    {
        var s = new Script("FirstTurnKills", 3).Fight();
        s.Hit(Alex, 1, 10, died: true);              // turn 1: counts
        s.NextRound().Hit(Alex, 2, 10, died: true);  // turn 2: doesn't
        s.Win();
        Assert.Equal(1, s.Progress[Alex]);

        // Progress lasts between fights.
        s.Fight().Hit(Alex, 3, 10, died: true).Hit(Alex, 4, 10, died: true);
        Assert.Equal(Alex, Assert.Single(s.Completions).PlayerId);
    }

    [Fact]
    public void CardKills_CountOnlyEnemiesTheCardKilled()
    {
        var s = new Script("CardKills", 2).Fight();
        s.Hit(Alex, 1, 10, died: true).Hit(Alex, 2, 10).Play(Alex);
        Assert.Empty(s.Completions);
        s.Hit(Alex, 2, 10, died: true).Hit(Alex, 3, 10, died: true).Play(Alex);
        Assert.Equal(Alex, Assert.Single(s.Completions).PlayerId);
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

    // ---- Team: pooled goals ------------------------------------------------------------------

    [Fact]
    public void Team_PotionsUsed_AddUpBothPlayers_AndTheLastOneCompletes()
    {
        var script = new Script(BingoMode.Team, "PotionsUsed", 3, Alex, Sam)
            .Send(new PotionUsed(Alex, "FIRE_POTION"))
            .Send(new PotionUsed(Sam, "FIRE_POTION"));
        Assert.Equal(2, script.Progress[Alex]);
        Assert.Equal(2, script.Progress[Sam]);
        Assert.Empty(script.Completions);

        script.Send(new PotionUsed(Sam, "BLOCK_POTION"));
        var done = Assert.Single(script.Completions);
        Assert.Equal(Sam, done.PlayerId);
        Assert.Contains("team total", done.Evidence);
    }

    [Fact]
    public void Team_CardsDrawnInTurn_AddUpWithinTheSameTurnOnly()
    {
        var script = new Script(BingoMode.Team, "CardsDrawnInTurn", 4, Alex, Sam).Fight()
            .Send(new CardDrawn(Alex, "STRIKE", true)).Send(new CardDrawn(Alex, "STRIKE", true))
            .Send(new CardDrawn(Sam, "STRIKE", true))
            .NextRound()
            .Send(new CardDrawn(Sam, "STRIKE", true));
        Assert.Empty(script.Completions);
        Assert.Equal(1, script.Progress[Alex]);

        script.Send(new CardDrawn(Alex, "STRIKE", true)).Send(new CardDrawn(Alex, "STRIKE", true)).Send(new CardDrawn(Sam, "STRIKE", true));
        Assert.Equal(Sam, Assert.Single(script.Completions).PlayerId);
    }

    [Fact]
    public void Versus_SameGoal_NeverAddsUp()
    {
        var script = new Script("PotionsUsed", 3, Alex, Sam)
            .Send(new PotionUsed(Alex, "P")).Send(new PotionUsed(Sam, "P")).Send(new PotionUsed(Sam, "P"));
        Assert.Empty(script.Completions);
        Assert.Equal(1, script.Progress[Alex]);
        Assert.Equal(2, script.Progress[Sam]);
    }

    [Fact]
    public void Team_OneMomentGoals_StayOnePlayer()
    {
        var script = new Script(BingoMode.Team, "BiggestHit", 20, Alex, Sam).Fight()
            .Hit(Alex, 1, 12).Hit(Sam, 1, 12);
        Assert.Empty(script.Completions);
    }

    [Fact]
    public void Team_ElitesDefeated_AreNotCountedTwice()
    {
        var script = new Script(BingoMode.Team, "ElitesDefeated", 2, Alex, Sam).Fight("Elite").Win("Elite");
        Assert.Empty(script.Completions);
        Assert.Equal(1, script.Progress[Alex]);
    }

    [Fact]
    public void Team_GoldHeld_AddsBothPurses_AndTakesOffShopSpending()
    {
        var script = new Script(BingoMode.Team, "GoldHeld", 300, Alex, Sam)
            .Send(new GoldGained(Alex, 150))
            .Send(new ItemPurchased(Alex, "Relic", 100))
            .Send(new GoldGained(Sam, 190));
        Assert.Empty(script.Completions);
        Assert.Equal(240, script.Progress[Sam]);

        script.Send(new GoldGained(Alex, 110));
        Assert.Equal(Alex, Assert.Single(script.Completions).PlayerId);
    }

    [Fact]
    public void GoldHeld_ProgressShowsGoldNow_NotThePeak()
    {
        var script = new Script(BingoMode.Team, "GoldHeld", 1000, Alex, Sam)
            .Send(new GoldGained(Alex, 400))
            .Send(new GoldGained(Sam, 276));
        Assert.Equal(676, script.Progress[Alex]);

        // A shop trip, then a room check after an event took some gold.
        script.Send(new ItemPurchased(Sam, "Card", 150));
        Assert.Equal(526, script.Progress[Alex]);
        script.Send(new GoldGained(Alex, 300));
        Assert.Equal(426, script.Progress[Sam]);
        Assert.Empty(script.Completions);
    }

    [Fact]
    public void Team_DebuffStacks_AddUpOnTheSameEnemy()
    {
        var script = new Script(BingoMode.Team, "PoisonStacks", 10, Alex, Sam).Fight()
            .Debuff(Alex, 1, "POISON_POWER", 6, 6)
            .Debuff(Sam, 1, "POISON_POWER", 4, 10);
        Assert.Equal(Sam, Assert.Single(script.Completions).PlayerId);
    }

    [Theory]
    [InlineData(5, 2, 9)]
    [InlineData(20, 2, 35)]
    [InlineData(400, 2, 700)]
    [InlineData(2, 2, 4)]
    [InlineData(1, 2, 2)]
    [InlineData(4, 3, 10)]
    [InlineData(10, 2, 18)]
    public void TeamTarget_AddsThreeQuartersPerExtraPlayer_RoundedClean(int solo, int players, int expected) =>
        Assert.Equal(expected, BoardBuilder.TeamTarget(solo, players));

    [Fact]
    public void TeamBoards_ScaleOnlyPooledGoals()
    {
        // One Ironclad or two build the same goals (the seed uses each character once), so only the numbers differ.
        var one = BoardBuilder.Build(BuiltInGoals.List, new BoardRequest("TEAMSEED", BingoMode.Team, "Standard", ["IRONCLAD"]));
        var two = BoardBuilder.Build(BuiltInGoals.List, new BoardRequest("TEAMSEED", BingoMode.Team, "Standard", ["IRONCLAD", "IRONCLAD"]));
        var detectors = BuiltInGoals.List.Goals.ToDictionary(g => g.Id, g => g.Detector);
        foreach (var (solo, team) in one.Squares.Zip(two.Squares))
        {
            Assert.Equal(solo.GoalId, team.GoalId);
            if (DetectorCatalog.Pools(detectors[solo.GoalId]) && solo.Params.TryGetValue("n", out var n))
            {
                Assert.Equal(BoardBuilder.TeamTarget(n, 2), team.Params["n"]);
                Assert.Contains(team.Params["n"].ToString(System.Globalization.CultureInfo.InvariantCulture), team.Text);
            }
            else
            {
                Assert.Equal(solo.Params, team.Params);
            }
        }
    }

    [Fact]
    public void Team_FlawlessFights_EachCleanPlayerAddsOne_AndBothSeeIt()
    {
        var script = new Script(BingoMode.Team, "FlawlessHallways", 10, Alex, Sam);

        // Both stay clean: the fight counts twice.
        script.Fight().Win();
        Assert.Equal(2, script.Progress[Alex]);
        Assert.Equal(2, script.Progress[Sam]);

        // Only Sam stays clean: one more, and Alex still sees the team's count.
        script.Fight().Hp(Alex, -6, 74).Win();
        Assert.Equal(3, script.Progress[Alex]);
        Assert.Equal(3, script.Progress[Sam]);
    }

    [Fact]
    public void Team_OneMomentGoals_ShowTheTeamsBestToEveryone()
    {
        var script = new Script(BingoMode.Team, "SurviveHit", 10, Alex, Sam).Fight()
            .Send(new DamageDealt(null, CreatureKind.Enemy, CreatureKind.Player, Sam, null, 0, 4, 0, 0, false, null))
            .Send(new DamageDealt(null, CreatureKind.Enemy, CreatureKind.Player, Alex, null, 0, 2, 0, 0, false, null));
        Assert.Equal(4, script.Progress[Alex]);
        Assert.Equal(4, script.Progress[Sam]);
        Assert.Empty(script.Completions);
    }

    [Fact]
    public void Versus_ProgressStaysPersonal()
    {
        var script = new Script("SurviveHit", 10, Alex, Sam).Fight()
            .Send(new DamageDealt(null, CreatureKind.Enemy, CreatureKind.Player, Sam, null, 0, 4, 0, 0, false, null));
        Assert.False(script.Progress.ContainsKey(Alex));
    }

    // ---- Kills by Poison and Doom --------------------------------------------------------------

    [Fact]
    public void ExactKills_PoisonKill_GoesToTheLastPlayerToApplyPoison()
    {
        var script = new Script("ExactKills", 1, Alex, Sam).Fight()
            .Debuff(Sam, 1, "POISON_POWER", 5, 5)
            .Debuff(Alex, 1, "POISON_POWER", 3, 8)
            .PoisonTick(1, 8, died: true);
        Assert.Equal(Alex, Assert.Single(script.Winners));
    }

    [Fact]
    public void ExactKills_PoisonOverkill_DoesNotCount()
    {
        var script = new Script("ExactKills", 1).Fight()
            .Debuff(Alex, 1, "POISON_POWER", 9, 9)
            .PoisonTick(1, 6, overkill: 3, died: true);
        Assert.Empty(script.Completions);
    }

    [Theory]
    [InlineData(209, 209, true)]
    [InlineData(211, 209, true)]
    [InlineData(212, 209, false)]
    public void ExactKills_DoomKill_CountsWithinTwoOfHp(int doom, int hp, bool counts)
    {
        var script = new Script("ExactKills", 1, Alex, Sam).Fight()
            .Debuff(Sam, 1, "DOOM_POWER", doom, doom)
            .Send(new DoomKilled("ENEMY", 1, hp, doom));
        Assert.Equal(counts ? [Sam] : [], script.Winners);
    }

    // ---- Progress between fights ---------------------------------------------------------------

    [Fact]
    public void FightGoals_ProgressClearsWhenTheFightEnds()
    {
        var script = new Script("BiggestHit", 50).Fight().Hit(Alex, 1, 30);
        Assert.Equal(30, script.Progress[Alex]);

        script.Win();
        Assert.Equal(0, script.Progress[Alex]);

        // The next fight starts from nothing, so a smaller hit shows again.
        script.Fight().Hit(Alex, 1, 10);
        Assert.Equal(10, script.Progress[Alex]);
    }

    [Fact]
    public void RunGoals_KeepTheirProgressBetweenFights()
    {
        var script = new Script("PotionsUsed", 5).Fight().Send(new PotionUsed(Alex, "P")).Win().Room("Shop");
        Assert.Equal(1, script.Progress[Alex]);
    }

    // ---- Evidence names ------------------------------------------------------------------------

    [Fact]
    public void DebuffStacks_Evidence_NamesTheDebuffAndTheEnemy()
    {
        var script = new Script("DebuffStacks", 5).Fight()
            .Send(new PowerChanged("WEAK_POWER", "Debuff", 5, 5, CreatureKind.Enemy, null, "FUZZY_WURM_CRAWLER", 1, Alex, null));
        Assert.Equal("5 Weak on Fuzzy Wurm Crawler", Assert.Single(script.Completions).Evidence);

        var named = new Script("DebuffStacks", 5).Fight()
            .Send(new PowerChanged("WEAK_POWER", "Debuff", 5, 5, CreatureKind.Enemy, null, "X", 1, Alex, null, "Faible", "Ver Flou"));
        Assert.Equal("5 Faible on Ver Flou", Assert.Single(named.Completions).Evidence);
    }
}
