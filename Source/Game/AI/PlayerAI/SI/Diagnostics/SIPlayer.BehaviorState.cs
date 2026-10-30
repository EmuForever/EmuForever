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
            if(_journeyFlight!=null)return (_journeyFlightTaken?"Finish flight: ":"Board flight: ")+JourneyFlightDescription+"; then "+_journeyFlightReason;
            if(_goal=="duel")return "Challenge nearby players of the same level or lower, then return to the duel area";
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
            if(_auctionMailDestination.HasValue || _awaitingPurchasedMail)return "Collect auction mail, then return to party or previous activity";
            if(PartyFollowing && !PartyErrand)return "Stay with party / return to its current area";
            return _task?.Kind switch
            {
                SITaskKind.PickUp=>"Accept local quest",
                SITaskKind.TurnIn=>"Turn in quest",
                SITaskKind.Objective=>"Execute quest objective",
                SITaskKind.Train=>"Reach trainer and learn affordable spells",
                SITaskKind.ProfessionTrain=>"Reach profession trainer and learn available recipes or ranks",
                SITaskKind.Vendor=>_task.VendorPurpose switch
                {
                    SIVendorPurpose.Bank=>"Deposit saved items, then resume task",
                    SIVendorPurpose.Auction=>"List surplus / buy needed items, then collect mail and return",
                    SIVendorPurpose.Supplies=>"Buy needed profession supplies from the selected vendor",
                    SIVendorPurpose.Withdraw=>"Withdraw needed profession materials from the bank",
                    _=>"Complete vendor interaction"
                },
                SITaskKind.Grind=>_goal=="quest"?$"Resume questing at level {_questGrindUntilLevel}":"Continue ordered grinding",
                _ when _goal=="professions"=>_professionSummary,
                _ when _goal=="gather"=>"Gather nearby nodes, skinnable beasts and cloth drops",
                _ when _goal=="social"=>_customerJob!=null?"Collect materials and meet the crafter":"Walk near the bank and auction house while waiting for work",
                _=>_goal=="quest"?$"Scan local quests within {QuestPickupRadius:0} yards":"Continue "+_goal
            };
        }
    }
}
