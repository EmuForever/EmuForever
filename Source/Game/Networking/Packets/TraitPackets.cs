// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.Networking.Packets
{
    class TraitsCommitConfig : ClientPacket
    {
        public TraitsCommitConfig(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Config.Read(_worldPacket);
            SavedConfigID = _worldPacket.ReadInt32();
            SavedLocalIdentifier = _worldPacket.ReadInt32();
        }
        
        public TraitConfigPacket Config = new();
        public int SavedConfigID;
        public int SavedLocalIdentifier;
    }

    class TraitConfigCommitFailed : ServerPacket
    {
        public TraitConfigCommitFailed(int configId = 0, int spellId = 0, int reason = 0) : base(ServerOpcodes.TraitConfigCommitFailed)
        {
            ConfigID = configId;
            SpellID = spellId;
            Reason = reason;
        }

        public override void Write()
        {
            _worldPacket.WriteInt32(ConfigID);
            _worldPacket.WriteInt32(SpellID);
            _worldPacket.WriteBits(Reason, 4);
            _worldPacket.FlushBits();
        }
        
        public int ConfigID;
        public int SpellID;
        public int Reason;
    }

    class ClassTalentsRequestNewConfig : ClientPacket
    {
        public ClassTalentsRequestNewConfig(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            Config.Read(_worldPacket);
        }
        
        public TraitConfigPacket Config = new();
    }

    class ClassTalentsRenameConfig : ClientPacket
    {
        public ClassTalentsRenameConfig(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            ConfigID = _worldPacket.ReadInt32();
            int nameLength = _worldPacket.ReadBits<int>(9);
            Name = _worldPacket.ReadString(nameLength);
        }
        
        public int ConfigID;
        public string Name;
    }

    class ClassTalentsDeleteConfig : ClientPacket
    {
        public ClassTalentsDeleteConfig(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            ConfigID = _worldPacket.ReadInt32();
        }
        
        public int ConfigID;
    }

    class ClassTalentsSetStarterBuildActive : ClientPacket
    {
        public ClassTalentsSetStarterBuildActive(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            ConfigID = _worldPacket.ReadInt32();
            Active = _worldPacket.HasBit();
        }
        
        public int ConfigID;
        public bool Active;
    }

    class ClassTalentsSetUsesSharedActionBars : ClientPacket
    {
        public ClassTalentsSetUsesSharedActionBars(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            ConfigID = _worldPacket.ReadInt32();
            UsesShared = _worldPacket.HasBit();
            IsLastSelectedSavedConfig = _worldPacket.HasBit();
        }
        
        public int ConfigID;
        public bool UsesShared;
        public bool IsLastSelectedSavedConfig;
    }

    public class TraitEntryPacket
    {
        public TraitEntryPacket() { }
        public TraitEntryPacket(TraitEntry ufEntry)
        {
            TraitNodeID = ufEntry.TraitNodeID;
            TraitNodeEntryID = ufEntry.TraitNodeEntryID;
            Rank = ufEntry.Rank;
            GrantedRanks = ufEntry.GrantedRanks;
        }

        public void Read(WorldPacket data)
        {
            TraitNodeID = data.ReadInt32();
            TraitNodeEntryID = data.ReadInt32();
            Rank = data.ReadInt32();
            GrantedRanks = data.ReadInt32();
            BonusRanks = data.ReadInt32();
        }

        public void Write(WorldPacket data)
        {
            data.WriteInt32(TraitNodeID);
            data.WriteInt32(TraitNodeEntryID);
            data.WriteInt32(Rank);
            data.WriteInt32(GrantedRanks);
            data.WriteInt32(BonusRanks);
        }
        
        public int TraitNodeID;
        public int TraitNodeEntryID;
        public int Rank;
        public int GrantedRanks;
        public int BonusRanks;
    }

    public class TraitSubTreeCachePacket
    {
        public int TraitSubTreeID;
        public Array<TraitEntryPacket> Entries = new(100);
        public bool Active;
        public void Read(WorldPacket data)
        {
            TraitSubTreeID = data.ReadInt32();
            int count = data.ReadInt32();
            if (count < 0 || count > 100) throw new System.IO.InvalidDataException("Invalid subtree entry count.");
            for (int i = 0; i < count; ++i) { TraitEntryPacket entry = new(); entry.Read(data); Entries.Add(entry); }
            Active = data.HasBit();
        }
        public void Write(WorldPacket data)
        {
            data.WriteInt32(TraitSubTreeID); data.WriteInt32(Entries.Count);
            foreach (TraitEntryPacket entry in Entries) entry.Write(data);
            data.WriteBit(Active); data.FlushBits();
        }
    }

    public class TraitConfigPacket
    {
        public TraitConfigPacket() { }
        public TraitConfigPacket(TraitConfig ufConfig)
        {
            ID = ufConfig.ID;
            Type = (TraitConfigType)(int)ufConfig.Type;
            ChrSpecializationID = (ChrSpecialization)ufConfig.ChrSpecializationID.GetValue();
            CombatConfigFlags = (TraitCombatConfigFlags)(int)ufConfig.CombatConfigFlags;
            LocalIdentifier = ufConfig.LocalIdentifier;
            SkillLineID = (SkillType)ufConfig.SkillLineID.GetValue();
            TraitSystemID = ufConfig.TraitSystemID;
            foreach (var ufEntry in ufConfig.Entries)
                Entries.Add(new TraitEntryPacket(ufEntry));
            Name = ufConfig.Name;
        }
        
        public void Read(WorldPacket data)
        {
            ID = data.ReadInt32();
            Type = (TraitConfigType)data.ReadInt32();
            if ((int)Type == 4) Type = TraitConfigType.Combat; // Forever CamelotCombat
            var entriesCount = data.ReadInt32();
            if (entriesCount < 0 || entriesCount > 100)
                throw new System.IO.InvalidDataException("Invalid trait entry count.");
            int subtreesCount = data.ReadInt32();
            if (subtreesCount < 0 || subtreesCount > 10) throw new System.IO.InvalidDataException("Invalid trait subtree count.");
            switch (Type)
            {
                case TraitConfigType.Combat:
                    ChrSpecializationID = (ChrSpecialization)data.ReadInt32();
                    CombatConfigFlags = (TraitCombatConfigFlags)data.ReadInt32();
                    LocalIdentifier = data.ReadInt32();
                    break;
                case TraitConfigType.Profession:
                    SkillLineID = (SkillType)data.ReadInt32();
                    break;
                case TraitConfigType.Generic:
                    TraitSystemID = data.ReadInt32();
                    VariationID = data.ReadInt32();
                    break;
                default:
                    break;
            }

            for (var i = 0; i < entriesCount; ++i)
            {
                TraitEntryPacket traitEntry = new();
                traitEntry.Read(data);
                Entries.Add(traitEntry);
            }

            for (int i = 0; i < subtreesCount; ++i)
            {
                TraitSubTreeCachePacket subtree = new(); subtree.Read(data); SubTrees.Add(subtree);
            }
            data.ResetBitPos();

            int nameLength = data.ReadBits<int>(9);
            Name = data.ReadString(nameLength);
        }

        public void Write(WorldPacket data)
        {
            data.WriteInt32(ID);
            data.WriteInt32(Type == TraitConfigType.Combat ? 4 : (int)Type);
            data.WriteInt32(Entries.Count);
            data.WriteInt32(SubTrees.Count);
            switch (Type)
            {
                case TraitConfigType.Combat:
                    data.WriteInt32((int)ChrSpecializationID);
                    data.WriteInt32((int)CombatConfigFlags);
                    data.WriteInt32(LocalIdentifier);
                    break;
                case TraitConfigType.Profession:
                    data.WriteInt32((int)SkillLineID);
                    break;
                case TraitConfigType.Generic:
                    data.WriteInt32(TraitSystemID);
                    data.WriteInt32(VariationID);
                    break;
                default:
                    break;
            }

            foreach (TraitEntryPacket traitEntry in Entries)
                traitEntry.Write(data);
            foreach (TraitSubTreeCachePacket subtree in SubTrees) subtree.Write(data);

            data.WriteBits(Name.GetByteCount(), 9);
            data.FlushBits();

            data.WriteString(Name);
        }

        public int ID;
        public TraitConfigType Type;
        public ChrSpecialization ChrSpecializationID = 0;
        public TraitCombatConfigFlags CombatConfigFlags;
        public int LocalIdentifier;  // Local to specialization
        public SkillType SkillLineID;
        public int TraitSystemID;
        public int VariationID;
        public Array<TraitEntryPacket> Entries = new(100);
        public Array<TraitSubTreeCachePacket> SubTrees = new(10);
        public string Name = string.Empty;
    }
}
