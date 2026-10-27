using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Constants;
using Game.Entities;

namespace Game.Services.Classic;

internal sealed record GuildClubMember(ObjectGuid Guid, uint AccountId, uint Role, string Note);
internal sealed record GuildClubState(ulong Id, string Name, ObjectGuid Actor, ObjectGuid Leader, uint RealmId,
    ulong CreationTime, IReadOnlyList<GuildClubMember> Members);

// Wire fields follow the reference's Client/api club and club_membership schemas.
static class ClassicSocialSupport
{
    public const uint MembershipService = 0x94B94786, ClubService = 0xE273DE0E, ClubListener = 0x80909D73;
    const uint NotImplemented = 0xBC7, NoClub = 0x2AF9, NotMember = 0x2AFA, NoSuchMember = 0x2AFC, NoStream = 0x2B5C;

    public static ulong CreateClubMemberId(ObjectGuid guid, uint realmId) =>
        (ulong)guid.GetCounter() | ((ulong)(realmId & 0xFFF) << 48);

    public static GuildClubState Snapshot(Player player)
    {
        var guild = player?.GetGuild();
        if (guild == null) return null;
        uint realm = (uint)Global.WorldMgr.GetRealm().Id.Index;
        var members = guild.GetMembers().Values.Select(member => new GuildClubMember(member.GetGUID(),
            (uint)member.GetAccountId(), member.GetRankId() == GuildRankId.GuildMaster ? 1u :
                guild.HasAnyRankRight(member.GetRankId(), GuildRankRights.OffChatListen | GuildRankRights.OffChatSpeak) ? 3u : 4u,
            member.GetPublicNote())).ToArray();
        long created = new DateTimeOffset(guild.GetCreatedDate().Data).ToUnixTimeMilliseconds();
        return new GuildClubState((ulong)guild.GetId(), guild.GetName(), player.GetGUID(), guild.GetLeaderGUID(), realm,
            (ulong)Math.Max(0, created) * 1000, members);
    }

    public static BnetCall Handle(uint service, uint method, bool inWorld, GuildClubState guild, byte[] payload)
    {
        if (service == MembershipService)
        {
            if (method == 2) return new();
            if (method != 1) return new() { Status = NotImplemented };
            // The Classic client disconnects on a successful subscription before an attached player exists.
            if (!inWorld) return new() { Status = 1 };
            if (guild != null && !guild.Members.Any(m => m.Guid == guild.Actor)) return new() { Status = NotMember };
            var view = new ProtoWriter(); view.Varint(1, 0); view.Varint(2, 0);
            var state = new ProtoWriter();
            if (guild != null)
            {
                var description = new ProtoWriter(); description.Message(1, MemberId(guild, guild.Actor));
                description.Message(2, Description(guild)); state.Message(1, description);
            }
            state.Message(4, view);
            var response = new ProtoWriter(); response.Message(1, state);
            return new() { Body = response.ToArray() };
        }
        if (service != ClubService) return new() { Status = NotImplemented };
        if (method == 6)
        {
            var requested = ProtoField.Walk(payload).FirstOrDefault(field => field.Field == 2 && field.Wire == 2);
            string name = requested.Bytes == null ? "" : ProtoField.Walk(requested.Bytes)
                .Where(field => field.Field == 2 && field.Wire == 2).Select(field => Encoding.UTF8.GetString(field.Bytes)).FirstOrDefault() ?? "";
            if (!name.Equals("guild", StringComparison.Ordinal)) return new() { Status = 0xD };
            var response = new ProtoWriter(); response.Message(1, GuildType());
            return new() { Body = response.ToArray() };
        }
        if (!inWorld) return new() { Status = 1 };
        if (guild == null) return new() { Status = NoClub };
        var actor = guild.Members.FirstOrDefault(m => m.Guid == guild.Actor);
        if (actor == null) return new() { Status = NotMember };
        ulong requestedClub = ProtoField.Walk(payload).FirstOrDefault(f => f.Field == 2 && f.Wire == 0).Varint;
        if (requestedClub != 0 && requestedClub != guild.Id) return new() { Status = NoClub };
        switch (method)
        {
            case 1:
            {
                if (!guild.Members.Any(m => m.Guid == guild.Leader)) return new() { Status = NoSuchMember };
                var note = new ProtoWriter(); note.Message(1, MemberId(guild, guild.Actor));
                note.Varint(3, guild.Id); note.Message(4, Club(guild)); note.Message(11, Member(guild, actor));
                var assignment = new ProtoWriter(); assignment.Message(1, MemberId(guild, guild.Actor)); assignment.Bool(2, true);
                var changed = new ProtoWriter(); changed.Varint(3, guild.Id); changed.Message(4, assignment);
                var result = new BnetCall();
                result.Notes.Add(new BnetNote { Service = ClubListener, Method = 0xC0000001, Body = note.ToArray() });
                result.Notes.Add(new BnetNote { Service = ClubListener, Method = 0xC0000021, Body = changed.ToArray() });
                return result;
            }
            case 2: case 101: case 107: case 155: return new();
            case 34:
            {
                var response = new ProtoWriter();
                foreach (var member in guild.Members) response.Message(1, Member(guild, member));
                return new() { Body = response.ToArray() };
            }
            case 105:
            {
                var response = new ProtoWriter();
                for (uint stream = 1; stream <= 2; ++stream)
                {
                    // Officer streams are offered only to guild ranks that can use officer chat.
                    if (stream == 2 && actor.Role == 4) continue;
                    var data = new ProtoWriter(); data.Varint(1, guild.Id); data.Varint(2, stream);
                    var value = new ProtoWriter(); value.String(4, stream == 1 ? "COMMUNITIES_GUILD_GENERAL_CHANNEL_NAME" : "COMMUNITIES_GUILD_OFFICER_CHANNEL_NAME");
                    var attribute = new ProtoWriter(); attribute.String(1, "global_strings_tag"); attribute.Message(2, value);
                    data.Message(3, attribute); data.String(4, stream == 1 ? "Guild" : "Officer");
                    var access = new ProtoWriter(); access.Bytes(1, stream == 1 ? new byte[] { 1, 2, 3, 4 } : new byte[] { 1, 2, 3 });
                    data.Message(6, access); data.Varint(7, 0); response.Message(1, data);
                    var view = new ProtoWriter(); view.Varint(1, guild.Id); view.Varint(2, stream); response.Message(2, view);
                }
                return new() { Body = response.ToArray() };
            }
            case 100:
            {
                var streams = ProtoField.Walk(payload).Where(f => f.Field == 3 && f.Wire == 0).Select(f => f.Varint).ToArray();
                if (streams.Length == 0 || streams.Any(s => s != 1 && s != 2)) return new() { Status = NoStream };
                if (actor.Role == 4 && streams.Contains(2UL)) return new() { Status = NotMember };
                return new();
            }
            default: return new() { Status = NotImplemented };
        }
    }

    static ProtoWriter GuildType()
    {
        var type = new ProtoWriter(); type.Fixed32(1, 0x576F57); type.String(2, "guild"); return type;
    }
    static ProtoWriter MemberId(GuildClubState guild, ObjectGuid guid)
    {
        var id = new ProtoWriter(); id.Varint(2, CreateClubMemberId(guid, guild.RealmId));
        id.Varint(3, guild.Members.FirstOrDefault(m => m.Guid == guid)?.AccountId ?? 0); return id;
    }
    static ProtoWriter Leader(GuildClubState guild)
    {
        var leader = new ProtoWriter(); leader.Message(1, MemberId(guild, guild.Leader)); return leader;
    }
    static ProtoWriter Description(GuildClubState guild)
    {
        var data = new ProtoWriter(); data.Varint(1, guild.Id); data.Message(2, GuildType()); data.String(3, guild.Name);
        data.Varint(6, 3); data.Varint(7, 0); data.Varint(8, (ulong)guild.Members.Count); data.Message(9, Leader(guild));
        data.Varint(10, guild.CreationTime); data.String(11, ""); data.String(12, ""); return data;
    }
    static ProtoWriter Club(GuildClubState guild)
    {
        var data = new ProtoWriter(); data.Varint(1, guild.Id); data.Message(2, GuildType()); data.String(4, guild.Name);
        data.Varint(8, 3); data.Varint(9, 0); data.Varint(10, (ulong)guild.Members.Count); data.Message(14, Leader(guild)); return data;
    }
    static ProtoWriter Member(GuildClubState guild, GuildClubMember member)
    {
        var data = new ProtoWriter(); data.Message(1, MemberId(guild, member.Guid)); data.Bytes(3, new[] { (byte)member.Role });
        data.Varint(6, 2); data.Varint(8, 0); data.String(9, member.Note ?? ""); data.Bool(50, true); return data;
    }
}
