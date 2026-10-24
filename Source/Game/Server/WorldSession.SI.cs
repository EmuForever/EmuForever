using Game.Entities;
using Game.Networking.Packets;
using Framework.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.DataStorage;

namespace Game
{
    public partial class WorldSession
    {
        public bool IsServerControlled { get; }
        bool _siRegistered;
        CharacterCreateInfo _siCreateInfo;
        Action<ResponseCodes, ObjectGuid> _siCreateCompleted;

        internal void CompleteSIActiveMoverInitialization()
        {
            if (!IsServerControlled || _player == null || !_player.IsInWorld || PlayerLoading() || _player.IsBeingTeleported() ||
                _player.HasPlayerLocalFlag(PlayerLocalFlags.OverrideTransportServerTime)) return;
            // There is no client clock: server-controlled movement uses the server's relative clock.
            CompleteActiveMoverInitialization(LoopTime.RelativeTime);
        }

        internal bool TrySelectSIAppearance(Race race, Class playerClass, Gender gender,
            List<ChrCustomizationChoice> previous, out List<ChrCustomizationChoice> selected)
        {
            selected = new();
            var options = Global.DB2Mgr.GetCustomiztionOptions(race, gender);
            foreach (var option in options.OrderBy(option => option.Id))
            {
                var optionRequirement = CliDB.ChrCustomizationReqStorage.LookupByKey(option.ChrCustomizationReqID);
                if (optionRequirement != null && !MeetsChrCustomizationReq(optionRequirement, race, playerClass, false, selected))
                    continue;
                var old = previous.FirstOrDefault(choice => choice.ChrCustomizationOptionID == option.Id);
                foreach (var choice in Global.DB2Mgr.GetCustomiztionChoices(option.Id)
                    .OrderBy(choice => choice.Id == old?.ChrCustomizationChoiceID ? 0 : 1).ThenBy(choice => choice.Id))
                {
                    List<ChrCustomizationChoice> trial = new(selected)
                    {
                        new() { ChrCustomizationOptionID = option.Id, ChrCustomizationChoiceID = choice.Id }
                    };
                    if (!ValidateAppearance(race, playerClass, gender, trial))
                        continue;
                    selected = trial;
                    break;
                }
            }
            return selected.Count > 0 && ValidateAppearance(race, playerClass, gender, selected);
        }

        internal void SetSICreation(CharacterCreateInfo info, Action<ResponseCodes, ObjectGuid> completed)
        {
            _siCreateInfo = info;
            _siCreateCompleted = completed;
        }

        void StartSICharacter()
        {
            if (_siCreateInfo != null)
                HandleCharCreate(new CreateCharacter(null) { CreateInfo = _siCreateInfo });
            else
                HandleContinuePlayerLogin();
        }

        void CompleteSICreation(ResponseCodes result, ObjectGuid guid)
        {
            _siCreateCompleted?.Invoke(result, guid);
            _siCreateCompleted = null;
            _siCreateInfo = null;
            KickPlayer("siPlayer character creation completed");
        }

        internal void RegisterSIAccount()
        {
            _siRegistered = true;
            Framework.Database.DB.Login.Execute($"UPDATE account SET online = 1 WHERE id = {unchecked((uint)GetAccountId())};");
        }

        internal void SetSICharacter(ObjectGuid guid)
        {
            if (!IsServerControlled || PlayerLoading() || GetPlayer() != null)
                throw new System.InvalidOperationException("Invalid siPlayer session state.");
            m_playerLoading = guid;
        }
    }
}
