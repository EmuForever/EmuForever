using Framework.Constants;
namespace Game.AI;
public sealed partial class SIPlayer
{
    string _observedBehavior="Starting",_observedNavigation="Idle";
    public string LastBehaviorState {get;private set;}="Starting";
    void TrackBehaviorState()
    {
        if(_observedBehavior==Behavior&&_observedNavigation==NavigationMode)return;
        LastBehaviorState=_observedBehavior+" / "+_observedNavigation;
        _observedBehavior=Behavior;_observedNavigation=NavigationMode;
    }
    public string NextExpectedState
    {
        get
        {
            if(_siDuelActive)return "Finish duel, then resume previous activity";
            if(_goal=="pause")return "Wait for resume";
            if(!_flightMaster.IsEmpty())return "Learn flight path, then resume task";
            if(!me.IsAlive())return me.HasPlayerFlag(PlayerFlags.Ghost)?"Run to corpse / reclaim (no spirit healer)":"Release spirit";
            if(_recoveringMovement)return "Replan after recovery step";
            if(_mageRetreatUntil>Now)return "Reach casting range, then resume mage casts";
            if(_movementFailed)return "Skip failed route and select permitted quest work";
            if(_planning!=null)return "Follow path when navigation completes";
            if(_encounter.Active)return _encounter.Stage switch
            {
                SIEncounterStage.Approach=>"Pull / engage target",
                SIEncounterStage.Loot=>"Resume retained quest after loot",
                _=>"Continue combat, then loot or skip corpse"
            };
            if(!_preparation.Complete)return "Finish preparation, then resume task";
            return _task?.Kind switch
            {
                SITaskKind.PickUp=>"Accept local quest",
                SITaskKind.TurnIn=>"Turn in quest",
                SITaskKind.Objective=>"Execute quest objective",
                SITaskKind.Train=>"Reach trainer and learn affordable spells",
                SITaskKind.Vendor=>_task.VendorPurpose switch
                {
                    SIVendorPurpose.Bank=>"Deposit saved items, then resume task",
                    SIVendorPurpose.Auction=>"List surplus items, then resume task",
                    _=>"Complete vendor interaction"
                },
                SITaskKind.Grind=>_goal=="quest"?$"Resume questing at level {_questGrindUntilLevel}":"Continue ordered grinding",
                _=>_goal=="quest"?$"Scan local quests within {QuestPickupRadius:0} yards":"Continue "+_goal
            };
        }
    }
}
