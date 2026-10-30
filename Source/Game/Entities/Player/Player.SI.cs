using Framework.Configuration;
using Game.AI;
using Game.Chat;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Game.Entities
{
    public partial class Player
    {
        public SIPlayer Controller { get; private set; }
        public string NavigationMode { get; internal set; } = "Not requested";
        public bool NavigationBlocked { get; internal set; }
        public bool IsSelfSIControlled => Controller != null && !GetSession().IsServerControlled;
        readonly ConcurrentQueue<bool?> _selfSIRequests = new();

        // Chat commands can arrive outside the owning map update. Attach/detach
        // the AI only at the start of Player.Update, before Unit locks its AI.
        internal void RequestSelfSIControl(bool? enabled)
        {
            if (GetSession().IsServerControlled)
                throw new InvalidOperationException(".si me requires a player connected to a client.");
            _selfSIRequests.Enqueue(enabled);
        }

        internal void InitializeSIController()
        {
            if (Controller != null) return;
            if (!_questHistoryLoaded) LoadQuestHistory();
            Controller = new SIPlayer(this);
            SetAI(Controller);
            Controller.Start();
        }

        void UpdateSelfSIControl()
        {
            if (GetSession().IsServerControlled) return;
            while (_selfSIRequests.TryDequeue(out bool? request))
            {
                bool enable = request ?? !IsSelfSIControlled;
                if (!enable)
                {
                    ReleaseSelfSIControl();
                    NotifySelfSI("SI control off. You control your character.");
                    continue;
                }
                if (IsSelfSIControlled)
                {
                    NotifySelfSI("SI already controls your character. Use .si me off to take control back.");
                    continue;
                }
                if (!ConfigMgr.GetDefaultValue("SI.Enabled", true) || GetSession().IsLogingOut() || GetSession().PlayerDisconnected() ||
                    IsBeingTeleported() || IsCharmed() || GetVehicle() != null || GetUnitBeingMoved() != this || GetTopAI() != null)
                {
                    NotifySelfSI("Cannot start SI control while SI is disabled, disconnecting, transferring, or another unit/AI controls you.");
                    continue;
                }
                try
                {
                    InitializeSIController();
                    KeepSelfSIActive();
                    CastStop(); StopMoving();
                    SetClientControl(this, false);
                    NotifySelfSI("SI now controls your character and will quest automatically. Use .si me off to take control back.");
                }
                catch (Exception error)
                {
                    ReleaseSelfSIControl();
                    NotifySelfSI("Could not start SI control: " + error.Message);
                }
            }
            if (IsSelfSIControlled && (!ConfigMgr.GetDefaultValue("SI.Enabled", true) || GetSession().IsLogingOut() ||
                GetSession().PlayerDisconnected() && !IsBeingTeleported() || IsCharmed() || GetVehicle() != null || GetUnitBeingMoved() != this))
            {
                ReleaseSelfSIControl();
                NotifySelfSI("SI control stopped. Use .si me to start it again when your character is available.");
            }
            KeepSelfSIActive();
        }

        internal void KeepSelfSIActive()
        {
            if(!IsSelfSIControlled)return;
            GetSession().ResetTimeOutTime(true);
            if(!IsAFK())return;
            ToggleAFK();
            GetGuild()?.SendEventAwayChanged(GetGUID(),false,IsDND());
        }

        void NotifySelfSI(string message) => new CommandHandler(GetSession()).SendSysMessage(message);

        internal void ReleaseSelfSIControl()
        {
            if (!IsSelfSIControlled) return;
            try { DisposeSIController(); }
            finally
            {
                GetSession().ResetTimeOutTime(true);
                if (IsInWorld && !IsCharmed() && GetVehicle() == null && GetUnitBeingMoved() == this)
                    SetClientControl(this, true);
            }
        }

        void DisposeSIController()
        {
            var controller = Controller;
            if (controller == null) return;
            try { controller.Dispose(); }
            finally
            {
                // Preserve any pre-existing or subsequently installed charm AI.
                var retained = i_AIs.Where(ai => !ReferenceEquals(ai, controller)).Reverse().ToArray();
                i_AIs.Clear();
                foreach (var ai in retained) PushAI(ai);
                RefreshAI();
                Controller = null;
                NavigationMode = "Not requested"; NavigationBlocked = false;
            }
        }
    }
}
