// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System.Collections.Generic;

namespace Game.Networking.Packets
{
    public class InitializeFactions : ServerPacket
    {
        public InitializeFactions() : base(ServerOpcodes.InitializeFactions, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Factions.Count);
            _worldPacket.WriteInt32(Bonuses.Count);

            foreach (FactionData faction in Factions)
                faction.Write(_worldPacket);

            foreach (FactionBonusData bonus in Bonuses)
                bonus.Write(_worldPacket);

        }

        public List<FactionData> Factions = new();
        public List<FactionBonusData> Bonuses = new();
    }

    public struct FactionData
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(FactionID);
            data.WriteUInt16((ushort)Flags);
            data.WriteInt32(Standing);
        }

        public int FactionID;
        public ReputationFlags Flags;
        public int Standing;
    }

    public struct FactionBonusData
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(FactionID);
            data.WriteBit(FactionHasBonus);
            data.FlushBits();
        }

        public int FactionID;
        public bool FactionHasBonus;
    }

    class RequestForcedReactions : ClientPacket
    {
        public RequestForcedReactions(WorldPacket packet) : base(packet) { }

        public override void Read() { }
    }

    class SetForcedReactions : ServerPacket
    {
        public SetForcedReactions() : base(ServerOpcodes.SetForcedReactions) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Reactions.Count);
            foreach (ForcedReaction reaction in Reactions)
                reaction.Write(_worldPacket);
        }

        public List<ForcedReaction> Reactions = new();
    }

    class SetFactionStanding : ServerPacket
    {
        public SetFactionStanding() : base(ServerOpcodes.SetFactionStanding, ConnectionType.Instance) { }

        public override void Write()
        {
            _worldPacket.WriteFloat(BonusFromAchievementSystem);

            _worldPacket.WriteInt32(Faction.Count);
            foreach (FactionStandingData factionStanding in Faction)
                factionStanding.Write(_worldPacket);

            _worldPacket.WriteBit(ShowVisual);
            _worldPacket.FlushBits();
        }

        public float BonusFromAchievementSystem;
        public List<FactionStandingData> Faction = new();
        public bool ShowVisual;
    }

    struct ForcedReaction
    {
        public void Write(WorldPacket data)
        {
            data.WriteInt32(Faction);
            data.WriteInt32(Reaction);
        }

        public int Faction;
        public int Reaction;
    }

    struct FactionStandingData
    {
        public FactionStandingData(int index, int standing)
        {
            Index = index;
            Change = 0;
            Standing = standing;
        }

        public void Write(WorldPacket data)
        {
            data.WriteInt32(Index);
            data.WriteInt32(Change);
            data.WriteInt32(Standing);
            data.WriteInt32(0);
        }

        int Index;
        int Change;
        int Standing;
    }
}
