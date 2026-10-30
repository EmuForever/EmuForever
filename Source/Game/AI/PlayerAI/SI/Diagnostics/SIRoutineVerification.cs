using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.AI
{
    public static class SIRoutineVerification
    {
        public static IReadOnlyList<string> Verify()
        {
            var passed = new List<string>();
            static void Check(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
            var trace = new List<SICombatActionStage>();
            SIRoutineExecution.Fight(true, stage =>
            {
                trace.Add(stage); return stage == SICombatActionStage.Heal ? SIBehaviorResult.Running : SIBehaviorResult.Failure;
            });
            Check(trace.SequenceEqual(new[] { SICombatActionStage.Reaction, SICombatActionStage.Heal }), "Damage/buffs ran while healing owned the routine.");
            trace.Clear();
            SIRoutineExecution.Fight(true, stage =>
            {
                trace.Add(stage); return stage == SICombatActionStage.CombatBuff ? SIBehaviorResult.Running : SIBehaviorResult.Failure;
            });
            Check(trace.SequenceEqual(new[] { SICombatActionStage.Reaction, SICombatActionStage.Heal, SICombatActionStage.CombatBuff }),
                "The routine skipped healing or cast damage during a pending combat buff.");
            trace.Clear();
            SIRoutineExecution.Fight(false, stage => { trace.Add(stage); return SIBehaviorResult.Failure; });
            Check(trace.SequenceEqual(new[] { SICombatActionStage.Pull, SICombatActionStage.Combat }), "Pull failure did not fall through to the basic attack rotation.");
            var preparation = new SIRoutineExecution(); preparation.Start();
            preparation.Prepare(_ => SIBehaviorResult.Failure, 0);
            Check(preparation.Prepare(stage => stage == SICombatActionStage.PreCombatBuff ? SIBehaviorResult.Running :
                throw new InvalidDataException("Buffing skipped to another pull stage"), 250) == SIBehaviorResult.Running,
                "Pre-pull preparation released its target while buffing.");
            preparation.Prepare(stage => stage == SICombatActionStage.PreCombatBuff ? SIBehaviorResult.Failure :
                throw new InvalidDataException("Pre-pull preparation restarted its completed rest"), 500);
            preparation.EngagedCombat();
            Check(preparation.Prepare(_ => throw new InvalidDataException("Rest/buffs ran while attacked"), 750) == SIBehaviorResult.Success,
                "Combat could not interrupt pre-pull preparation.");
            passed.Add("routine phase execution: heal/buff ownership, pull fallback, retained preparation and combat interrupt");
            var waitingEncounter = new SIEncounter();
            waitingEncounter.Start(ObjectGuid.Create(HighGuid.Player, 9002L), 0);
            waitingEncounter.Pause(250);
            Check(waitingEncounter.Tick(new ApproachOnly(), 61000) == SIBehaviorResult.Running,
                "Resting/buffing or loss of control exhausted the mob's approach budget before movement resumed.");
            passed.Add("encounter timers exclude retained preparation and loss-of-control interruptions");

            foreach (Class cls in new[] { Class.Warrior, Class.Paladin, Class.Hunter, Class.Rogue, Class.Priest,
                Class.DeathKnight, Class.Shaman, Class.Mage, Class.Warlock, Class.Druid })
            {
                SICombatRoutine routine = SIRoutineManager.For(cls);
                Check(routine.Class == cls && routine.GetType().Name == cls + "Routine" && routine.Rules.Count > 0,
                    $"No independent basic routine for {cls}.");
                Check(routine.ForStage(SICombatActionStage.Pull).Any() && routine.ForStage(SICombatActionStage.Combat).Any(),
                    $"{cls} is missing a pull or attack rotation.");
                Check(routine.ForStage(SICombatActionStage.Combat).All(rule => rule.Stage == SICombatActionStage.Combat),
                    $"{cls} healing or buffs leaked into its attack rotation.");
                Check(routine.ForStage(SICombatActionStage.PreCombatBuff).All(rule => rule.Target == SICombatActionTarget.Self),
                    $"{cls} offensive rotation ran during preparation.");
            }
            Check(SIRoutineManager.For(Class.Warrior).Rules.Single(rule => rule.Name == "charge").Stage == SICombatActionStage.Pull &&
                SIRoutineManager.For(Class.Priest).Rules.Single(rule => rule.Name == "heal").Stage == SICombatActionStage.Heal &&
                SIRoutineManager.For(Class.Hunter).Rules.Single(rule => rule.Name == "pet heal").Stage == SICombatActionStage.Heal,
                "Class pull/healing abilities are routed into the wrong phase.");
            passed.Add("ten separate basic class routines: pull, heal, buffs, reactions and combat stage routing");

            Check(SISpellCastPolicy.Accept(SpellCastResult.SpellCastOk) &&
                !SISpellCastPolicy.Accept(SpellCastResult.NotReady) &&
                !SISpellCastPolicy.Accept(SpellCastResult.OutOfRange) &&
                !SISpellCastPolicy.Accept(SpellCastResult.UnitNotInfront) &&
                !SISpellCastPolicy.Accept(SpellCastResult.DontReport),
                "Valid player spells require a pet hit list, or invalid casts bypass normal validation.");
            passed.Add("valid player casts do not require pet auto-targeting; cooldown, range and facing failures remain rejected");

            Check(SIVendorPolicy.PurchaseQuantity(20, 5, 25, 100) == 10 &&
                SIVendorPolicy.PurchaseQuantity(20, 5, 1, 10) == 20 &&
                SIVendorPolicy.PurchaseQuantity(20, 5, 100, 10) == 0 &&
                SIVendorPolicy.PurchaseQuantity(600, 1, 1, 10000) == 255 &&
                SIVendorPolicy.PurchaseQuantity(0, 1, 1, 100) == 0 &&
                SIVendorPolicy.PurchaseQuantity(20, 0, 1, 100) == 0 &&
                SIVendorPolicy.PurchaseQuantity(20, 5, 0, 0) == 20,
                "Food purchases confuse stack price with unit count, overspend reserved funds or exceed packet limits.");
            Check(SIVendorPolicy.RepairNeeded(40, 100, .4f) && !SIVendorPolicy.RepairNeeded(41, 100, .4f) &&
                !SIVendorPolicy.RepairNeeded(0, 0, .4f), "Repair threshold ignored durability or durability-free equipment.");
            Check(new SITask { Kind = SITaskKind.Vendor, VendorPurpose = SIVendorPurpose.Sell }.Key !=
                new SITask { Kind = SITaskKind.Vendor, VendorPurpose = SIVendorPurpose.Restock }.Key,
                "Failed selling blacklisted another purpose at the same merchant.");
            passed.Add("vendor unit pricing/budget/packet bounds, repair threshold and separate sell/repair/restock identity");
            Check(SIVendorPolicy.ShouldSell(16, true, true) && !SIVendorPolicy.ShouldSell(16, true, false) &&
                !SIVendorPolicy.ShouldSell(16, false, true) && !SIVendorPolicy.ShouldSell(0, true, true),
                "Optional junk sales require junk, a nearby vendor and bags to sell from.");
            Check(SITrainerPolicy.NeedsQuery(1, 0) && !SITrainerPolicy.NeedsQuery(2, 2) &&
                SITrainerPolicy.NeedsQuery(3, 2) && SITrainerPolicy.NeedsQuery(4, 3) && SITrainerPolicy.NeedsQuery(5, 4),
                "Trainer availability must query each new level, including odd levels, without repeating a completed query.");
            passed.Add("local-only junk sales, all-broken emergency repairs and database-driven spell training");
            return passed;
        }

        sealed class ApproachOnly : ISIEncounterActions
        {
            public SIEncounterView Observe(ObjectGuid guid, bool melee) => new(true, true, true, false, false, true, false, 100, 0);
            public SIBehaviorResult Approach(ObjectGuid guid, bool melee) => SIBehaviorResult.Running;
            public void StopTravel() { }
            public SIBehaviorResult Attack(ObjectGuid guid, bool melee) => throw new InvalidDataException("Approach-only target was attacked at range");
            public SIBehaviorResult Loot(ObjectGuid guid) => throw new InvalidDataException("Live target was looted");
        }
    }
}
