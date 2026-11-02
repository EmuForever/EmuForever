using Framework.Constants;
using Game.BattleGrounds;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    static readonly Lazy<int[]> BattlemasterEntries = new(() => Global.ObjectMgr.GetCreatureTemplates().Values
        .Where(t => t.Npcflag.HasFlag(NPCFlags1.BattleMaster) && SIBattlegroundPolicy.Supported(Global.BattlegroundMgr.GetBattleMasterBG(t.Entry)))
        .Select(t => t.Entry).ToArray());
    SISpawn? _bgMaster;
    readonly Dictionary<long, long> _bgMasterRetryAt = new();
    readonly Dictionary<string, long> _bgObjectiveRetryAt = new();
    BattlegroundObjective _bgObjective;
    long _bgNextPlan, _bgNextQueue, _bgNextObjective, _bgNextInteraction, _bgNextBuff;
    int _bgInstance;
    bool _bgCarryingFlag;
    ObjectGuid _bgEscortTarget;
    bool _bgEscortApproaching;
    Position _bgRally;
    bool _bgGateSpotFailed;
    Position _bgExitReached;
    long _bgDirectUntil;
    int _bgSpreeBase;
    int KillingSpree(Battleground bg) => bg.KillingBlowsForSI(me.GetGUID()) - _bgSpreeBase;
    bool _bgWasQueued;
    public string BattlegroundState { get; private set; } = "Not queued";
    internal bool WantsBattleground => _goal == "bg";
    bool BattlegroundTravelMountAllowed => !me.InBattleground() ||
        SIBattlegroundPolicy.CanMount(_bgCarryingFlag, me.GetBattleground()?.GetStatus() ?? BattlegroundStatus.None);

    void ResetBattlegroundGoal()
    {
        me.GetSession().CancelBattlegroundQueuesForSI();
        _bgMaster = null; _bgObjective = null; _bgCarryingFlag = false;
        _bgNextPlan = _bgNextQueue = _bgNextObjective = _bgNextInteraction = 0;
        _bgMasterRetryAt.Clear(); _bgObjectiveRetryAt.Clear();
        ResetBattlegroundWaiting();
        BattlegroundState = "Looking for a battlemaster";
    }

    void CompleteBattlegroundVisit()
    {
        Stop(); _bgInstance = 0; _bgObjective = null; _bgMaster = null; _bgRally = null;
        _bgCarryingFlag = false; _bgNextPlan = 0; _bgNextQueue = Now + 3000; _nextWorldSnapshot = 0;
        ResetBattlegroundWaiting();
    }

    void TickBattlegroundGoal()
    {
        Behavior = "Battleground";
        _preparation.Pause(Now); _task?.Suspend(Now);
        var bg = me.GetBattleground();
        if (bg == null)
        {
            _bgCarryingFlag = false;
            RefreshSpellBook(); SelectAmmunition(); RefreshPartyContext();
            if (TickPartyBuffs()) return;
            if (me.InBattlegroundQueue())
            {
                if (!_bgWasQueued)
                {
                    CancelTravel(); ReleaseJourney(); me.SetSelection(ObjectGuid.Empty);
                    SetTask(null); _suspendedTask = null; ResetProfessionGoal();
                    _bgWasQueued = true;
                }
                BattlegroundState = BattlegroundQueueDescription();
            }
            else if (_bgWasQueued)
            {
                CancelTravel(); ResetBattlegroundWaiting(); _bgNextPlan = 0;
            }
            if (TickCombatBehavior() != SIBehaviorResult.Failure) return;
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return;
            if (TickCrafting()) return;
            if (Recuperate() || Rest()) return;
            if (_bgWasQueued) { TickBattlegroundWaiting(); return; }
            TickBattlemasterTrip();
            return;
        }
        if (_bgInstance != bg.GetInstanceID())
        {
            Stop(); ReleaseJourney(); SetTask(null); _suspendedTask = null;
            _bgInstance = bg.GetInstanceID(); _bgObjective = null; _bgMaster = null;
            ResetBattlegroundWaiting();
            // Take the north road through Stormpike rather than a cliff shortcut
            // selected by a direct cross-valley route from the Alliance cave.
            _bgRally = bg.GetTypeID() == BattlegroundTypeId.AV && bg.GetMapId() == 30 &&
                bg.GetPlayerTeam(me.GetGUID()) == Team.Alliance && me.GetPositionX() > 700
                ? new Position(669.2f, -294.1f, 30.56f) : null;
            _bgObjectiveRetryAt.Clear(); _bgNextObjective = 0; _bgGateSpotFailed = false; _bgSpreeBase = 0; _bgExitReached = null; _bgBuffWaitUntil = 0;
            _nextWorldSnapshot = 0;
            me.GetSession().CancelBattlegroundQueuesForSI();
        }
        if (bg.GetStatus() == BattlegroundStatus.WaitLeave)
        {
            Stop(); BattlegroundState = Status = "Match finished; returning to the battlemaster";
            me.GetSession().LeaveFinishedBattlegroundForSI();
            return;
        }
        if (me.IsInCombat()) { _recovering = _resting = false; Recovery = "Alive"; }
        RefreshSpellBook(); SelectAmmunition(); RefreshPartyContext();
        if (bg.GetStatus()!=BattlegroundStatus.InProgress&&TickPartyBuffs()) return;
        if (me.HasUnitState(UnitState.LostControl | UnitState.Stunned | UnitState.Confused | UnitState.Fleeing))
        { ClassReaction(null); Status = "Recovering control in the battleground"; return; }
        if (bg.GetStatus() != BattlegroundStatus.InProgress)
        {
            BattlegroundState = Status = "Preparing for the battleground gates to open";
            if (WaitForTeammateBuffs(bg)) return;
            if (!_bgGateSpotFailed && bg.GateWaitSpotForSI(me) is { } spot && me.GetExactDist2d(spot) > 2)
            {
                var walk = Travel(spot, "Waiting by the gate", arrival: 1.5f, destinationTolerance: 2, heightKnown: false, rememberFailure: false);
                if (walk == SIBehaviorResult.Running) return;
                if (walk == SIBehaviorResult.Failure)
                {
                    // The SI planner rejects a few start-room steps; the core's mmaps path gets there instead.
                    _bgGateSpotFailed = true;
                    me.GetMotionMaster().MovePoint(0, spot, generatePath: true);
                    return;
                }
            }
            if (_bgGateSpotFailed && me.IsMoving()) return;   // the core path is still walking it there
            CancelTravel();
            if (me.IsStopped() && bg.GateWaitSpotForSI(me) is { } facing) me.SetFacingTo(facing.GetOrientation());
            if (!me.IsNonMeleeSpellCast(false, skipAutorepeat: true))
            {
                // Joining refills health and power, so the gate wait is for buffs, not food.
                if (Now >= _bgNextBuff) { _bgNextBuff = Now + 1600; if (Buff()) return; }
                TryClassAction(SICombatActionStage.Heal);
            }
            return;
        }
        if (Now >= _bgNextObjective || _bgObjective == null)
        {
            _bgNextObjective = Now + 1000;
            ChooseBattlegroundObjective(bg);
        }
        EmergencySurvival();
        if (_goal == "flee" && !_bgCarryingFlag) { Flee(); return; }
        var bgPlayers = bg.GetPlayers().Keys.Select(Global.ObjAccessor.FindPlayer).Where(p => p != null&&p.GetMap()==me.GetMap()).ToList();
        bool movingCarrier=SIBattlegroundPolicy.CarrierMovingOnly(_bgCarryingFlag,bg.InFlagRoomForSI(me));
        if(_knownSpells.Any(spell=>IsHeal(spell)||IsShield(spell))&&!me.IsNonMeleeSpellCast(false,skipAutorepeat:true))
            foreach(var ally in bgPlayers.Where(p=>p!=me&&SIBattlegroundPolicy.HelpTeammate(p.GetHealthPct(),p.IsAlive(),p.GetBGTeam()==me.GetBGTeam(),me.GetDistance(p),true)&&CombatSight(p)).OrderBy(p=>p.GetHealthPct()).ThenBy(me.GetDistance))
                if(HealAlly(ally,instantOnly:movingCarrier)){Status="Healing battleground teammate: "+ally.GetName();return;}
        if(TickPartyBuffs(instantOnly:movingCarrier))return;
        if (movingCarrier)
        {
            // Keep the flag moving; only instant actions may accompany the run.
            _recovering = _resting = false; Recovery = "Alive"; Dismount();
            if (_encounter.Active) EndEncounter(false);
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return;
            var threat=bgPlayers.Where(p=>CanFight(p)&&me.GetDistance(p)<=35&&CombatSight(p)).OrderBy(me.GetDistance).FirstOrDefault();
            bool acted=TryClassAction(SICombatActionStage.Reaction,threat,instantOnly:true)||TryClassAction(SICombatActionStage.Heal,instantOnly:true);
            if(!acted&&threat!=null)
                foreach(var spell in OffensiveSpells(threat).Where(SIInstantAttackPolicy.Instant))
                    if(Cast(spell,threat,keepMoving:true))break;
            ExecuteBattlegroundObjective(bg);
            return;
        }
        // Escorts with heals keep the flag carrier at 90% health or more, ahead of fighting.
        if (_bgObjective is { Action: BattlegroundObjectiveAction.Escort } escort && Resolve(escort.Target) is Player runner &&
            runner.IsAlive() && runner.GetHealthPct() < 90 && !me.IsNonMeleeSpellCast(false, skipAutorepeat: true))
        {
            if (HealAlly(runner)) { Status = "Healing our flag carrier"; return; }
        }
        // Initiate PvP on every nearby enemy, including while escorting.
        // Flag assignments resume as soon as the local fight ends.
        // Focus fire: count teammates already hitting each enemy, so bots pile onto the same low target.
        var allyAttackers = new Dictionary<ObjectGuid, int>();
        foreach (Player ally in bgPlayers)
            if (ally != me && ally.IsAlive() && ally.GetBGTeam() == me.GetBGTeam() && ally.GetVictim() is { } victim)
                allyAttackers[victim.GetGUID()] = allyAttackers.GetValueOrDefault(victim.GetGUID()) + 1;
        float FocusScore(Unit u)
        {
            float score = u.GetHealthPct() + me.GetDistance(u);   // low health and close first
            score -= Math.Min(3, allyAttackers.GetValueOrDefault(u.GetGUID())) * 25;
            if (u.GetGUID() == _bgObjective?.Target && _bgObjective.Action == BattlegroundObjectiveAction.Attack) score -= 200;
            return score;
        }
        var nearbyEnemy = bgPlayers.Cast<Unit>().Concat(NearbyCreatures(35))
            .Where(p => CanFight(p) && me.GetDistance(p) <= 35 && CombatSight(p)&&(!_bgCarryingFlag||bg.InFlagRoomForSI(p)))
            .OrderBy(FocusScore).FirstOrDefault();
        // A dead (or released) target ends the fight even when no other enemy is near.
        if (_encounter.Active && (Resolve(_encounter.Target) is not { } current || !current.IsAlive() || me.GetDistance(current) > 60))
            EndEncounter(false);
        if (nearbyEnemy != null && (!_encounter.Active || !CanFight(Resolve(_encounter.Target))))
            BeginEncounter(nearbyEnemy);
        // Switch to a clearly better focus target (hysteresis keeps bots from flipping every tick).
        else if (nearbyEnemy != null && _encounter.Active && Resolve(_encounter.Target) is Unit engaged &&
            engaged.GetGUID() != nearbyEnemy.GetGUID() && FocusScore(nearbyEnemy) + 40 < FocusScore(engaged))
        { EndEncounter(false); BeginEncounter(nearbyEnemy); }
        if (_encounter.Active)
        {
            _recovering = _resting = false; Recovery = "Alive";
            // PvP must not stand still in the PvE rest/pre-pull preparation stages.
            _routine.EngagedCombat();
            if (TickCombatBehavior() != SIBehaviorResult.Failure) return;
        }
        if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return;
        if (_bgCarryingFlag)
        {
            if (ClassReaction(null) || TryClassAction(SICombatActionStage.Heal)) return;
        }
        else
        {
            // No eating or drinking in a battleground, except on a killing spree of 5 or more since the last death.
            if (KillingSpree(bg) >= 5) { if (Recuperate() || Rest()) return; }
            else if (me.GetHealthPct() < 60 && TryClassAction(SICombatActionStage.Heal)) return;
            if (Now >= _bgNextBuff) { _bgNextBuff = Now + 10000; if (Buff()) return; }
        }
        if (!me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) ExecuteBattlegroundObjective(bg);
    }

    void ResetBattlegroundWaiting()
    {
        if (_bgWasQueued) { ResetProfessionGoal(); LeaveTradeChannel(); }
        _bgWasQueued = false;
    }

    string BattlegroundQueueDescription()
    {
        var queues = new List<string>();
        for (int slot = 0; slot < SharedConst.MaxPlayerBGQueues; ++slot)
        {
            var queue = me.GetBattlegroundQueueTypeId(slot);
            if (queue == default) continue;
            var template = Global.BattlegroundMgr.GetBattlegroundTemplateByTypeId(queue.BattlemasterListId);
            var bracket = template == null ? null : Global.DB2Mgr.GetBattlegroundBracketByLevel(
                template.BattlemasterEntry.MapId[0], me.GetLevel());
            queues.Add(queue.BattlemasterListId + (bracket == null ? "" : $" ({bracket.MinLevel}-{bracket.MaxLevel})") +
                (me.InBattleground() && me.GetBattlegroundTypeId() == queue.BattlemasterListId ? ": in match" :
                    me.IsInvitedForBattlegroundQueueType(queue) ? ": invited" : ": queued"));
        }
        return queues.Count == 0 ? "Not queued" : string.Join(", ", queues) + $"; {me.GetTeam()}";
    }

    void TickBattlegroundWaiting()
    {
        // Social behavior runs under the retained bg order, so switching activity
        // neither removes queue tickets nor disables the invite-first main loop.
        Behavior = "Social while queued";
        _task?.Resume(Now);
        if (TickAuctionMail() || TickVendorBehavior() || TickTrainerBehavior() || TickFirstAidUpkeep()) return;
        TickSocialBehavior();
    }

    bool EligibleBattleground(BattlegroundTypeId type)
    {
        if (!SIBattlegroundPolicy.Supported(type)) return false;
        var template = Global.BattlegroundMgr.GetBattlegroundTemplateByTypeId(type);
        return template != null && !template.IsArena() && me.GetBGAccessByLevel(type) && me.CanJoinToBattleground(template) &&
            !template.BattlemasterEntry.HasFlag(BattlemasterListFlags.Disabled) &&
            !Global.DisableMgr.IsDisabledFor(DisableType.Battleground, (int)type, null) &&
            Global.DB2Mgr.GetBattlegroundBracketByLevel(template.BattlemasterEntry.MapId[0], me.GetLevel()) != null;
    }

    void TickBattlemasterTrip()
    {
        if (me.IsDeserter()) { CancelTravel(); BattlegroundState = Status = "Waiting for the deserter penalty to expire"; return; }
        if (me.IsUsingLfg()) { CancelTravel(); BattlegroundState = Status = "Leave the dungeon finder before queueing for a battleground"; return; }
        if (!me.HasFreeBattlegroundQueueId())
        { CancelTravel(); BattlegroundState = Status = "Queued; waiting for a battleground invitation"; return; }
        if (Now < _bgNextQueue)
        {
            if (me.GetSession().SIBattlegroundJoinResult is string result) BattlegroundState = Status = result;
            return;
        }
        // Queue from anywhere, as a player does from the PvP panel. Walking every bot to a battlemaster
        // made ".si bg all" plan hundreds of cross-continent routes at once and froze the map threads.
        if (Now < _bgNextPlan) return;
        _bgNextPlan = Now + 15000;
        BattlegroundTypeId remote = RemoteBattlegroundTypes
            .Where(EligibleBattleground)
            .Where(t => !me.InBattlegroundQueueForBattlegroundQueueType(Global.BattlegroundMgr.BGQueueTypeId(t, BattlegroundQueueIdType.Battleground, false, 0)))
            .OrderBy(t => SIBattlegroundPolicy.Score(0, 0, 0, false, me.GetGUID().GetCounter(), "queue:" + t))
            .FirstOrDefault();
        if (remote == BattlegroundTypeId.None)
        {
            CancelTravel(); BattlegroundState = Status = me.InBattlegroundQueue()
                ? "Queued for available battlegrounds; waiting for an invitation"
                : "No eligible battleground for this level; checking again shortly";
            return;
        }
        CancelTravel();
        _bgNextQueue = Now + 2000;
        if (me.GetSession().JoinBattlegroundRemotelyForSI(remote))
            BattlegroundState = Status = "Requesting a queue for " + remote;
    }

    static readonly BattlegroundTypeId[] RemoteBattlegroundTypes =
        { BattlegroundTypeId.WS, BattlegroundTypeId.AB, BattlegroundTypeId.AV, BattlegroundTypeId.DI };

    void TickBattlemasterTripByFoot()
    {
        if (_bgMaster is not SISpawn master)
        {
            if (Now < _bgNextPlan) return;
            _bgNextPlan = Now + 15000;
            PruneExpired(_bgMasterRetryAt);
            _bgMaster = ServiceSpawns(BattlemasterEntries.Value, SITaskKind.Battleground)
                .Where(s => !_bgMasterRetryAt.ContainsKey(s.SpawnId))
                .Where(s => EligibleBattleground(Global.BattlegroundMgr.GetBattleMasterBG(s.Entry)))
                .Where(s => !me.InBattlegroundQueueForBattlegroundQueueType(Global.BattlegroundMgr.BGQueueTypeId(
                    Global.BattlegroundMgr.GetBattleMasterBG(s.Entry), BattlegroundQueueIdType.Battleground, false, 0)))
                .OrderBy(s => SIBattlegroundPolicy.Score(0, Distance(s.Position), 0, false, me.GetGUID().GetCounter(),
                    "queue:" + Global.BattlegroundMgr.GetBattleMasterBG(s.Entry)))
                .Select(s => (SISpawn?)s).FirstOrDefault();
            if (_bgMaster is not SISpawn selected)
            {
                CancelTravel(); BattlegroundState = Status = me.InBattlegroundQueue()
                    ? "Queued for available battlegrounds; waiting for an invitation"
                    : "No eligible, friendly battlemaster is available on this continent; checking again shortly";
                return;
            }
            master = selected;
        }
        var type = Global.BattlegroundMgr.GetBattleMasterBG(master.Entry);
        if (!EligibleBattleground(type)) { _bgMaster = null; _bgNextPlan = 0; return; }
        BattlegroundState = Status = "Travelling to a battlemaster for " + type;
        var approach = ApproachFlightMaster(master, Status, out var npc);
        if (approach == SIBehaviorResult.Running) return;
        _bgMaster = null; _bgNextPlan = 0; _bgNextQueue = Now + 2000;
        if (approach == SIBehaviorResult.Success && me.GetSession().JoinBattlegroundForSI(npc))
        {
            CancelTravel(); BattlegroundState = Status = "Requesting a queue for " + type;
        }
        // Failed NPCs and native queue refusals must not create a busy retry loop.
        _bgMasterRetryAt[master.SpawnId] = Now + 30000;
    }

    // Before the gates open, a bot that lacks a buff a teammate can cast waits within range and line of sight
    // of that teammate instead of walking to its gate. Gives up after 90 s so a missing buffer cannot hold a bot.
    long _bgBuffWaitUntil;
    bool WaitForTeammateBuffs(Battleground bg)
    {
        if (_bgBuffWaitUntil == 0) _bgBuffWaitUntil = Now + 90000;
        if (Now >= _bgBuffWaitUntil) return false;
        Player buffer = null;
        foreach (ObjectGuid guid in bg.GetPlayers().Keys)
        {
            Player ally = Global.ObjAccessor.FindPlayer(guid);
            if (ally == null || ally == me || !ally.IsAlive() || ally.GetMap() != me.GetMap() ||
                bg.GetPlayerTeam(guid) != bg.GetPlayerTeam(me.GetGUID())) continue;
            bool canBuff = ally.GetSpellMap().Keys.Any(id => Global.SpellMgr.GetSpellInfo(id, Difficulty.None) is { } spell &&
                CanBuffParty(spell) && !me.HasSpell(spell.Id) && !HasCurrentBuff(me, SpellName(spell)) && !ConflictsWithActiveBuff(spell));
            if (canBuff && (buffer == null || me.GetExactDist(ally) < me.GetExactDist(buffer))) buffer = ally;
        }
        if (buffer == null) return false;
        if (me.GetExactDist(buffer) <= 30 && CombatSight(buffer))
        {
            CancelTravel();
            Status = BattlegroundState = "Waiting in range of " + buffer.GetName() + " for a buff";
            return true;
        }
        var walk = Travel(buffer.GetPosition(), "Walking to " + buffer.GetName() + " for a buff", arrival: 20, destinationTolerance: 20, rememberFailure: false);
        if (walk == SIBehaviorResult.Failure) { _bgBuffWaitUntil = Now; return false; }
        return true;
    }

    void ChooseBattlegroundObjective(Battleground bg)
    {
        if (_bgRally != null)
        {
            if (me.GetExactDist(_bgRally) > 8)
            {
                _bgObjective = new("av-rally", "Advance along the Stormpike road", _bgRally,
                    BattlegroundObjectiveAction.Move, default, 0);
                _bgCarryingFlag = false;
                return;
            }
            _bgRally = null;
        }
        PruneExpired(_bgObjectiveRetryAt);
        var options = bg.ObjectivesForSI(me).Where(o => o != null).ToArray();
        _bgCarryingFlag = options.Any(o => o.CarryingFlag);
        var allies = bg.GetPlayers().Keys.Select(Global.ObjAccessor.FindPlayer)
            .Where(p => p != null && p != me && p.IsAlive() && p.GetMap() == me.GetMap() && bg.GetPlayerTeam(p.GetGUID()) == bg.GetPlayerTeam(me.GetGUID())).ToArray();
        _bgObjective = options.Where(o => !_bgObjectiveRetryAt.ContainsKey(o.Key))
            .OrderBy(o => SIBattlegroundPolicy.Score(o.Priority, me.GetExactDist2d(o.Position),
                allies.Count(p => p.GetExactDist2d(o.Position) < 25), o.Key == _bgObjective?.Key, me.GetGUID().GetCounter(), o.Key))
            .FirstOrDefault();
    }

    Position _bgProgressPos;
    long _bgProgressAt;
    bool BattlegroundLegStuck()
    {
        if (_bgProgressPos == null || me.GetExactDist2d(_bgProgressPos) >= 3)
        {
            _bgProgressPos = new Position(me.GetPosition()); _bgProgressAt = Now;
            return false;
        }
        return Now - _bgProgressAt >= 6000;
    }

    void ExecuteBattlegroundObjective(Battleground bg)
    {
        if (_bgObjective is not { } objective)
        { CancelTravel(); BattlegroundState = Status = "Waiting for the next battleground objective"; return; }
        BattlegroundState = Status = objective.Label;
        WorldObject target = objective.Target.IsEmpty() ? null : Global.ObjAccessor.GetWorldObject(me, objective.Target);
        if (target == null && objective.SpawnId != 0)
            target = objective.Action == BattlegroundObjectiveAction.Attack
                ? me.GetMap().GetCreatureBySpawnId(objective.SpawnId) : me.GetMap().GetGameObjectBySpawnId(objective.SpawnId);
        SIBehaviorResult result;
        if (target is GameObject obj && objective.Action == BattlegroundObjectiveAction.Interact)
        {
            if (!obj.IsSpawned()) { _bgNextObjective = 0; return; }
            result = MoveWithin(obj, 3, objective.Label);
            if (result == SIBehaviorResult.Success && Now >= _bgNextInteraction && !me.IsNonMeleeSpellCast(false, skipAutorepeat: true))
            {
                CancelTravel(); Dismount(); StandUp(); me.SetSelection(obj.GetGUID());
                me.SetFacingToObject(obj); _bgNextInteraction = Now + 1500;
                me.GetSession().UseObjectForSI(obj);
                _bgNextObjective = 0;
            }
        }
        else if (target is Unit unit && objective.Action == BattlegroundObjectiveAction.Attack && CanFight(unit))
        { BeginEncounter(unit); if (unit is Player) _routine.EngagedCombat(); TickEncounter(); return; }
        else
        {
            var destination = target?.GetPosition() ?? objective.Position;
            if(objective.Action==BattlegroundObjectiveAction.Escort)
            {
                if(_bgEscortTarget!=objective.Target){_bgEscortTarget=objective.Target;_bgEscortApproaching=false;}
                _bgEscortApproaching=Game.AI.SIMovement.SIMovementRoute.FollowNeedsMovement(me.GetExactDist(destination),_bgEscortApproaching);
                if(!_bgEscortApproaching){CancelTravel();Status="Protecting the flag carrier";return;}
            }
            else {_bgEscortTarget=ObjectGuid.Empty;_bgEscortApproaching=false;}
            // Moving past a spot the SI planner rejected (see the Failure fallback below): let that run finish.
            if (Now < _bgDirectUntil && me.IsMoving()) { Status = objective.Label; return; }
            // Leave the flag room through this bot's own exit first (half the team uses each).
            var exit = bg.ExitWaypointForSI(me, destination);
            // An exit once reached counts until the bot is well clear of that base; forgetting it at the gate made
            // bots bounce between the two gates whenever their route passed near the flag room again.
            if (_bgExitReached != null && me.GetExactDist2d(_bgExitReached) > 100) _bgExitReached = null;
            if (exit != null && _bgExitReached != null && _bgExitReached.GetExactDist2d(exit) < 120) exit = null;
            if (exit != null)
            {
                if (me.GetExactDist2d(exit) <= 5) _bgExitReached = exit;
                else
                {
                    var leg = Travel(exit, objective.Label, arrival: 3, destinationTolerance: 3, heightKnown: false, rememberFailure: false);
                    if (leg != SIBehaviorResult.Failure) return;
                    _bgExitReached = exit;   // unreachable: take the direct route instead
                }
            }
            float arrival = objective.Action == BattlegroundObjectiveAction.Escort ? 3 : objective.Action == BattlegroundObjectiveAction.Defend ? 6 : 1.5f;
            result = Travel(destination, objective.Label, arrival: arrival, destinationTolerance: 2, rememberFailure: false);
            // A leg that keeps re-planning from a spot the mesh rejects never fails on its own: count it as failed
            // once the bot has not moved for a few seconds, so the core-path fallback below takes over.
            if (result == SIBehaviorResult.Running && BattlegroundLegStuck()) { result = SIBehaviorResult.Failure; CancelTravel(); }
            if (result == SIBehaviorResult.Success && objective.Action is BattlegroundObjectiveAction.Escort or BattlegroundObjectiveAction.Defend)
                Status = "Holding position: " + objective.Label;
        }
        if (result == SIBehaviorResult.Failure && objective.Action != BattlegroundObjectiveAction.Interact && Now >= _bgDirectUntil + 2000)
        {
            // The SI planner's strict step/slope checks reject some WSG door thresholds and ramp lips, which left
            // flag carriers standing at their gate. Fall back to the core's mmaps path for a few seconds, then
            // plan normally again from the far side.
            var destination = (objective.Target.IsEmpty() ? null : Global.ObjAccessor.GetWorldObject(me, objective.Target))?.GetPosition() ?? objective.Position;
            CancelTravel();
            me.GetMotionMaster().MovePoint(0, destination, generatePath: true);
            _bgDirectUntil = Now + 5000;
            Report("navigation_fallback", $"SI route rejected; using the core path toward {objective.Label}");
            return;
        }
        if (result == SIBehaviorResult.Failure)
        {
            _bgObjectiveRetryAt[objective.Key] = Now + 20000;
            _bgObjective = null; _bgNextObjective = 0; CancelTravel();
        }
    }
}
