// Trait packet layout and delta semantics follow the local TrinityCore forever reference.
using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Systems.Legacy;
using Game.Networking;
using Game.Networking.Packets;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    public partial class WorldSession
    {
        [WorldPacketHandler(ClientOpcodes.TraitsCommitConfig)]
        void HandleTraitsCommitConfig(TraitsCommitConfig packet)
        {
            int id = packet.Config.ID;
            void Fail(TalentLearnResult reason) => SendPacket(new TraitConfigCommitFailed(id, 0, (int)reason));
            TraitConfig existing = _player.GetTraitConfig(id);
            if (existing == null) { Fail(TalentLearnResult.FailedUnknown); return; }
            if (packet.Config.SubTrees.Count != 0) { Fail(TalentLearnResult.FailedUnknown); return; }
            if (_player.IsInCombat()) { Fail(TalentLearnResult.FailedAffectingCombat); return; }
            if (!_player.IsAlive() || _player.IsNonMeleeSpellCast(false)) { Fail(TalentLearnResult.FailedCantDoThatRightNow); return; }
            if (_player.GetBattleground()?.GetStatus() == BattlegroundStatus.InProgress) { Fail(TalentLearnResult.InPvpMatch); return; }

            // Metadata and granted ranks belong to the server. Client entries are rank deltas, not a replacement inventory.
            TraitConfigPacket state = new(existing);
            HashSet<int> touched = new();
            foreach (TraitEntryPacket change in packet.Config.Entries)
            {
                if (change.Rank < 0 || change.GrantedRanks < 0 || change.BonusRanks != 0 || !touched.Add(change.TraitNodeID)) { Fail(TalentLearnResult.FailedUnknown); return; }
                TraitEntryPacket old = state.Entries.Find(x => x.TraitNodeID == change.TraitNodeID && x.TraitNodeEntryID == change.TraitNodeEntryID);
                if (change.GrantedRanks != (old?.GrantedRanks ?? 0)) { Fail(TalentLearnResult.FailedUnknown); return; }
                if (change.Rank < (old?.Rank ?? 0))
                {
                    TraitNodeRecord node = CliDB.TraitNodeStorage.LookupByKey(change.TraitNodeID);
                    if (node == null || CliDB.TraitTreeStorage.LookupByKey(node.TraitTreeID)?.HasFlag(TraitTreeFlag.CannotRefund) != false)
                    { Fail(TalentLearnResult.FailedCantRemoveTalent); return; }
                    TraitDefinitionRecord definition = CliDB.TraitDefinitionStorage.LookupByKey(CliDB.TraitNodeEntryStorage.LookupByKey(change.TraitNodeEntryID)?.TraitDefinitionID ?? 0);
                    if (definition == null || (definition.SpellID != 0 && _player.GetSpellHistory().HasCooldown(definition.SpellID)))
                    { Fail(TalentLearnResult.FailedCantRemoveTalent); return; }
                }
                if (old != null) old.Rank = change.Rank;
                else state.Entries.Add(new TraitEntryPacket { TraitNodeID = change.TraitNodeID, TraitNodeEntryID = change.TraitNodeEntryID, Rank = change.Rank });
            }
            state.Entries.RemoveAll(entry => entry.Rank == 0 && entry.GrantedRanks == 0);
            if (!LegacyValidation.EntriesBelongToConfig(state)) { Fail(TalentLearnResult.FailedUnknown); return; }
            TalentLearnResult validation = TraitMgr.ValidateConfig(state, _player);
            if (validation != TalentLearnResult.LearnOk)
            {
                Log.outInfo(LogFilter.Player, $"Trait config {id} rejected for {_player.GetGUID()}: {validation}.");
                Fail(validation); return;
            }
            if (LegacyManager.IsLegacy(state))
            {
                // A Legacy commit must never edit a second config through SavedConfigID.
                if (!_player.CommitLegacyTraits(state))
                    Fail(TalentLearnResult.FailedCantDoThatRightNow);
                return;
            }
            _player.UpdateTraitConfig(state, 0, false);
        }
    }
}
