using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.AI
{
    // Execute the production preparation/encounter/interaction engines against a deterministic world.
    // These are multi-tick regressions for the reported run-past-target and leave-before-interacting bugs.
    public static class SIBehaviorVerification
    {
        public static IReadOnlyList<string> Verify()
        {
            var passed = new List<string>();
            passed.AddRange(SIRoutineVerification.Verify());
            static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
            ObjectGuid target = ObjectGuid.Create(HighGuid.Player, 9001L);

            foreach (bool rangedCannotCast in new[] { false, true })
            {
                var world = new EncounterWorld { Ranged = rangedCannotCast, CanCast = !rangedCannotCast, Distance = 40 };
                var encounter = new SIEncounter();
                encounter.Start(target, 0);
                int questTicks = 0;
                for (int tick = 0; tick < 180 && encounter.Active; ++tick)
                {
                    if (encounter.Active) encounter.Tick(world, world.Now); else ++questTicks;
                    world.Advance();
                    Check(questTicks == 0, "Quest/vendor travel interrupted an active approach, pull, fight or loot.");
                }
                Check(encounter.Stage == SIEncounterStage.Complete && world.Health == 0 && world.Loots == 1,
                    "Approach did not become a kill followed by confirmed loot.");
                Check(world.Approaches > 0 && world.Attacks > 0 && world.SeenTargets.All(guid => guid == target),
                    "The encounter changed its committed target or skipped the approach/pull.");
                Check(!rangedCannotCast || encounter.ForceMelee, "A caster with unusable spells never closed for melee.");
                if (!encounter.Active) ++questTicks;
                Check(questTicks == 1, "Quest execution did not resume after the kill and loot finished.");
            }
            passed.Add("encounter replay: approach -> pull -> damage -> kill -> loot -> resume (melee and failed ranged cast)");

            var corpseWorld = new EncounterWorld { Health = 0, Distance = 20 };
            var corpse = new SIEncounter(); corpse.Start(target, 0);
            corpse.Tick(corpseWorld, 0);
            int stops = corpseWorld.Stops;
            corpseWorld.Advance(); corpse.Tick(corpseWorld, corpseWorld.Now);
            Check(corpseWorld.Stops == stops && corpseWorld.Moving, "Repeated loot ticks cancelled their own corpse approach spline.");
            passed.Add("loot approach retains movement across repeated ticks");

            var stuck = new SIEncounter(); stuck.Start(target, 0);
            var stuckWorld = new EncounterWorld { Distance = 40, ApproachResult = SIBehaviorResult.Success };
            Check(stuck.Tick(stuckWorld, 0) == SIBehaviorResult.Running && stuckWorld.Attacks == 0,
                "A navigation success was mistaken for entering attack range.");
            Check(stuck.Tick(stuckWorld, 45000) == SIBehaviorResult.Failure && stuck.Stage == SIEncounterStage.Failed,
                "A target could be chased forever without making contact.");
            var blocked = new SIEncounter(); blocked.Start(target, 0);
            Check(blocked.Tick(new EncounterWorld { Distance = 40, ApproachResult = SIBehaviorResult.Failure }, 0) == SIBehaviorResult.Failure,
                "A blocked approach was counted as a successful kill.");
            var vanished = new SIEncounter(); vanished.Start(target, 0);
            Check(vanished.Tick(new EncounterWorld { Exists = false }, 0) == SIBehaviorResult.Failure,
                "A vanished target remained committed indefinitely.");
            var noDamage = new SIEncounter(); noDamage.Start(target, 0);
            var immune = new EncounterWorld { Distance = 4, DealsDamage = false };
            noDamage.Tick(immune, 0);
            Check(noDamage.Tick(immune, 30000) == SIBehaviorResult.Failure && immune.Loots == 0,
                "Repeated attack requests with no damage were counted as progress or loot.");
            passed.Add("encounter failure paths: false arrival, 45-second chase, blocked path, vanished target, no damage");

            var interaction = new SIInteractionExecution();
            int calls = 0;
            SIBehaviorResult Pending() { ++calls; return SIBehaviorResult.Running; }
            Check(interaction.Tick(Pending, 0) == SIBehaviorResult.Running && interaction.Tick(Pending, 250) == SIBehaviorResult.Running,
                "Reaching an NPC completed the interaction without a confirmed result.");
            Check(interaction.Tick(() => SIBehaviorResult.Success, 500) == SIBehaviorResult.Success && calls == 2,
                "The NPC action did not wait for confirmation.");
            Check(interaction.Tick(() => throw new InvalidDataException("Repeated completed interaction"), 750) == SIBehaviorResult.Success,
                "A completed NPC action executed twice.");
            var retries = new SIInteractionExecution(); int attempts = 0;
            SIBehaviorResult Reject() { ++attempts; return SIBehaviorResult.Failure; }
            Check(retries.Tick(Reject, 0) == SIBehaviorResult.Running && retries.Tick(Reject, 250) == SIBehaviorResult.Running && attempts == 1,
                "The interaction retry delay released its task or retried every frame.");
            retries.Tick(Reject, 2000);
            Check(retries.Tick(Reject, 4000) == SIBehaviorResult.Failure && attempts == 3, "NPC retries were not bounded.");
            var pendingForever = new SIInteractionExecution();
            pendingForever.Tick(Pending, 0);
            Check(pendingForever.Tick(Pending, 20000) == SIBehaviorResult.Failure, "Pending NPC action ignored the 20-second budget.");
            passed.Add("NPC actions: pending/confirmed results, no double action, retained retry delay, bounded failures/time");

            var preparation = new SIPreparation();
            int equipment = 0, supplies = 0;
            SIBehaviorResult Prepare(SIPreparationStage step)
            {
                if (step == SIPreparationStage.Equipment) ++equipment;
                if (step == SIPreparationStage.Supplies) return ++supplies < 3 ? SIBehaviorResult.Running : SIBehaviorResult.Success;
                return SIBehaviorResult.Success;
            }
            preparation.Tick(Prepare, 0); preparation.Tick(Prepare, 100); preparation.Tick(Prepare, 250);
            preparation.Pause(500);
            preparation.Tick(_ => throw new InvalidDataException("Preparation ran during combat/pause"), 40000);
            preparation.Resume(40500);
            preparation.Tick(Prepare, 40750);
            Check(preparation.Stage == SIPreparationStage.Supplies && equipment == 1,
                "Combat restarted preparation or consumed the pending supply step's budget.");
            for (long tick = 41000; !preparation.Complete; tick += 250) preparation.Tick(Prepare, tick);
            preparation.Tick(_ => throw new InvalidDataException("Login preparation restarted after Ready"), 50000);
            Check(equipment == 1 && supplies == 3, "Preparation repeated completed work or skipped a pending supply cast.");
            var unavailable = new SIPreparation(); unavailable.Tick(_ => SIBehaviorResult.Success, 0);
            unavailable.Tick(_ => SIBehaviorResult.Success, 100);
            unavailable.Tick(_ => SIBehaviorResult.Running, 250);
            unavailable.Tick(_ => SIBehaviorResult.Running, 30250);
            Check(unavailable.Stage == SIPreparationStage.Buffs, "Unavailable optional supplies could block questing forever.");
            var task = new SITask { StartedAt = 100, LastProgressAt = 100, HotspotWaitUntil = 6000 };
            task.Interaction.Tick(() => SIBehaviorResult.Running, 100);
            task.Suspend(500); task.Suspend(1000); task.Resume(60500);
            Check(task.StartedAt == 60100 && task.LastProgressAt == 60100 && task.HotspotWaitUntil == 66000 &&
                task.Interaction.Tick(() => SIBehaviorResult.Success, 60750) == SIBehaviorResult.Success,
                "Combat/vendor interruption expired or restarted the retained quest action.");
            passed.Add("login preparation runs once, retains pending supplies, pauses for combat and bounds unavailable actions");
            passed.Add("quest/NPC timers exclude interruptions and resume the same action");
            Check(SISettings.Range(float.NaN, 60, 120) == 60 && SISettings.Range(999, 60, 120) == 120 &&
                SISettings.Range(-1, 60, 120) == 10, "Profile-free search ranges were not bounded.");
            passed.Add("bounded range configuration");
            return passed;
        }

        sealed class EncounterWorld : ISIEncounterActions
        {
            public bool Exists = true, Ranged, CanCast = true, DealsDamage = true, Engaged, Moving;
            public float Distance = 4, Health = 100;
            public long Now, Damage, NextSwing;
            public int Approaches, Attacks, Loots, Stops;
            float _moveRange;
            public SIBehaviorResult ApproachResult = SIBehaviorResult.Running;
            public readonly List<ObjectGuid> SeenTargets = new();
            public SIEncounterView Observe(ObjectGuid target, bool melee)
            {
                SeenTargets.Add(target);
                return new(Exists, Health > 0, true, Engaged, Distance <= (Ranged && !melee ? 25 : 4.5f), true,
                    Health <= 0 && Loots == 0, Health, Damage);
            }
            public SIBehaviorResult Approach(ObjectGuid target, bool melee)
            {
                ++Approaches;
                if (ApproachResult == SIBehaviorResult.Running) { Moving = true; _moveRange = Ranged && !melee ? 25 : 4.5f; }
                return ApproachResult;
            }
            public void StopTravel() { ++Stops; Moving = false; }
            public SIBehaviorResult Attack(ObjectGuid target, bool melee)
            {
                ++Attacks;
                if (Distance > 4.5f && (!Ranged || melee || !CanCast)) return SIBehaviorResult.Running;
                Engaged = true;
                if (DealsDamage && Now >= NextSwing) { Health = Math.Max(0, Health - 25); Damage += 25; NextSwing = Now + 1000; }
                return SIBehaviorResult.Running;
            }
            public SIBehaviorResult Loot(ObjectGuid target)
            {
                if (Distance > 3) { Moving = true; _moveRange = 3; return SIBehaviorResult.Running; }
                ++Loots; return SIBehaviorResult.Success;
            }
            public void Advance()
            {
                Now += 250;
                if (Moving) { Distance = Math.Max(_moveRange, Distance - 3); if (Distance <= _moveRange) Moving = false; }
            }
        }
    }
}
