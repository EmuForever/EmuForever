// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.Entities;
using System.Collections.Generic;

namespace Game.Networking.Packets
{
    class UpdateTalentData : ServerPacket
    {
        public UpdateTalentData() : base(ServerOpcodes.UpdateTalentData, ConnectionType.Instance) { }

        // Forever layout: TC UpdateTalentData::Write
        public override void Write()
        {
            // Legacy callers fill TalentGroupInfos (classic layout); convert when TalentGroups was not filled.
            if (TalentGroups.Count == 0)
            {
                foreach (var legacy in TalentGroupInfos)
                {
                    ForeverTalentGroupInfo group = new();
                    group.SpecID = legacy.SpecID;
                    if (legacy.Talents != null)
                        foreach (var talent in legacy.Talents)
                            group.TalentIDs.Add((ushort)talent.TalentID);
                    if (legacy.GlyphIDs != null)
                        foreach (var glyph in legacy.GlyphIDs)
                            group.GlyphIDs.Add((uint)glyph);
                    TalentGroups.Add(group);
                }
            }

            _worldPacket.WriteUInt8(ActiveGroup);
            _worldPacket.WriteUInt32(PrimarySpecialization);
            _worldPacket.WriteInt32(TalentGroups.Count);

            foreach (var group in TalentGroups)
            {
                _worldPacket.WriteUInt32(group.SpecID);
                _worldPacket.WriteInt32(group.TalentIDs.Count);
                _worldPacket.WriteInt32(group.PvPTalents.Count);
                _worldPacket.WriteInt32(group.GlyphIDs.Count);

                foreach (var talent in group.TalentIDs)
                    _worldPacket.WriteUInt16(talent);

                foreach (var pvpTalent in group.PvPTalents)
                    pvpTalent.Write(_worldPacket);

                foreach (var glyph in group.GlyphIDs)
                    _worldPacket.WriteUInt32(glyph);
            }
        }

        public byte ActiveGroup;
        public uint PrimarySpecialization;
        public List<ForeverTalentGroupInfo> TalentGroups = new();

        // Legacy (not serialized directly, see Write)
        public int UnspentTalentPoints;
        public bool IsPetTalents;
        public List<TalentGroupInfo> TalentGroupInfos = new();
    }

    public class ForeverTalentGroupInfo
    {
        public uint SpecID;
        public List<ushort> TalentIDs = new();
        public List<PvPTalent> PvPTalents = new();
        public List<uint> GlyphIDs = new();
    }

    class RespecWipeConfirm : ServerPacket
    {
        public RespecWipeConfirm() : base(ServerOpcodes.RespecWipeConfirm) { }

        public override void Write()
        {
            _worldPacket.WriteInt8((sbyte)RespecType);
            _worldPacket.WriteUInt32(Cost);
            _worldPacket.WritePackedGuid(RespecMaster);
            _worldPacket.WriteInt32(0); // Forever layout: TC RespecWipeConfirm::Write (Classic 1.60.1 extra int32)
        }

        public ObjectGuid RespecMaster;
        public uint Cost;
        public SpecResetType RespecType;
    }

    class ConfirmRespecWipe : ClientPacket
    {
        public ConfirmRespecWipe(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            RespecMaster = _worldPacket.ReadPackedGuid();
            RespecType = (SpecResetType)_worldPacket.ReadUInt8();
        }

        public ObjectGuid RespecMaster;
        public SpecResetType RespecType;
    }

    class LearnTalentFailed : ServerPacket
    {
        public LearnTalentFailed() : base(ServerOpcodes.LearnTalentFailed) { }

        public override void Write()
        {
            _worldPacket.WriteBits(Reason, 4);
            _worldPacket.WriteInt32(SpellID);
            _worldPacket.WriteInt32(Talents.Count);

            foreach (var talent in Talents)
                _worldPacket.WriteUInt16(talent);
        }

        public uint Reason;
        public int SpellID;
        public List<ushort> Talents = new();
    }

    class ActiveGlyphs : ServerPacket
    {
        public ActiveGlyphs() : base(ServerOpcodes.ActiveGlyphs) { }

        public override void Write()
        {
            _worldPacket.WriteInt32(Glyphs.Count);
            foreach (GlyphBinding glyph in Glyphs)
                glyph.Write(_worldPacket);

            _worldPacket.WriteBit(IsFullUpdate);
            _worldPacket.FlushBits();
        }

        public List<GlyphBinding> Glyphs = new();
        public bool IsFullUpdate;
    }

    class LearnPvpTalents : ClientPacket
    {
        public LearnPvpTalents(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            uint size = _worldPacket.ReadUInt32();
            for (int i = 0; i < size; ++i)
                Talents[i] = new PvPTalent(_worldPacket);
        }

        public Array<PvPTalent> Talents = new(4);
    }

    class LearnPvpTalentFailed : ServerPacket
    {
        public LearnPvpTalentFailed() : base(ServerOpcodes.LearnPvpTalentFailed) { }

        public override void Write()
        {
            _worldPacket.WriteBits(Reason, 4);
            _worldPacket.WriteInt32(SpellID);
            _worldPacket.WriteInt32(Talents.Count);

            foreach (var pvpTalent in Talents)
                pvpTalent.Write(_worldPacket);
        }

        public uint Reason;
        public int SpellID;
        public List<PvPTalent> Talents = new();
    }

    class LearnTalent : ClientPacket
    {
        public LearnTalent(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            TalentID = _worldPacket.ReadInt32();
            Rank = _worldPacket.ReadUInt16();
        }

        public int TalentID;
        public ushort Rank;
    }

    class LearnPreviewTalents : ClientPacket
    {
        public LearnPreviewTalents(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            count = _worldPacket.ReadUInt32();

            for (int i = 0; i < count; i++)
            {
                TalentInfo talentInfo = new TalentInfo();
                talentInfo.Read(_worldPacket);
                talentInfos.Add(talentInfo);
            }
        }

        public uint count;
        public Array<TalentInfo> talentInfos = new(60);
    }

    class RemoveGlyph : ClientPacket
    {
        public RemoveGlyph(WorldPacket packet) : base(packet) { }

        public override void Read()
        {
            _worldPacket.ReadUInt8();
        }

        public byte GlyphSlot;
    }

//Structs
    public struct PvPTalent
    {
        public PvPTalent(WorldPacket data)
        {
            PvPTalentID = data.ReadUInt16();
            Slot = data.ReadUInt8();
        }

        public void Write(WorldPacket data)
        {
            data.WriteUInt16(PvPTalentID);
            data.WriteUInt8(Slot);
        }
        
        public ushort PvPTalentID;
        public byte Slot;
    }

    struct GlyphBinding
    {
        public GlyphBinding(int spellId, ushort glyphId)
        {
            SpellID = spellId;
            GlyphID = glyphId;
        }

        public void Write(WorldPacket data)
        {
            data.WriteInt32(SpellID);
            data.WriteUInt16(GlyphID);
        }

        int SpellID;
        ushort GlyphID;
    }

    public struct TalentGroupInfo
    {
        public TalentGroupInfo()
        {
            Talents = new();
        }

        public void Write(WorldPacket data)
        {
            data.WriteUInt8((byte)Talents.Count);
            data.WriteUInt32((uint)Talents.Count);

            data.WriteUInt8(PlayerConst.MaxGlyphSlotIndex);
            data.WriteUInt32(PlayerConst.MaxGlyphSlotIndex);

            data.WriteUInt8(SpecID);

            foreach (var talentInfo in Talents)
                talentInfo.Write(data);

            foreach (var glyphInfo in GlyphIDs)
                data.WriteUInt16((ushort)glyphInfo);
        }

        public byte SpecID;
        public List<TalentInfo> Talents;
        public int[] GlyphIDs;
    }

    public struct TalentInfo
    {
        public TalentInfo(int talentId, byte rank)
        {
            TalentID = talentId;
            Rank = rank;
        }

        public void Write(WorldPacket data)
        {
            data.WriteInt32(TalentID);
            data.WriteUInt8(Rank);
        }

        public void Read(WorldPacket data)
        {
            TalentID = data.ReadInt32();
            Rank = data.ReadUInt8();
        }

        public int TalentID;
        public byte Rank;
    };
}
