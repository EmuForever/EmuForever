using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Groups;
using Game.Networking;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game
{
    // Classic 1.60 Group Finder (vanilla style premade groups, CMSG_LFG_LIST_*), ported from the reference
    // (E:\trinitycore_new GuildHandler.cpp HandleLfgListProbe). Listings live in memory only.
    static class GroupFinderListings
    {
        // One posted listing. Request = the client's LFGListJoinRequest bytes as sent; the client reads it back with the mirrored
        // reader, so it is echoed unchanged in the status and the search results.
        internal sealed class Listing
        {
            public int Id;
            public uint CategoryId;
            public List<uint> Activities = new();
            public byte[] Request = Array.Empty<byte>();
            public long Created;
            public string Name = "", Surname = "", Comment = "";
            public byte PlayStyle;    // 1 Learning, 2 Relaxed, 3 Competitive, 4 Carry Offered
        }

        internal static readonly Dictionary<ObjectGuid, Listing> Listings = new();
        internal static readonly Dictionary<ObjectGuid, byte> Roles = new();
        internal static readonly object Sync = new();
        static int _nextId = 1;
        internal static int NextId() => _nextId++;

        // LFGListJoinRequest: bits activity count 5, name 10, comment 11, voice chat 8, 9 flags (4 bools, has int32 x3, has uint8,
        // bool); uint32 category, float item level, {float, float, uint32 n, n x 16 bytes}, int8, n x uint32 activity ids, name,
        // comment, voice chat, the optional int32 x3 and uint8
        internal static bool ReadRequest(WorldPacket data, Listing listing)
        {
            long start = data.Position;
            int activityCount = data.ReadBits<int>(5);
            int nameLength = data.ReadBits<int>(6);
            int surnameLength = data.ReadBits<int>(9);
            int commentLength = data.ReadBits<int>(11);
            int voiceChatLength = data.ReadBits<int>(8);
            var bits = new bool[9];
            for (int i = 0; i < bits.Length; ++i) bits[i] = data.HasBit();
            data.ResetBitPos();

            listing.CategoryId = data.ReadUInt32();
            data.ReadFloat();       // required item level
            data.ReadFloat();
            data.ReadFloat();
            uint extraCount = data.ReadUInt32();
            data.Skip((int)(extraCount * 16));
            data.ReadInt8();
            listing.Activities.Clear();
            for (int i = 0; i < activityCount; ++i) listing.Activities.Add(data.ReadUInt32());
            listing.Name = data.ReadString(nameLength);
            listing.Surname = data.ReadString(surnameLength);
            listing.Comment = data.ReadString(commentLength);
            data.Skip(voiceChatLength);
            for (int i = 4; i < 7; ++i)
                if (bits[i]) data.ReadInt32();
            if (bits[7]) listing.PlayStyle = data.ReadUInt8();

            byte[] all = data.GetData();
            int length = (int)(data.Position - start);
            listing.Request = new byte[length];
            Array.Copy(all, start, listing.Request, 0, length);
            return data.Position == data.GetSize();
        }

        internal static RideTicket Ticket(ObjectGuid leader, Listing listing) => new()
        {
            RequesterGuid = leader,
            Id = listing.Id,
            Type = RideType.Lfg,
            JoinTime = (ServerTime)(UnixTime64)listing.Created
        };

        // the listing a player belongs to: their own, or their group leader's
        internal static (ObjectGuid Leader, Listing Listing) Of(Player player)
        {
            ObjectGuid leader = player.GetGroup() != null ? player.GetGroup().GetLeaderGUID() : player.GetGUID();
            if (!Listings.TryGetValue(leader, out Listing listing))
            {
                leader = player.GetGUID();
                Listings.TryGetValue(leader, out listing);
            }
            return (leader, listing);
        }
    }

    // Raw access to a client packet whose layout is parsed by hand.
    public class GroupFinderClientPacket : ClientPacket
    {
        public GroupFinderClientPacket(WorldPacket packet) : base(packet) { }
        public override void Read() { }
        public WorldPacket Data => _worldPacket;
    }

    class GroupFinderServerPacket : ServerPacket
    {
        readonly Action<WorldPacket> _write;
        public GroupFinderServerPacket(ServerOpcodes opcode, Action<WorldPacket> write) : base(opcode) { _write = write; }
        public override void Write() => _write(_worldPacket);
    }

    public partial class WorldSession
    {
        // SMSG_LFG_LIST_UPDATE_STATUS: ticket, request, uint64 remaining time, uint8 result, bits listed, has guid, has uint8
        void SendLfgListStatus(GroupFinderListings.Listing listing, bool listed)
        {
            SendPacket(new GroupFinderServerPacket(ServerOpcodes.LfgListUpdateStatus, data =>
            {
                GroupFinderListings.Ticket(GetPlayer().GetGUID(), listing).Write(data);
                data.WriteBytes(listing.Request);
                data.WriteUInt64(listed ? 1800ul : 0ul);
                data.WriteUInt8(0);
                data.WriteBit(listed);
                data.WriteBit(false);
                data.WriteBit(false);
                data.FlushBits();
            }));
        }

        // one LFGListSearchResult
        static void WriteLfgListSearchResult(WorldPacket data, ObjectGuid leader, GroupFinderListings.Listing listing)
        {
            GroupFinderListings.Ticket(leader, listing).Write(data);
            data.WriteUInt32((uint)listing.Id);     // sequence
            data.WriteBytes(listing.Request);
            data.WriteUInt8(0);
            data.WritePackedGuid(leader);           // leader
            data.WritePackedGuid(leader);           // last touched: any, name, comment
            data.WritePackedGuid(leader);
            data.WritePackedGuid(leader);
            data.WritePackedGuid(ObjectGuid.Empty); // last touched voice chat
            data.WriteUInt32(Global.WorldMgr.GetVirtualRealmAddress());
            data.WriteUInt32(0);
            data.WriteUInt32(0);
            data.WriteUInt32(0);                    // battle.net friends
            data.WriteUInt32(0);                    // character friends
            data.WriteUInt32(0);                    // guild mates
            // the leader's group as it is now (only the leader when not in a group)
            var members = new List<ObjectGuid>();
            Player leaderPlayer = Global.ObjAccessor.FindConnectedPlayer(leader);
            Group group = leaderPlayer?.GetGroup();
            if (group != null)
                members.AddRange(group.GetMemberSlots().Select(slot => slot.guid));
            else
                members.Add(leader);
            data.WriteUInt32((uint)members.Count);
            data.WriteUInt32(0);                    // completed encounters
            data.WriteUInt64((ulong)listing.Created);
            data.WriteUInt8(0);                     // application status
            data.WritePackedGuid(ObjectGuid.Empty); // party
            data.WriteFloat(0.0f);
            data.WriteFloat(0.0f);
            data.WriteUInt32(0);
            for (int i = 0; i < 9; ++i)
            {
                data.WriteInt32(0);
                data.WriteInt8(0);
            }
            data.WriteUInt8(0);
            data.WriteUInt8(0);

            // members: guid, int8 level, int8 class, int8 role (of the talent specialization: 0 tank, 1 healer, 2 damage), int32 area,
            // uint8, {guid, float, uint32 x3, int32, uint64 x2, int32, uint8}, bit leader
            foreach (ObjectGuid guid in members)
            {
                Player member = Global.ObjAccessor.FindConnectedPlayer(guid);
                var info = Global.CharacterCacheStorage.GetCharacterCacheByGuid(guid);
                ChrSpecializationRecord spec = member != null ? CliDB.ChrSpecializationStorage.LookupByKey((int)member.GetPrimarySpecialization()) : null;
                data.WritePackedGuid(guid);
                data.WriteInt8((sbyte)(member != null ? member.GetLevel() : info?.Level ?? 0));
                data.WriteInt8((sbyte)(member != null ? (int)member.GetClass() : (int)(info?.ClassId ?? 0)));
                data.WriteInt8((sbyte)(spec != null ? (int)spec.Role : 2));
                data.WriteInt32(member != null ? member.GetZoneId() : 0);
                data.WriteUInt8((byte)(member != null ? member.GetLevel() : 0));
                data.WritePackedGuid(ObjectGuid.Empty);
                data.WriteFloat(0.0f);
                data.WriteUInt32(0);
                data.WriteUInt32(0);
                data.WriteUInt32(0);
                data.WriteInt32(0);
                data.WriteUInt64(0);
                data.WriteUInt64(0);
                data.WriteInt32(0);
                data.WriteUInt8(0);
                data.WriteBit(guid == leader);
                data.FlushBits();
            }

            data.WriteBit(false);                   // (entry reader ends with one bit)
            data.FlushBits();
        }

        // create or re-post the player's listing
        [WorldPacketHandler(ClientOpcodes.LfgListJoin)]
        void HandleLfgListJoin(GroupFinderClientPacket packet)
        {
            var listing = new GroupFinderListings.Listing();
            if (!GroupFinderListings.ReadRequest(packet.Data, listing))
                Log.outError(LogFilter.Network, "GroupFinder: join request has unread bytes");
            lock (GroupFinderListings.Sync)
            {
                listing.Id = GroupFinderListings.Listings.TryGetValue(_player.GetGUID(), out var existing) ? existing.Id : GroupFinderListings.NextId();
                listing.Created = (int)LoopTime.UnixServerTime;
                GroupFinderListings.Listings[_player.GetGUID()] = listing;
            }
            // SMSG_LFG_LIST_JOIN_RESULT: ticket, int32, uint8 result, uint8 detail
            SendPacket(new GroupFinderServerPacket(ServerOpcodes.LfgListJoinResult, data =>
            {
                GroupFinderListings.Ticket(_player.GetGUID(), listing).Write(data);
                data.WriteInt32(0);
                data.WriteUInt8(0);
                data.WriteUInt8(0);
            }));
            SendLfgListStatus(listing, true);
        }

        // edit: ticket, request
        [WorldPacketHandler(ClientOpcodes.LfgListUpdateRequest)]
        void HandleLfgListUpdateRequest(GroupFinderClientPacket packet)
        {
            new RideTicket().Read(packet.Data);
            GroupFinderListings.Listing current;
            lock (GroupFinderListings.Sync)
                if (!GroupFinderListings.Listings.TryGetValue(_player.GetGUID(), out current)) return;
            var listing = new GroupFinderListings.Listing();
            if (!GroupFinderListings.ReadRequest(packet.Data, listing))
                Log.outError(LogFilter.Network, "GroupFinder: update request has unread bytes");
            listing.Id = current.Id;
            listing.Created = current.Created;
            lock (GroupFinderListings.Sync)
                GroupFinderListings.Listings[_player.GetGUID()] = listing;
            SendLfgListStatus(listing, true);
        }

        // delist: ticket
        [WorldPacketHandler(ClientOpcodes.LfgListLeave)]
        void HandleLfgListLeave(GroupFinderClientPacket packet)
        {
            GroupFinderListings.Listing listing;
            lock (GroupFinderListings.Sync)
            {
                if (!GroupFinderListings.Listings.Remove(_player.GetGUID(), out listing)) return;
            }
            SendLfgListStatus(listing, false);
        }

        // Classic 1.60 reuses this opcode for the roles ticked in the Group Finder (uint8 mask)
        [WorldPacketHandler(ClientOpcodes.PerksProgramRequestRefund)]
        void HandleGroupFinderRoles(GroupFinderClientPacket packet)
        {
            lock (GroupFinderListings.Sync)
                GroupFinderListings.Roles[_player.GetGUID()] = packet.Data.ReadUInt8();
        }

        // login: re-send the player's own listing
        [WorldPacketHandler(ClientOpcodes.LfgListGetStatus)]
        void HandleLfgListGetStatus(GroupFinderClientPacket packet)
        {
            GroupFinderListings.Listing listing;
            lock (GroupFinderListings.Sync)
                if (!GroupFinderListings.Listings.TryGetValue(_player.GetGUID(), out listing)) return;
            SendLfgListStatus(listing, true);
        }

        // flags byte, uint32 category, uint32, uint32, int32, uint32 count A, uint32, uint32 count B, uint32 count C, uint32, uint8,
        // uint8, uint32 count D, D entries (only 0 seen), A x uint32, B x uint32, C x activity id
        [WorldPacketHandler(ClientOpcodes.LfgListSearch)]
        void HandleLfgListSearch(GroupFinderClientPacket packet)
        {
            WorldPacket data = packet.Data;
            data.ReadUInt8();
            uint categoryId = data.ReadUInt32();
            data.Skip(3 * sizeof(uint));
            uint countA = data.ReadUInt32();
            data.ReadUInt32();
            uint countB = data.ReadUInt32();
            uint countC = data.ReadUInt32();
            data.ReadUInt32();
            data.Skip(2);
            uint countD = data.ReadUInt32();
            var activities = new HashSet<uint>();
            if (countD == 0)
            {
                data.Skip((int)((countA + countB) * sizeof(uint)));
                for (uint i = 0; i < countC; ++i) activities.Add(data.ReadUInt32());
            }

            bool crossFaction = WorldConfig.Values[WorldCfg.AllowTwoSideInteractionGroup].Bool;
            var found = new List<(ObjectGuid Leader, GroupFinderListings.Listing Listing)>();
            lock (GroupFinderListings.Sync)
            {
                foreach (var (leader, listing) in GroupFinderListings.Listings.ToArray())
                {
                    Player leaderPlayer = Global.ObjAccessor.FindConnectedPlayer(leader);
                    if (leaderPlayer == null) { GroupFinderListings.Listings.Remove(leader); continue; }   // leader logged out
                    // only groups of the searcher's own faction, like groups themselves
                    if (!crossFaction && leaderPlayer.GetTeam() != GetPlayer().GetTeam()) continue;
                    if (listing.CategoryId == categoryId && (activities.Count == 0 || listing.Activities.Any(activities.Contains)))
                        found.Add((leader, listing));
                }
            }
            SendPacket(new GroupFinderServerPacket(ServerOpcodes.LfgListSearchResults, results =>
            {
                results.WriteUInt16((ushort)found.Count);
                results.WriteUInt32((uint)found.Count);
                foreach (var (leader, listing) in found)
                    WriteLfgListSearchResult(results, leader, listing);
            }));
        }

        // login: empty blacklist (uint32 count); the category list waits for it
        [WorldPacketHandler(ClientOpcodes.RequestLfgListBlacklist)]
        void HandleRequestLfgListBlacklist(GroupFinderClientPacket packet)
        {
            SendPacket(new GroupFinderServerPacket(ServerOpcodes.LfgListUpdateBlacklist, data => data.WriteUInt32(0)));
        }
    }
}
