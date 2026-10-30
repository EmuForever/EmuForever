using System;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    SITask _journeyTask;
    Vector3 _journeyTaskDestination;
    bool _hasCommittedJourney;
    string _journeyGoal;
    bool CommittedToJourney()=>_hasCommittedJourney&&_goal==_journeyGoal&&ReferenceEquals(_task,_journeyTask)&&WorkingGoal&&
        !_movementFailed&&(_task==null||!TaskDone(_task)&&!Blacklisted(_task.Key))&&
        Distance(_journeyTaskDestination)>Math.Max(120,_settings.TargetSearchRadius);
    void CommitJourney(Vector3 destination)
    {
        if(!WorkingGoal||_encounter.Active||Distance(destination)<=Math.Max(120,_settings.TargetSearchRadius))return;
        _hasCommittedJourney=true;_journeyGoal=_goal;_journeyTask=_task;_journeyTaskDestination=destination;
    }
    void ReleaseJourney(){_hasCommittedJourney=false;_journeyTask=null;ResetJourneyFlight();}
}
