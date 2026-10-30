using System.Numerics;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        long _lastMotionProgress, _lastActionProgress;
        Vector3 _activityPosition;
        bool _activityStarted;

        void TrackActivity()
        {
            if (!_activityStarted || Vector3.DistanceSquared(Here, _activityPosition) >= 4)
            {
                _activityStarted = true; _activityPosition = Here; _lastMotionProgress = Now;
            }
        }

        void RecoverIdleTask()
        {
            // Rest, casts, combat and corpse reclaim are owned waits. Task selection
            // alone is not progress: selecting the same impossible task cannot reset this.
            if (_operatorControlled || !me.IsAlive() || me.IsInCombat() || me.IsBeingTeleported() ||
                PartyFollowing && !PartyErrand ||
                !_preparation.Complete || _encounter.Active || _recovering || _resting ||
                me.IsNonMeleeSpellCast(false, skipAutorepeat: true) ||
                NavigationPending || Now - _lastMotionProgress < 45000 || Now - _lastActionProgress < 45000)
                return;
            if (_task != null)
            {
                _taskBlacklist[_task.Key] = Now + 60000;
                if (_task.Hotspot >= 0 && _task.Hotspot < _task.Hotspots.Count)
                    RememberSearchedArea(_task, _task.Hotspots[_task.Hotspot]);
            }
            Report("idle_recovery", $"No movement/action for 45 seconds; releasing {Plan}; previous state {Behavior}: {Status}");
            var resume = _task?.Kind == SITaskKind.Train ? _trainingReturnTask :
                _task?.Kind == SITaskKind.Vendor ? _suspendedTask : null;
            _trainingReturnTask = null; _suspendedTask = null;
            SetTask(resume != null && !TaskDone(resume) ? resume : null);
            _nextTrainingScan = Now + 30000; _nextVendorScan = Now + 1000;
            _nextReplan = 0; _nextNearbyQuestScan = 0;
            _lastActionProgress = _lastMotionProgress = Now;
        }
    }
}

