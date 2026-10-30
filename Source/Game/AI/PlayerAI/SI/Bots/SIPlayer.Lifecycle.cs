using Framework.Constants;
using Game.Entities;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        bool _started;
        // QuestBot.Start/LevelBot.Start: initialize once after login. Gameplay runs in UpdateAI.
        public void Start()
        {
            if (_started) return;
            _started = true;
            BeginPreparation();
        }

        public string Behavior { get; private set; } = "Starting";
        public string Activity => _encounter.Active ? $"{_encounter.Stage}: {Resolve(_encounter.Target)?.GetName() ?? "target"}" : !_preparation.Complete ? $"Preparing: {_preparation.Stage}" : Behavior is "Party" or "Mailbox" ? Status : _task?.Label ?? Status;

        SIBehaviorResult TickCombatBehavior()
        {
            Unit partyTarget = PartyAssistTarget();
            if (_encounter.Active && !me.IsInCombat() && !PartyTargetAllowed(Resolve(_encounter.Target))) EndEncounter(false);
            if (!_encounter.Active && partyTarget != null) BeginEncounter(partyTarget);
            Unit attacker = ActiveMobAttackers().Where(unit => CanFight(unit) && !HeldByMagePolymorph(unit)).FirstOrDefault();
            attacker ??= me.GetAttackers().FirstOrDefault(unit => CanFight(unit) && !HeldByMagePolymorph(unit));
            Unit current = Resolve(_encounter.Target);
            if (attacker != null && (!_encounter.Active || !CanFight(current) ||
                attacker != current && !me.IsInCombatWith(current))) BeginEncounter(attacker);
            if (!_encounter.Active && _goal == "attack")
            {
                Unit ordered = Resolve(_target);
                if (CanFight(ordered)) BeginEncounter(ordered);
                else { Status = "Ordered attack target is unavailable"; return SIBehaviorResult.Running; }
            }
            if (!_encounter.Active && CanFight(me.GetVictim())) BeginEncounter(me.GetVictim());
            if (!_encounter.Active)
            {
                if (!me.IsInCombat()) return SIBehaviorResult.Failure;
                CancelTravel(); Status = "Waiting for an attackable combat target"; return SIBehaviorResult.Running;
            }
            Unit target = Resolve(_encounter.Target);
            if (me.IsInCombat())
            {
                _routine.EngagedCombat();
                CommandPetAttack(target);
                // React/heal during pursuit too, without releasing the Kill POI.
                if (target != null && me.GetExactDist(target) > CombatApproachRange(target, _encounter.ForceMelee) &&
                    (TickRoutinePhase(SICombatActionStage.Reaction, target) != SIBehaviorResult.Failure ||
                     TickRoutinePhase(SICombatActionStage.Heal, target) != SIBehaviorResult.Failure)) return SIBehaviorResult.Running;
            }
            return TickEncounter();
        }

        SIBehaviorResult TickGoalBehavior()
        {
            _task?.Resume(Now);
            switch (_goal)
            {
                case "quest":
                    if (!SIQuestKnowledge.IsBuilt) { Status = "Loading quest knowledge"; return SIBehaviorResult.Running; }
                    // Complete local area planning before a finished individual objective
                    // can trigger a premature trip back to its nearby quest giver.
                    if (_workingQuestBatch && _task != null && TaskDone(_task)) Replan();
                    // Completed accepted work gets a turn-in plan before a
                    // fresh pickup scan can acquire another NPC order.
                    if (_task==null&&Now>=_nextReplan&&QuestLog().Any(q=>me.GetQuestStatus(q.Quest.Id)==QuestStatus.Complete)) Replan();
                    // Selecting a local interaction is planning, not its execution. Execute the
                    // retained order in the same tick so pickup scans cannot starve quest work.
                    bool interaction = NearbyQuestInteraction();
                    // Keep the pickup scan's short delay inside the collection phase.
                    if (interaction && (_task == null || TaskDone(_task))) return SIBehaviorResult.Running;
                    // Targets of every accepted quest around the bot, before sweeping the current task's area.
                    SelectNearbyQuestWork();
                    Quest();
                    return SIBehaviorResult.Running;
                case "grind":
                    return TickGrindBehavior();
                case "duel":
                    return TickDuelGoalBehavior();
                case "gather":
                    return TickGatherGoal();
                case "professions":
                    return TickProfessionsBehavior();
                case "social":
                    return TickSocialBehavior();
                case "follow": Follow(); return SIBehaviorResult.Running;
                case "rest": Status = "Rested"; if (!_operatorControlled) _goal = "quest"; return SIBehaviorResult.Running;
                case "guard":
                    Unit enemy = EnemyPlayer();
                    if (enemy != null) BeginEncounter(enemy);
                    else Status = "Guarding";
                    return SIBehaviorResult.Running;
                default: Status = "Idle"; return SIBehaviorResult.Running;
            }
        }
    }
}
