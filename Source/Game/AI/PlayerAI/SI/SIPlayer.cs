using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Game.AI
{
    // Server-side port of the QuestBot/LevelBot lifecycle. Start once; retain work across updates.
    public sealed partial class SIPlayer : PlayerAI, IDisposable
    {
        readonly ConcurrentQueue<(string Action, ObjectGuid Target)> _orders = new();
        SISettings _settings = SISettings.Load();
        TimeSpan _clock, _nextThink;
        long _nextSettingsCheck;
        bool _settingsStopped;
        public SISettings EffectiveSettings=>_settings;
        ObjectGuid _target;
        ObjectGuid _previousTarget;
        string _goal = "quest";
        string _previousGoal = "quest";
        bool _recovering;
        bool _operatorControlled;
        public string Status { get; private set; } = "Questing";

        public SIPlayer(Player player) : base(player)
        {
            _nextThink = TimeSpan.FromMilliseconds(player.GetGUID().GetCounter() % 250);
            // Database knowledge is shared by all bots; build it off the map thread.
            if (!SIQuestKnowledge.IsBuilt) _ = Task.Run(() => SIQuestKnowledge.Instance);
        }

        // Commands enqueue values. All gameplay mutation happens inside UpdateAI.
        public void Order(string action, ObjectGuid target = default) => _orders.Enqueue((action, target));

        void ApplyOrders()
        {
            while (_orders.TryDequeue(out var order))
            {
                if (order.Action == "save")
                {
                    me.SaveToDB();
                    continue;
                }
                if (order.Action == "teleport")
                {
                    Player destination = Global.ObjAccessor.FindConnectedPlayer(order.Target);
                    if (destination != null)
                    {
                        me.RequestOperatorTeleport(new WorldLocation(destination),destination.GetInstanceId(),destination.GetGUID());
                    }
                    continue;
                }
                _operatorControlled = order.Action is not ("quest" or "grind" or "resume" or "gather" or "professions" or "social");
                if (order.Action is "gather" or "professions" or "social") ResetProfessionGoal();
                // pause and duel hold the bot in place; resume returns it to what it was doing before either
                if (order.Action is "pause" or "duel" && _goal is not ("pause" or "duel"))
                {
                    _previousGoal = _goal;
                    _previousTarget = _target;
                }
                if (order.Action == "duel") ResetDuelGoal();
                if (order.Action == "resume")
                {
                    if (_goal is not ("pause" or "duel"))
                        continue;
                    _goal = _previousGoal;
                    _target = _previousTarget;
                }
                else if (order.Action != "pause")
                {
                    _goal = order.Action;
                    _target = order.Target;
                    if (_goal == "rest") _recovering = true;
                }
                else
                    _goal = "pause";
                Stop();
                if (order.Action is not ("pause" or "resume")) { ReleaseJourney();ResetPriorityTurnIn();SetTask(null); _suspendedTask = null; _nextReplan = 0; ResetQuestingArea(); }
            }
        }

        public override void UpdateAI(TimeSpan diff)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdateBrain(diff); }
            finally
            {
                TrackActivity();
                TrackBehaviorState();
                ReplyPlayerInfo();
                double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Game.Maps.MapTickProfile.BotAiMs += elapsed;
                if (elapsed >= 25 && Now >= _nextSlowUpdateReport)
                {
                    _nextSlowUpdateReport = Now + 5000;
                    Report("slow_update", $"{elapsed:F1} ms; behavior {Behavior}; navigation {NavigationMode}; task {Plan}");
                }
            }
        }
        long _nextSlowUpdateReport;
        bool Observed() => me.IsInWorld && SIMapObjectIndex.For(me.GetMap()).Observed(me, 200);
        bool _followingTaxi;

        void UpdateBrain(TimeSpan diff)
        {
            _clock += diff;
            _movementTickSeconds = diff.TotalSeconds;
            if(Now>=_nextSettingsCheck)
            {
                _nextSettingsCheck=Now+5000;
                var current=SISettings.Load();
                if(current!=_settings){_settings=current;_nextWorldSnapshot=0;_nextNearbyQuestScan=0;_nextReplan=0;}
            }
            if(!_settings.Enabled)
            {
                if(!_settingsStopped){Stop();_settingsStopped=true;}
                Behavior="Disabled";Status="SI.Enabled = 0";return;
            }
            _settingsStopped=false;
            ApplyOrders();
            if(me.HasPendingOperatorTeleport)return;
            StopUnsafeHealthFunnel();
            StopInvalidRangedAttacks();
            StopObstructedCombatCasts();
            me.GetSession().AcceptPartyInviteForSI();
            if (TickLootAnimation()) return;
            if (me.IsInWorld && !me.IsBeingTeleported() && me.IsAlive())
            {
                bool swimming=SwimmingSurface(Here);
                me.SetSwim(swimming);
                _travelMount.ObserveMounted(Now,me.IsMounted(),swimming);
                if(swimming&&me.IsMounted())me.RemoveAurasByType(AuraType.Mounted);
                else if(me.IsMounted())ValidateTravelMount();
                me.RefreshSISplineSpeed();
            }
            MaintainNavigation();
            if (_clock < _nextThink) return;
            // Nobody sees a bot far from every real player: it decides half as often, which
            // halves its share of the map update without changing what it does.
            _nextThink = _clock + TimeSpan.FromMilliseconds(Observed() ? 250 : 500);
            ReplyToWhispers();
            RestoreTravelPet();

            TickQuestReservation();
            if (me.IsBeingTeleported() || me.IsCharmed()) { _preparation.Pause(Now); _encounter.Pause(Now); _task?.Suspend(Now); return; }
            // Native taxi movement owns the player until landing. Quest/trainer
            // replans must not cancel its itinerary or time out its saved task.
            if(me.IsInFlight())
            {
                _followingTaxi=true;
                Behavior="Flight";Status=_journeyFlight==null?"Following native flight path":"Flying "+JourneyFlightDescription+"; then "+_journeyFlightReason;
                _task?.Suspend(Now);_preparation.Pause(Now);_encounter.Pause(Now);return;
            }
            if(_followingTaxi)
            {
                // Landing is expected relocation, not a summon that should
                // discard the trainer itinerary and its suspended quest task.
                _followingTaxi=false;_localQuestMap=me.GetMapId();
                _lastQuestPosition=Here;_lastQuestPositionAt=Now;
            }
            if (!_started) Start();
            CheckTrainingAvailability();
            RefreshWorldSnapshot();
            RefreshPartyContext();
            if(TickDuel())return;
            if (_goal == "pause") { Behavior = "Pause"; Status = "Paused"; _preparation.Pause(Now); return; }
            ReviewLocalQuestContext();
            if (!me.IsAlive())
            {
                Behavior = "Death"; _preparation.Pause(Now); _task?.Suspend(Now);
                EndEncounter(false); RecoverCorpse(); return;
            }
            if (_deathAt != 0 || _ghostStartedAt != 0 || Recovery is "CorpseRun" or "AwaitingReclaim" or "AwaitingRelease" or "Blocked" or "SpiritHealer") OnResurrected();
            RefreshSpellBook();
            SelectAmmunition();
            if (me.HasUnitState(UnitState.LostControl | UnitState.Stunned | UnitState.Confused | UnitState.Fleeing))
            {
                Behavior = "Control"; _preparation.Pause(Now); _task?.Suspend(Now);
                _encounter.Pause(Now);
                ClassReaction(null); Status = "Under a loss-of-control effect"; return;
            }
            EmergencySurvival();
            if (_goal == "flee")
            {
                Behavior = "Escape"; _preparation.Pause(Now); _task?.Suspend(Now);
                EndEncounter(false); if (!ClassReaction(null)) Flee(); return;
            }
            if(TickPriorityTurnIn())return;
            if(TickJourneyFlight())return;
            if (ContinuePastTrivialAttackers()) return;
            if (!_auctionMailDestination.HasValue && (!PartyFollowing || PartyErrand) && ContinueLongJourney()) return;
            // Death/combat can interrupt preparation. Neither releases the current quest order.
            if (TickCombatBehavior() != SIBehaviorResult.Failure)
            {
                Behavior = "Combat"; _preparation.Pause(Now); _task?.Suspend(Now); return;
            }
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return;
            if (me.GetClass() == Class.Warlock && TryClassAction(SICombatActionStage.Heal))
            {
                Behavior = "Pet recovery"; _task?.Suspend(Now); return;
            }
            ReviewQuestLevels();
            if (!_preparation.Complete)
            {
                Behavior = "Preparation"; _task?.Suspend(Now);
                _preparation.Resume(Now); _preparation.Tick(Prepare, Now); return;
            }
            // A full bag must not repeatedly choose loot/pulls ahead of an inventory trip.
            // Once selected, bank/AH travel remains committed after the first slot is freed.
            if (WorkingGoal && (StoragePressure() ||
                _task?.Kind == SITaskKind.Vendor && _task.VendorPurpose is SIVendorPurpose.Bank or SIVendorPurpose.Auction))
            {
                RecoverIdleTask();
                if (TickVendorBehavior()) { Behavior = "Vendor"; return; }
            }
            if (TryLoot()) { Behavior = "Loot"; _task?.Suspend(Now); return; }
            if (TickProfessionOpportunities()) { Behavior = "Gather"; _task?.Suspend(Now); return; }
            if (Recuperate() || Rest() || Survive()) { Behavior = "Recovery"; _task?.Suspend(Now); return; }
            // Finish the selected service before planning a different trainer,
            // discovering a flight path, or returning a social bot to its city.
            if (WorkingGoal && OwnedServiceTrip)
            {
                if (_task.Kind == SITaskKind.ProfessionTrain) { TickProfessionTrainingTask(); Behavior = "Trainer"; return; }
                if (_task.Kind == SITaskKind.Train) { TickTrainerBehavior(); Behavior = "Trainer"; return; }
                TickVendorBehavior(); Behavior = "Vendor"; return;
            }
            if (TickPartyCohesion(regroupOnly: true)) return;
            if(TickFlightPaths()){Behavior="FlightPaths";return;}
            // Maintenance is periodic, not a restart of login preparation or the quest order.
            if (Now >= _nextMaintenance)
            {
                if (Buff()) { _nextMaintenance = Now + 1600; Behavior = "Buffs"; _task?.Suspend(Now); return; }
                _nextMaintenance = Now + 5000;
                EquipUpgrades(); StartQuestsFromItems();
            }
            RecoverIdleTask();
            if (WorkingGoal && TickAuctionMail()) { Behavior = "Mailbox"; return; }
            if (WorkingGoal && TickVendorBehavior()) { Behavior = "Vendor"; return; }
            if (!PartyFollowing && _goal=="quest"&&SIQuestKnowledge.IsBuilt) NearbyQuestInteraction();
            if (WorkingGoal && TickTrainerBehavior()) { Behavior = "Trainer"; return; }
            if (WorkingGoal && TickFirstAidUpkeep()) return;
            if (TickPartyCohesion()) return;
            Behavior = _goal switch { "grind" => "LevelBot", "duel" => "Dueling", "gather" => "GatherBot", "professions" => "Professions",
                "social" => "Social", _ => "QuestBot" };
            TickGoalBehavior();
        }

        // Goals that look after themselves: vendors, bank, auction mail and trainers on the way.
        bool WorkingGoal => _goal is "quest" or "grind" or "gather" or "professions" or "social";

        // World PvP: an opposite-faction player the core lets us attack (PvP-flagged on a normal realm, any on a
        // PvP realm, never in a sanctuary), close in level and in line of sight.
        Player EnemyPlayer()
        {
            Player best = NearestPlayer(35, player => player.IsAlive() && !player.IsGameMaster() && !player.IsInFlight() &&
                Math.Abs(player.GetLevel() - me.GetLevel()) <= 5 && me.IsValidAttackTarget(player) && me.IsWithinLOSInMap(player));
            if (best != null && _combatTarget != best.GetGUID()) Report("pvp_target", $"Attacking {best.GetName()}");
            return best;
        }

        Player NearestPlayer(float radius, Func<Player, bool> eligible)
        {
            Player best = null;
            float bestDistance = float.MaxValue;
            foreach (Player player in me.GetPlayerListInGrid(radius))
            {
                if (player == me || !eligible(player)) continue;
                float distance = me.GetDistance(player);
                if (distance < bestDistance) { bestDistance = distance; best = player; }
            }
            return best;
        }

        void Follow()
        {
            Unit leader = Resolve(_target);
            if (leader != null && leader.IsAlive())
            {
                Travel(leader.GetPosition(), "Following", 4);
                Status = "Following";
            }
            else
            {
                Stop();
                Status = "Follow target unavailable";
            }
        }

        Unit Resolve(ObjectGuid guid)
        {
            if (guid.IsEmpty())
                return null;
            Unit target = Global.ObjAccessor.GetUnit(me, guid);
            return target != null && target.IsInWorld && target.GetMap() == me.GetMap()
                && me.CanSeeOrDetect(target, distanceCheck: true) ? target : null;
        }

        // Retry and memory tables keyed by expiry time; Dictionary allows removal while enumerating.
        void PruneExpired<TKey>(System.Collections.Generic.Dictionary<TKey, long> expiries)
        {
            foreach (var (key, until) in expiries)
                if (until <= Now) expiries.Remove(key);
        }

        bool CanFight(Unit target) => target != null && target.IsInWorld && target.IsAlive() && target.GetMap() == me.GetMap()
            && target.ToCreature()?.IsEvadingAttacks() != true && me.IsValidAttackTarget(target)
            && me.CanSeeOrDetect(target, distanceCheck: true) && me.GetExactDist(target) <= 90;

        void Stop()
        {
            CloseLootAnimation();
            _task?.Suspend(Now);
            CancelTravel();
            EndEncounter(false);
            me.CastStop();
        }

        static readonly SIBufferedLog Diagnostics = new(message => Log.outWarn(LogFilter.SI, message));
        readonly System.Collections.Generic.Dictionary<string, long> _nextDiagnostic = new();
        void Report(string type, [InterpolatedStringHandlerArgument("type")] ref SIDiagnosticMessage summary)
        {
            if (SIDiagnosticPolicy.Important(type)) Report(type, summary.GetText());
        }
        void Report(string type, string summary)
        {
            if (!SIDiagnosticPolicy.Important(type) || _nextDiagnostic.TryGetValue(type, out long next) && Now < next) return;
            _nextDiagnostic[type] = Now + 15000;
            Diagnostics.Publish($"SIPlayer {me.GetName()} [{type}] {summary}");
        }

        public void Dispose() { Stop(); ReleaseQuestTarget(); }
    }
}

