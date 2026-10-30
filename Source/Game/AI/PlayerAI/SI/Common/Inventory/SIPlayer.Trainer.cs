using Framework.Constants;
using Game.Entities;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        int _trainingQueryLevel;
        Task<Dictionary<int, HashSet<int>>> _trainingQuery;
        Dictionary<int, HashSet<int>> _trainingOffers;
        long _nextTrainingQuery;
        long _nextTrainingScan;
        long _trainingScanMoney = -1;
        ulong _trainingScanRevision = ulong.MaxValue;
        int[] _trainingEntries = Array.Empty<int>();
        SITask _trainingReturnTask;

        void CheckTrainingAvailability()
        {
            int level = me.GetLevel();
            // Login and every level change query actual database offers, including odd levels.
            if (_trainingQuery != null)
            {
                if (!_trainingQuery.IsCompleted) return;
                if (_trainingQuery.IsCompletedSuccessfully && _trainingQueryLevel == level)
                { _trainingOffers = _trainingQuery.Result; _nextTrainingScan = 0; _trainingScanRevision = ulong.MaxValue; _trainerTravelNeedsReplan=true; }
                else
                {
                    if (_trainingQuery.Exception != null) Report("trainer_query_failed", _trainingQuery.Exception.GetBaseException().Message);
                    _trainingQueryLevel = 0; _nextTrainingQuery = Now + 10000;
                }
                _trainingQuery = null;
            }
            if (!SITrainerPolicy.NeedsQuery(level, _trainingQueryLevel) || Now < _nextTrainingQuery) return;
            _trainingOffers = null;
            _trainingQuery = SITrainerQueries.TryQuery(me.GetClass(), level);
            if (_trainingQuery != null) _trainingQueryLevel = level;
            else _nextTrainingQuery = Now + 500 + (long)(me.GetGUID().GetCounter() % 1000);
        }

        bool HasSomethingToLearn(int trainerEntry)
        {
            if (_trainingOffers == null || _trainingQueryLevel != me.GetLevel()) return false;
            if (Global.ObjectMgr.GetCreatureTemplate(trainerEntry) == null) return false;
            float discount = PriceDiscount(trainerEntry);
            return SITrainerCatalog.ForCreature(trainerEntry, me.GetClass())
                .Any(trainer => _trainingOffers.TryGetValue(trainer.Id, out var spells) && trainer.HasAffordableLesson(me, discount, spells));
        }

        bool TrainAt(Creature trainer)
        {
            if (trainer == null || me.GetNPCIfCanInteractWith(trainer.GetGUID(), NPCFlags1.Trainer, NPCFlags2.None) == null) return false;
            int learned = me.GetSession().TrainForSI(trainer);
            if (learned > 0)
            {
                _spellCount = -1; NoteGameplayProgress();
                Report("trainer_learned", $"Learned {learned} spell(s)");
            }
            Status = learned > 0 ? $"Learned {learned} new spell(s) from {trainer.GetName()}" : $"Nothing affordable to learn from {trainer.GetName()}";
            return !HasSomethingToLearn(trainer.GetEntry());
        }

        SITask TrainerTask(SITrainerTrip trip) => new() { Kind = SITaskKind.Train, Destination = trip.Trainer, Creatures = [trip.Trainer.Entry],
            Label = (trip.Boarding.HasValue ? "Fly to " : "Visit ") + CreatureName(trip.Trainer.Entry) + " for confirmed available class spells" };

        void RefreshTrainingEntries()
        {
            if(_trainingScanMoney==me.GetMoney()&&_trainingScanRevision==me.SpellBook.Revision)return;
            _trainingScanMoney=me.GetMoney();_trainingScanRevision=me.SpellBook.Revision;
            _trainingEntries=Knowledge.ClassTrainers(me.GetClass()).Where(HasSomethingToLearn).ToArray();
        }

        bool TickTrainerBehavior()
        {
            if (_task != null && _task.Kind != SITaskKind.Train && SIServiceSelection.OwnsTrip(_task.Kind)) return false;
            if (_task?.Kind == SITaskKind.Train)
            {
                _task.Resume(Now);
                if(_trainingOffers==null||_trainingQueryLevel!=me.GetLevel())
                {_task.Suspend(Now);Status="Checking available lessons for the current level";return true;}
                if(_trainerTravelNeedsReplan&&!_trainerFlightTaken)
                {
                    _trainerTravelNeedsReplan=false;RefreshTrainingEntries();
                    var improved=ChooseTrainerTrip();
                    if(improved!=null){_trainerTrip=improved;SetTask(TrainerTask(improved));}
                }
                // A trainer commute must produce progress, not suspend its task
                // deadline forever while a travel controller keeps running.
                float remaining=TaskDistance(_task);
                if(remaining<_task.ClosestApproach-2||_trainerProgressMap!=me.GetMapId()||
                    System.Numerics.Vector3.DistanceSquared(Here,_trainerProgressPosition)>=4)
                {
                    _task.ClosestApproach=remaining;_task.LastProgressAt=Now;
                    _trainerProgressMap=me.GetMapId();_trainerProgressPosition=Here;
                }
                if(Now-_task.LastProgressAt>60000)
                {
                    FailInteractionTask(_task,"Trainer trip made no progress for 60 seconds.");
                    var previous=_trainingReturnTask;_trainingReturnTask=null;
                    _trainerTrip=null;SetTask(previous);_nextTrainingScan=Now+30000;return true;
                }
                if(!TaskDone(_task))
                {
                    var transit=TravelToTrainerByFlight();
                    if(transit==SIBehaviorResult.Running)return true;
                    if(transit==SIBehaviorResult.Failure)FailInteractionTask(_task,"Could not take the planned trainer flight; choosing another trip later.");
                    else if(VisitNpc(_task)==SIBehaviorResult.Running)return true;
                }
                SITask resume = _trainingReturnTask; _trainingReturnTask = null;
                _trainerTrip=null;
                SetTask(resume); _nextReplan = 0; _nextTrainingScan = Now + 30000;
                return true;
            }
            _trainerTrip=null;
            // Local quest interactions precede starting a new, distant trainer trip.
            if(CommittedToJourney())return false;
            if(!PartyFollowing && (_task?.Kind is SITaskKind.PickUp or SITaskKind.TurnIn) && !TaskDone(_task))return false;
            if(!PartyFollowing && CommittedToObjective())return false;
            if (!SIQuestKnowledge.IsBuilt || Now < _nextTrainingScan || _trainingOffers == null || _trainingQueryLevel != me.GetLevel()) return false;
            _nextTrainingScan = Now + 15000;
            // Known/affordable class lessons cannot change while the spellbook and funds are unchanged.
            // Do not rescan every class trainer for hundreds of idle bots every fifteen seconds.
            RefreshTrainingEntries();
            if (_trainingEntries.Length == 0) return false;
            var trip=ChooseTrainerTrip();
            if(trip==null)return false;
            var task = TrainerTask(trip);
            _trainingReturnTask = _task != null && !TaskDone(_task) ? _task : null;
            _trainingReturnTask?.Suspend(Now);
            _trainerTrip=trip;_trainerFlightTaken=false;_trainerTravelNeedsReplan=false;
            _trainerProgressMap=me.GetMapId();_trainerProgressPosition=Here;
            SetTask(task);
            return true;
        }
    }
}
