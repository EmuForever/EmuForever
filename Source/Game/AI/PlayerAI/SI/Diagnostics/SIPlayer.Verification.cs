using Framework.Constants;
using Game.Entities;
using Game.DataStorage;
using Game.Spells;
using System.Numerics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.AI
{
    // Pure rule checks for the bot (pull safety, corpse recovery, combat rules, travel watchdog, movement style,
    // reservations, hotspots). No database, map or session is required.
    public sealed partial class SIPlayer
    {
        public static IReadOnlyList<string> VerifyController()
        {
            var passed = new List<string>();
            passed.AddRange(SIBehaviorVerification.Verify());
            static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }

            foreach (ItemQuality quality in new[] { ItemQuality.Poor, ItemQuality.Normal, ItemQuality.Uncommon, ItemQuality.Rare })
            {
                Require(SIInventoryPolicy.Disposition(ItemClass.Weapon, quality, true, false, false, false, true) == SIItemDisposition.Carry &&
                    SIInventoryPolicy.Disposition(ItemClass.Armor, quality, false, true, false, false, true) == SIItemDisposition.Carry,
                    "Protected quest/supply items or a current upgrade were removed from inventory.");
            }
            Require(SIInventoryPolicy.Disposition(ItemClass.Armor, ItemQuality.Uncommon, false, false, true, false, true) == SIItemDisposition.Bank &&
                SIInventoryPolicy.Disposition(ItemClass.Weapon, ItemQuality.Rare, false, false, false, true, false) == SIItemDisposition.Bank &&
                SIInventoryPolicy.Disposition(ItemClass.TradeGoods, ItemQuality.Normal, false, false, false, false, true) == SIItemDisposition.Auction &&
                SIInventoryPolicy.Disposition(ItemClass.Armor, ItemQuality.Uncommon, false, false, false, false, true) == SIItemDisposition.Auction &&
                SIInventoryPolicy.Disposition(ItemClass.Weapon, ItemQuality.Poor, false, false, false, false, true) == SIItemDisposition.Vendor,
                "Later-use items were auctioned, surplus gear was not listed, or junk went to the AH.");
            foreach (ItemClass itemClass in new[] { ItemClass.Weapon, ItemClass.Armor })
            foreach (ItemQuality quality in new[] { ItemQuality.Poor, ItemQuality.Normal })
            {
                Require(SIInventoryPolicy.IsVendorQuality(itemClass, quality) &&
                    SIInventoryPolicy.Disposition(itemClass, quality, false, false, false, false, true) == SIItemDisposition.Vendor &&
                    SIInventoryPolicy.Disposition(itemClass, quality, false, false, true, true, false) == SIItemDisposition.Vendor,
                    "Gray/white surplus equipment was banked or auctioned instead of vendored.");
            }
            foreach (ItemClass itemClass in new[] { ItemClass.TradeGoods, ItemClass.Gem, ItemClass.Recipe })
                Require(!SIInventoryPolicy.IsVendorQuality(itemClass, ItemQuality.Normal) &&
                    SIInventoryPolicy.Disposition(itemClass, ItemQuality.Normal, false, false, false, false, true) == SIItemDisposition.Auction &&
                    SIInventoryPolicy.Disposition(itemClass, ItemQuality.Normal, true, false, false, false, true) == SIItemDisposition.Carry,
                    "White materials/trade items were vendored, or protected materials were auctioned.");
            Require(!SIInventoryPolicy.IsVendorQuality(ItemClass.Armor, ItemQuality.Uncommon) &&
                !SIInventoryPolicy.IsVendorQuality(ItemClass.Weapon, ItemQuality.Rare),
                "The white-gear rule expanded vendor sales to higher-quality equipment.");
            Require(SIInventoryPolicy.ShouldVisit(10, true, true) && SIInventoryPolicy.ShouldVisit(0, false, true) &&
                !SIInventoryPolicy.ShouldVisit(1, false, true) && !SIInventoryPolicy.ShouldVisit(0, true, false),
                "Inventory service travel ignored proximity, full bags, or eligible stock.");
            Require(SIInventoryPolicy.AuctionUnitPrice(0) == 100 && SIInventoryPolicy.AuctionUnitPrice(123) == 246 &&
                SIInventoryPolicy.AuctionUnitPrice(long.MaxValue) == PlayerConst.MaxMoneyAmount &&
                SIInventoryPolicy.CanPayDeposit(1000, 100, 400) && !SIInventoryPolicy.CanPayDeposit(1000, 100, 401) &&
                !SIInventoryPolicy.CanPayDeposit(0, 0, 1) && !SIInventoryPolicy.CanPayDeposit(100, 100, 1),
                "Auction pricing overflowed or deposits spent the repair/half-money reserve.");
            var bankTask = new SITask { Kind = SITaskKind.Vendor, VendorPurpose = SIVendorPurpose.Bank };
            var auctionTask = new SITask { Kind = SITaskKind.Vendor, VendorPurpose = SIVendorPurpose.Auction };
            Require(bankTask.Key != auctionTask.Key && bankTask.Key != new SITask { Kind = SITaskKind.Vendor }.Key,
                "A failed bank/AH visit blacklisted another inventory service.");
            passed.Add("bank/AH item protection, later-use storage, surplus listings, full-bag trips, price/deposit limits and independent service retries");

            var unrelatedGrind = new SITask { Kind = SITaskKind.Grind, Score = -1000 };
            var questKill = new SITask { Kind = SITaskKind.Objective, Score = 20 };
            var questTurnIn = new SITask { Kind = SITaskKind.TurnIn, Score = 10 };
            Require(ChooseQuestTask(Array.Empty<SITask>()) == null && ChooseQuestTask(new[] { unrelatedGrind }) == null &&
                ChooseQuestTask(new[] { unrelatedGrind, questKill }) == questKill &&
                ChooseQuestTask(new[] { questKill, questTurnIn }) == questTurnIn,
                "Quest mode picked unrelated grinding, lost a kill-count objective, or ignored task priority.");
            passed.Add("quest-only task selection (empty, unrelated grind, quest kill objective, turn-in)");

            var apples = new SITask { Kind = SITaskKind.Objective, Score = 3 };
            var scorpids = new SITask { Kind = SITaskKind.Objective, Score = 20 };
            var distantQuest = new SITask { Kind = SITaskKind.Objective, Score = 500 };
            var firstReturn = new SITask { Kind = SITaskKind.TurnIn, Score = 5 };
            var secondReturn = new SITask { Kind = SITaskKind.TurnIn, Score = 25 };
            var villageDelivery = new SITask { Kind = SITaskKind.TurnIn, Score = 1000 };
            float WorkDistance(SITask task) => task.Score;
            bool InArea(SITask task) => task == apples || task == scorpids || task == firstReturn || task == secondReturn;
            Require(SIQuestBatchPolicy.Choose(new[] { apples, scorpids, firstReturn }, WorkDistance, InArea, false) == apples &&
                SIQuestBatchPolicy.Choose(new[] { scorpids, firstReturn }, WorkDistance, InArea, false) == scorpids &&
                SIQuestBatchPolicy.Choose(new[] { firstReturn, distantQuest }, WorkDistance, InArea, false) == firstReturn &&
                SIQuestBatchPolicy.Choose(new[] { apples, firstReturn, secondReturn }, WorkDistance, InArea, true) == firstReturn &&
                SIQuestBatchPolicy.Choose(new[] { apples, secondReturn }, WorkDistance, InArea, true) == secondReturn &&
                SIQuestBatchPolicy.Choose(new[] { apples, scorpids }, WorkDistance, InArea, false) == apples &&
                SIQuestBatchPolicy.Choose(new[] { scorpids, villageDelivery }, WorkDistance, InArea, true) == scorpids &&
                SIQuestBatchPolicy.Choose(new[] { firstReturn, villageDelivery }, WorkDistance, InArea, true) == firstReturn &&
                SIQuestBatchPolicy.Choose(new[] { villageDelivery }, WorkDistance, InArea, true) == villageDelivery,
                "Area batching returned after one quest, skipped another local objective, or abandoned the remaining turn-ins.");
            Require(SIQuestBatchPolicy.NearbyCost(5, true) < SIQuestBatchPolicy.NearbyCost(3, false) &&
                SIQuestBatchPolicy.NearbyCost(25, true) > SIQuestBatchPolicy.NearbyCost(10, false),
                "A needed apple at the player's feet lost to another pull, or gathering caused a large detour past closer quest work.");
            passed.Add("shared quest areas: gather between pulls, finish local objectives, grouped turn-ins, then the next work area");

            foreach (int level in new[] { 1, 5, 10, 40, 80 })
            {
                Require(SIQuestPolicy.Eligible(level, level, level) && SIQuestPolicy.Eligible(level, Math.Max(1, level - 4), 1) &&
                    SIQuestPolicy.Eligible(level, level + 1, 1) && SIQuestPolicy.Eligible(level, level + 2, 1) &&
                    !SIQuestPolicy.Eligible(level, level + 3, 1) && !SIQuestPolicy.Eligible(level, level, level + 1),
                    "Quest level policy lost the solo upper bound or rejected an eligible quest.");
                if (level >= 6) Require(SIQuestPolicy.TooLow(level, level - 5) && !SIQuestPolicy.TooLow(level, level - 4),
                    "Quest abandonment boundary must be five levels below, not four.");
            }
            Require(SIQuestPolicy.Eligible(1, 2, 1), "Level-one starter cannot accept Cutting Teeth after the introductory turn-in.");
            Require(ChooseQuestTask(new[] { questTurnIn, questKill }, task => task == questKill) == questKill &&
                SIQuestPolicy.MayYield(SITaskKind.Objective, 121, 60) && SIQuestPolicy.MayYield(SITaskKind.TurnIn, 121, 60) &&
                !SIQuestPolicy.MayYield(SITaskKind.Objective, 60, 60) && !SIQuestPolicy.MayYield(SITaskKind.PickUp, 200, 60) &&
                !SIQuestPolicy.MayYield(SITaskKind.Vendor, 200, 60),
                "Local quest work did not beat a distant score, or preempted a committed local/NPC action.");
            passed.Add("quest level bounds, five-level abandonment and local-work priority without interaction churn");



            string report = WhisperStatus(1, new Position(-618.5f, -4251.7f, 38.7f), "quest", "Cutting Teeth", 12, 2, 10);
            Require(report.Contains("map 1") && report.Contains("-618.5") && report.Contains("-4251.7") && report.Contains("38.7") &&
                report.Contains("Goal: quest") && report.Contains("Cutting Teeth") && report.Contains("Quests completed: 12 (2 this SI session)") &&
                report.Contains("Kills: 10 this SI session"), "Whisper report omitted location, goal, quest totals or credited kills.");
            Require(WhisperStatus(1, new Position(-17000, -17000, 10000), "quest", new string('x', 500),
                int.MaxValue, int.MaxValue, int.MaxValue).Length <= 255, "Whisper status exceeded a normal chat message.");
            passed.Add("whisper status (coordinates, goal/task, lifetime/session quests, session kills, length)");

            Require(CanCollectQuestBatch(0, false) && CanCollectQuestBatch(2, false) &&
                !CanCollectQuestBatch(2, true) && CanCollectQuestBatch(0, true),
                "Quest batching did not collect locally, finish its log, then reopen pickup.");
            Position pickupAnchor = new(0, 0, 0);
            Require(WithinQuestPickupArea(pickupAnchor, new Position(60, 0, 0)) &&
                !WithinQuestPickupArea(pickupAnchor, new Position(100, 0, 0)) &&
                !WithinQuestPickupArea(null, new Position()),
                "Quest pickup radius drifted with movement to the next giver or accepted an unset anchor.");
            Require(MustEscape(.20f, true, false) && MustEscape(.1f, true, true) &&
                !MustEscape(.201f, true, false) && !MustEscape(.8f, true, true) && !MustEscape(.1f, false, true),
                "Escape was triggered above critical health, by recovery alone, or outside combat.");
            passed.Add("local quest batches and critical-health escape (fixed radius, batch completion, 20% threshold, recovery alone)");

            var playerFaction = new FactionTemplateRecord { Faction = 1 };
            var neutralFaction = new FactionTemplateRecord { Faction = 2 };
            var hostileFaction = new FactionTemplateRecord { Faction = 3 };
            hostileFaction.Enemies[0] = 1;
            var friendlyFaction = new FactionTemplateRecord { Faction = 4 };
            friendlyFaction.Friend[0] = 1;
            Require(GrindFactionAllowed(playerFaction, neutralFaction) && GrindFactionAllowed(playerFaction, hostileFaction) &&
                !GrindFactionAllowed(playerFaction, friendlyFaction) && !GrindFactionAllowed(playerFaction, playerFaction) &&
                !GrindFactionAllowed(playerFaction, null), "Grind selection excluded neutral wildlife or included friendly creatures.");
            passed.Add("grind factions (neutral, hostile, friendly, self, unknown)");

            // The same ranked spell set drives reactions, recovery, buffs and damage.
            static SpellInfo TestSpell(int id, int level, AuraType aura = AuraType.ModStat, int duration = 600000, SpellEffectName direct = SpellEffectName.None)
            {
                var name = new LocalizedString();
                name[Locale.enUS] = "Test spell";
                var effects = new List<SpellEffectRecord> { new() { EffectIndex = 0, Effect = SpellEffectName.ApplyAura, EffectAura = aura } };
                if (direct != SpellEffectName.None) effects.Add(new() { EffectIndex = 1, Effect = direct });
                var spell = new SpellInfo(new SpellNameRecord { Id = id, Name = name }, Difficulty.None, effects.ToArray());
                spell.SpellLevel = level;
                spell.DurationEntry = new SpellDurationRecord();
                typeof(SpellDurationRecord).GetField("_duration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(spell.DurationEntry, duration);
                return spell;
            }
            SpellInfo rank1 = TestSpell(10001, 1), rank2 = TestSpell(10002, 10), rank3 = TestSpell(10003, 20);
            rank1.ChainEntry = new SpellChainNode { first = rank1, rank = 1 };
            rank2.ChainEntry = new SpellChainNode { first = rank1, rank = 2 };
            rank3.ChainEntry = new SpellChainNode { first = rank1, rank = 3 };
            SpellInfo unrelated = TestSpell(10004, 5);
            foreach (var input in new[] { new[] { rank1, rank2, rank3, unrelated }, new[] { rank3, unrelated, rank2, rank1 } })
                Require(HighestKnownRanks(input).SequenceEqual(new[] { rank3, unrelated }), "Spell selection retained a lower learned rank or dropped an unrelated spell.");
            Require(HighestKnownRanks(new[] { rank1, rank2 }).Single() == rank2, "Spell selection chose an unlearned rank.");
            rank1.SpellName[Locale.enUS] = "Old armor"; rank3.SpellName[Locale.enUS] = "Improved armor";
            Require(!BuffRankIsCurrent(rank1, new[] { rank3 }) && BuffRankIsCurrent(rank3, new[] { rank3 }) &&
                BuffRankIsCurrent(rank1, new[] { unrelated }), "A renamed buff upgrade was mistaken for an already current aura.");
            Require(IsMaintenanceBuff(TestSpell(10005, 1, AuraType.ModStat)) &&
                IsMaintenanceBuff(TestSpell(10006, 1, AuraType.ModPowerRegen)) &&
                IsMaintenanceBuff(TestSpell(10007, 1, AuraType.ModResistance, -1)), "Useful long-lived buffs were omitted.");
            Require(!IsMaintenanceBuff(TestSpell(10008, 1, AuraType.SchoolAbsorb, 30000)) &&
                !IsMaintenanceBuff(TestSpell(10009, 1, AuraType.PeriodicHeal)) &&
                !IsMaintenanceBuff(TestSpell(10010, 1, AuraType.ModShapeshift)) &&
                !IsMaintenanceBuff(TestSpell(10011, 1, AuraType.Mounted)), "Maintenance consumed a defensive/heal or changed movement form.");
            passed.Add("highest learned ranks and maintenance buff coverage");
            Require(IsMountSpell(TestSpell(10015, 1, AuraType.Mounted)) && !IsMountSpell(rank3) && !IsMountSpell(null),
                "Travel mount detection missed a learned mount or selected a normal buff.");
            passed.Add("travel distinguishes actual mount auras from ordinary buffs");
            SpellInfo fireball = TestSpell(10012, 1, AuraType.PeriodicDamage, direct: SpellEffectName.SchoolDamage);
            SpellInfo regrowth = TestSpell(10013, 1, AuraType.PeriodicHeal, direct: SpellEffectName.Heal);
            SpellInfo pain = TestSpell(10014, 1, AuraType.PeriodicDamage);
            Require(HasDirectDamage(fireball) && !AuraBlocksRecast(fireball, false) && !AuraBlocksRecast(regrowth, false) &&
                AuraBlocksRecast(pain, false) && AuraBlocksRecast(fireball, true) && AuraBlocksRecast(rank3, false),
                "An active secondary aura blocked a direct attack/heal, or pure DoT/buff actions were reapplied.");
            passed.Add("hybrid direct-damage/heal recasts while a secondary aura persists; pure DoT/buff retention");

            Require(ReclaimTravelArrival < ReclaimReadyDistance, "Corpse travel must arrive inside the reclaim radius.");
            // Pull safety (SIPullSafetyVerification, rank 1 elite, 2 rare elite, 3 boss, 4 rare).
            static SIPullAssessment Assess(int player = 1, int enemy = 1, CreatureClassifications rank = CreatureClassifications.Normal,
                float health = 1, float mana = 1, bool usesMana = false, int adds = 0, bool learned = false, bool recovering = false) =>
                SIPullSafety.Assess(player, enemy, rank, health, mana, usesMana, adds, learned, recovering);
            static void Allowed(SIPullAssessment result, string message) => Require(result.Allowed, message + " " + result.Reason);
            static void Rejected(SIPullAssessment result, string message) => Require(!result.Allowed && !string.IsNullOrWhiteSpace(result.Reason), message);
            Allowed(Assess(), "A healthy level one cannot pull a solo level one.");
            Allowed(Assess(player: 5, enemy: 1), "Lower-level solo targets were rejected.");
            Rejected(Assess(enemy: 2), "An optional pull selected an above-level enemy.");
            Rejected(Assess(enemy: 4), "A dangerous level gap was ignored.");
            Rejected(Assess(rank: CreatureClassifications.Elite), "A level-one bot pulled an equal-level elite.");
            Rejected(Assess(rank: CreatureClassifications.RareElite), "A level-one bot pulled an equal-level rare elite.");
            Rejected(Assess(rank: CreatureClassifications.Obsolete), "A level-one bot pulled an equal-level world boss.");
            Allowed(Assess(rank: CreatureClassifications.Rare), "A rare normal creature was incorrectly treated as an elite.");
            Allowed(Assess(player: 20, enemy: 15, rank: CreatureClassifications.Elite), "A much stronger solo character cannot attempt a lower elite.");
            Rejected(Assess(health: .89f), "Optional pull ignored incomplete health recovery.");
            Allowed(Assess(health: .9f), "Health readiness boundary changed unexpectedly.");
            Rejected(Assess(mana: .59f, usesMana: true), "Mana class initiated a pull with inadequate resources.");
            Allowed(Assess(mana: .6f, usesMana: true), "Mana readiness boundary changed unexpectedly.");
            Allowed(Assess(mana: 0, usesMana: false), "A rogue was blocked by irrelevant mana.");
            Rejected(Assess(adds: 1), "A starter bot pulled a group of hostiles.");
            Rejected(Assess(player: 20, enemy: 20, adds: 1), "A healthy stronger character initiated an optional group pull.");
            Rejected(Assess(player: 20, enemy: 20, adds: 2), "Multiple additional hostiles were ignored.");
            Rejected(Assess(player: 20, enemy: 20, adds: 1, health: .9f), "A group pull began during partial recovery.");
            Rejected(Assess(learned: true), "A known lethal enemy was deliberately pulled again.");
            Rejected(Assess(recovering: true), "Recovery did not preempt an optional pull.");
            Rejected(Assess(health: float.NaN), "Unknown health was treated as combat readiness.");
            Rejected(Assess(mana: float.PositiveInfinity), "Invalid resources were treated as combat readiness.");
            Rejected(Assess(player: 0), "An uninitialized character initiated combat.");
            Rejected(Assess(enemy: 0), "Unknown enemy difficulty was treated as safe.");
            passed.Add("pull safety (24 cases; equal/lower levels, 90% health, 60% mana, no optional adds)");

            // Corpse recovery and emergency escape (SIControllerVerification).
            var corpse = new Position(0, 0, 0);
            SIRecoveryHazard[] hazards = { new(corpse, 15) };
            Require(!SICorpseRecovery.AvoidsNewThreats(new Position(0, 0, 0), new Position(60, 0, 0), new[] { new SIRecoveryHazard(new Position(30, 0, 0), 10) }) &&
                SICorpseRecovery.AvoidsNewThreats(new Position(0, 0, 0), new Position(60, 0, 0), new[] { new SIRecoveryHazard(new Position(-5, 0, 0), 10) }),
                "Escape crossed a new hostile's aggro or could not leave an existing threat.");
            Position[] safePoints = SICorpseRecovery.Candidates(corpse, new Position(0, 40, 0), hazards).ToArray();
            Require(safePoints.Length > 0 && safePoints.All(point => SICorpseRecovery.IsSafe(point, hazards) &&
                point.GetExactDist(corpse) <= SICorpseRecovery.ReclaimRadius + .001f),
                "Recovery points entered aggro range or exceeded reclaim range.");
            Require(!SICorpseRecovery.Candidates(corpse, new Position(0, 40, 0), new[] { new SIRecoveryHazard(corpse, 45) }).Any(),
                "An unsafe corpse allowed resurrection.");
            Require(!SICorpseRecovery.CanFinishCorpseRun(100) && !SICorpseRecovery.CanFinishCorpseRun(29999) &&
                SICorpseRecovery.CanFinishCorpseRun(30000) && SICorpseRecovery.CanFinishCorpseRun(60000),
                "Corpse reclaim skipped its wait or required an invented walking distance at a nearby graveyard.");
            Require(MustEscape(.2f, true, false) && !MustEscape(.8f, true, true) && !MustEscape(.9f, true, false) && !MustEscape(.1f, false, false),
                "Emergency survival did not protect low health or interrupted recovery.");
            passed.Add("corpse recovery, reclaim run and emergency escape");

            // Class rotation priorities and rule conditions.
            float Score(Class cls, string name, int combo = 0, float targetHealth = 1, float mana = 1, float health = 1) =>
                SIClassSpellPriorities.ScoreNamed(SIClassStrategy.For(cls), name, 10, combo, targetHealth, mana, health);
            Require(!float.IsFinite(Score(Class.Rogue, "eviscerate", 0)) && float.IsFinite(Score(Class.Rogue, "eviscerate", 4)) &&
                !float.IsFinite(Score(Class.Warrior, "execute", targetHealth: .8f)) && float.IsFinite(Score(Class.Warrior, "execute", targetHealth: .1f)) &&
                Score(Class.Mage, "shoot", mana: .1f) > Score(Class.Mage, "frostbolt", mana: .1f) &&
                Score(Class.Warlock, "drain life", health: .3f) > Score(Class.Warlock, "shadow bolt", health: .3f) &&
                Score(Class.Rogue, "sinister strike") > Score(Class.Rogue, "smite"),
                "Class rotation ignored finishers, execute health or resource-specific spell priorities.");
            static SICombatFacts Facts(long combo = 0, float enemy = 1, float distance = 2, bool combat = true, bool casting = false, float health = 1) =>
                new(combat, health, 1, 0, 100, combo, enemy, distance, 5, false, false, casting, false, false, 1, false, false, 1, false, false, false, false, 0);
            SICombatRule execute = SIRoutineManager.For(Class.Warrior).Rules.First(rule => rule.Name == "execute");
            SICombatRule interrupt = SIRoutineManager.For(Class.Warrior).Rules.First(rule => rule.Name == "interrupt");
            SICombatRule charge = SIRoutineManager.For(Class.Warrior).Rules.First(rule => rule.Name == "charge");
            Require(!Facts(enemy: .5f).Matches(execute.Conditions) && Facts(enemy: .1f).Matches(execute.Conditions),
                "Warrior execute ignored the enemy health window.");
            Require(!Facts().Matches(interrupt.Conditions) && Facts(casting: true).Matches(interrupt.Conditions),
                "Interrupt fired without an enemy cast.");
            Require(Facts(combat: false, distance: 20).Matches(charge.Conditions) && !Facts(combat: true, distance: 20).Matches(charge.Conditions) &&
                !Facts(combat: false, distance: 2).Matches(charge.Conditions), "Charge ignored pull state or distance.");
            Require(!Facts(combo: 3).Matches(SICombatCondition.Combo) && Facts(combo: 4).Matches(SICombatCondition.Combo),
                "Combo finisher threshold changed.");
            Require(!Facts(health: .5f).Matches(SICombatCondition.CriticalHealth) && Facts(health: .2f).Matches(SICombatCondition.CriticalHealth),
                "Critical-health reactions changed threshold.");
            foreach (Class cls in new[] { Class.Warrior, Class.Paladin, Class.Hunter, Class.Rogue, Class.Priest, Class.DeathKnight,
                Class.Shaman, Class.Mage, Class.Warlock, Class.Druid })
                Require(SIRoutineManager.For(cls).Rules.Count > 0 && SIRoutineManager.For(cls).Rules.All(rule => rule.Alternatives.Length > 0 &&
                    rule.Alternatives.All(name => name == name.Trim().ToLowerInvariant())), $"{cls} combat rules are empty or not normalized spell names.");
            passed.Add("class priorities and combat rule conditions (10 classes)");

            var style = new SITargetPreference(42);
            Require(style.Seed != new SITargetPreference(43).Seed && style.Seed == new SITargetPreference(42).Seed,
                "Target preference must be stable per character.");
            Require(style.TargetCost(7, 0, 10) < style.TargetCost(7, 0, 100), "Target jitter outweighed distance.");
            Require(RouteCell(0, new Position(1, 1, 0)) != RouteCell(1, new Position(1, 1, 0)), "Blocked routes crossed maps.");
            passed.Add("target preferences and map-specific blocked destinations");
            // Quest reservations between SI players.
            var key = new SIQuestReservationKey(0, 0, false, new ObjectGuid(0x0800000000000000, 900001));
            var owners = Enumerable.Range(1, 10).Select(i => new ObjectGuid(0, 990000 + i)).ToArray();
            Require(owners.Count(owner => SIQuestReservations.TryClaim(key, owner, 1000)) == 1, "Ten SI characters reserved the same pull.");
            SIQuestReservations.Release(key, owners[1]);
            Require(!SIQuestReservations.Available(key, owners[1], 1000) && SIQuestReservations.Available(key, owners[0], 1000),
                "Another character released an owned pull.");
            Require(SIQuestReservations.Available(key, owners[1], 21001) && SIQuestReservations.TryClaim(key, owners[1], 21001),
                "An expired pull stayed reserved.");
            Require(SIQuestReservations.Available(key with { GameObject = true }, owners[2], 21001), "Creature/object GUIDs collided.");
            SIQuestReservations.Release(key, owners[1]);
            Require(SIQuestReservations.Available(key, owners[3], 21001), "Released pull stayed reserved.");
            SIQuestReservations.Prune(long.MaxValue);
            passed.Add("quest target reservations (leases, ownership, expiry)");

            // Hotspots from spawn points: dense groups first, one centre per cell.
            var spawnPoints = new List<System.Numerics.Vector3>();
            for (int i = 0; i < 6; ++i) spawnPoints.Add(new System.Numerics.Vector3(100 + i, 100 + i, 0));
            spawnPoints.Add(new System.Numerics.Vector3(400, 400, 0));
            List<System.Numerics.Vector3> clustered = SIQuestKnowledge.Cluster(spawnPoints);
            Require(clustered.Count == 2 && spawnPoints.Contains(clustered[0]) &&
                System.Numerics.Vector2.Distance(new(clustered[0].X, clustered[0].Y), new(102.5f, 102.5f)) < 1,
                "Spawn hotspots invented a location instead of choosing a real spawn near the dense area's centre.");
            passed.Add("objective and grind hotspot clustering");

            var visitedTask = new SITask { Kind = SITaskKind.Grind, Hotspots = new() { new(0, 0, 1), new(100, 0, 1) } };
            visitedTask.Visited.UnionWith(new[] { 0, 1 });
            var shell = (SIPlayer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SIPlayer));
            Require(shell.NextHotspot(visitedTask) == -1 && visitedTask.Visited.Count == 2,
                "An exhausted search reset its visited list and restarted the same empty area.");
            Require(SearchAreaKey(visitedTask.Key, 0, new(-1, 1, 0)) != SearchAreaKey(visitedTask.Key, 0, new(1, 1, 0)) &&
                SearchAreaKey(visitedTask.Key, 0, new(1, 1, 0)) != SearchAreaKey(visitedTask.Key, 1, new(1, 1, 0)) &&
                SearchAreaKey("quest", 0, new(1, 1, 0)) != SearchAreaKey(visitedTask.Key, 0, new(1, 1, 0)),
                "Search cooldowns mixed map, objective, or negative-coordinate areas.");
            passed.Add("exhausted hotspots finish without resetting; search memory separates map and objective");

            using (var entered = new System.Threading.ManualResetEventSlim())
            using (var release = new System.Threading.ManualResetEventSlim())
            {
                var written = new System.Collections.Concurrent.ConcurrentQueue<string>();
                using var output = new SIBufferedLog(message =>
                {
                    if (message == "first") { entered.Set(); release.Wait(); }
                    written.Enqueue(message);
                }, 2);
                try
                {
                    Require(output.Publish("first") && entered.Wait(TimeSpan.FromSeconds(2)), "Diagnostic worker did not start.");
                    var producer = System.Threading.Tasks.Task.Run(() =>
                        output.Publish("second") && output.Publish("third") && !output.Publish("overflow"));
                    Require(producer.Wait(TimeSpan.FromSeconds(2)) && producer.Result && output.Dropped == 1,
                        "A blocked diagnostic sink stopped the AI producer or grew an unbounded queue.");
                    release.Set();
                    Require(System.Threading.SpinWait.SpinUntil(() => written.Contains("third"), TimeSpan.FromSeconds(2)) &&
                        written.Where(value => value is "first" or "second" or "third").SequenceEqual(new[] { "first", "second", "third" }),
                        "Buffered SI diagnostics lost the retained message order.");
                }
                finally { release.Set(); }
            }
            passed.Add("blocked SI diagnostic output cannot stall gameplay; bounded queue retains message order");
            Require(SIPullSafety.RecoveryReady(90, 60, true) && SIPullSafety.RecoveryReady(91, 61, true) && SIPullSafety.RecoveryReady(91, 0, false) &&
                !SIPullSafety.RecoveryReady(89, 61, true) && !SIPullSafety.RecoveryReady(91, 59, true),
                "Ordinary recovery waits for full resources after pull readiness, or resumes before ready.");
            passed.Add("ordinary recovery ends at combat readiness; mana-free classes do not wait for mana");
            return passed;
        }
    }
}

