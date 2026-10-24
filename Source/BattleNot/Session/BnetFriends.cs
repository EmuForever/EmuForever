using MySqlConnector;

namespace BattleNot;

sealed class FriendsPush
{
    public uint AccountId;
    public uint Service;
    public uint Method;
    public byte[] Body = [];
}

sealed class FriendsReply
{
    public uint Status;
    public byte[] Body = [];
    public string Command = "";
    public List<FriendsPush> Push = [];
}

static class BnetFriends
{
    public const uint ServiceHash = 0x5869BE8C;
    public const uint ListenerHash = 0xDBC01A89;
    const uint ProgramWoW = 0x576F57;
    const int MaxFriends = 200;
    const int MaxPending = 100;
    const uint Listen = 0xC0000000;

    const uint ErrorInternal = 0x1;
    const uint ErrorInvalidTarget = 0x14;
    const uint ErrorTooManySent = 0x1389;
    const uint ErrorAlreadyFriends = 0x138B;
    const uint ErrorNotFriends = 0x138C;
    const uint ErrorInvitationExists = 0x138D;
    const uint ErrorInvalidInvitation = 0x138E;
    const uint ErrorBlocked = 0x1391;
    const uint ErrorNoteTooLong = 0x1395;
    const uint ErrorInviteeFull = 0x1397;
    const uint ErrorInviterFull = 0x1398;
    const uint ErrorBlockedByInvitee = 0x1399;
    const uint ErrorUndeliverable = 0x139A;

    public static async Task<FriendsReply> Call(BnotDb db, uint me, uint method, byte[] payload)
    {
        await db.Schema.EnsureBnetIdentity();
        try
        {
            return method switch
            {
                1 => Ok("Subscribe"),
                2 => Ok("Unsubscribe"),
                3 => await Sent(db, me),
                4 => await Received(db, me),
                5 => await Friends(db, me),
                6 => await IsFriend(db, me, payload),
                7 => Ok("ViewFriends"),
                8 => await Invite(db, me, payload),
                9 => await Accept(db, me, Field(payload, 1)),
                10 => await RemoveInvite(db, me, Field(payload, 1), false, 2),
                11 => await RevokeAll(db, me),
                12 => await RemoveInvite(db, me, Field(payload, 1), true, 1),
                13 => await Remove(db, me, Field(payload, 1)),
                14 => await Update(db, me, payload),
                _ => new FriendsReply { Status = Rpc.ErrorRpcInvalidMethod, Command = "method " + method }
            };
        }
        catch (Exception ex)
        {
            Log.outError(LogFilter.Session, "Friends " + method + " " + ex.Message);
            return new FriendsReply { Status = ErrorInternal, Command = "method " + method };
        }
    }

    public static async Task SelfTest(BnotDb db)
    {
        Totp.SelfTest();
        await db.Schema.EnsureBnetIdentity();
        string tag = await db.Tags.GameBattleTag(1, "");
        if (tag.Length == 0)
            throw new InvalidOperationException("account 1 has no BattleTag");
        if (await AreFriends(db, 1, 2))
        {
            FriendsReply listed = await Call(db, 1, 5, []);
            if (listed.Status != 0)
                throw new InvalidOperationException("GetFriends status " + listed.Status);
            Log.outDebug(LogFilter.Session, "Friends self-test skipped the invite (accounts 1 and 2 are already friends). BattleTag " + tag);
            return;
        }
        bool existed = await InviteExists(db, 1, 2);
        if (!existed)
        {
            var target = new ProtoWriter();
            target.Varint(10, 2);
            var request = new ProtoWriter();
            request.Message(1, target);
            FriendsReply sent = await Call(db, 1, 8, request.ToArray());
            if (sent.Status != 0)
                throw new InvalidOperationException("SendInvitation status 0x" + sent.Status.ToString("X"));
            FriendsReply incoming = await Call(db, 2, 4, []);
            if (incoming.Status != 0 || incoming.Body.Length == 0)
                throw new InvalidOperationException("GetReceivedInvitations empty");
            ulong id = await InviteId(db, 1, 2);
            var revoke = new ProtoWriter();
            revoke.Varint(1, id);
            FriendsReply dropped = await Call(db, 1, 10, revoke.ToArray());
            if (dropped.Status != 0 || await InviteExists(db, 1, 2))
                throw new InvalidOperationException("RevokeInvitation left the invite");
        }
        Log.outDebug(LogFilter.Session, "Friends and authenticator self-test passed. BattleTag " + tag);
    }

    static FriendsReply Ok(string command) => new() { Command = command };

    static async Task<FriendsReply> Friends(BnotDb db, uint me)
    {
        var body = new ProtoWriter();
        await using MySqlConnection conn = db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT f.friend_account_id, IFNULL(a.battle_tag,''), f.created, f.note FROM bnot_friends f " +
            "JOIN bnot_accounts a ON a.id = f.friend_account_id WHERE f.account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        int count = 0;
        while (await reader.ReadAsync())
        {
            body.Message(1, Friend(reader.GetUInt32(0), reader.GetString(1), reader.GetUInt32(2), reader.GetString(3)));
            count++;
        }
        body.Varint(2, 0);
        return new FriendsReply { Body = body.ToArray(), Command = "GetFriends " + count };
    }

    static async Task<FriendsReply> Sent(BnotDb db, uint me)
    {
        var body = new ProtoWriter();
        await using MySqlConnection conn = db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.id, IFNULL(a.battle_tag,''), i.created FROM bnot_friend_invitations i " +
            "JOIN bnot_accounts a ON a.id = i.invitee_account_id WHERE i.inviter_account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            body.Message(1, SentInvite(reader.GetUInt32(0), reader.GetString(1), reader.GetUInt32(2)));
        body.Varint(2, 0);
        return new FriendsReply { Body = body.ToArray(), Command = "GetSentInvitations" };
    }

    static async Task<FriendsReply> Received(BnotDb db, uint me)
    {
        var body = new ProtoWriter();
        await using MySqlConnection conn = db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.id, i.inviter_account_id, IFNULL(a.battle_tag,''), i.created FROM bnot_friend_invitations i " +
            "JOIN bnot_accounts a ON a.id = i.inviter_account_id WHERE i.invitee_account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            body.Message(1, ReceivedInvite(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), reader.GetUInt32(3)));
        body.Varint(2, 0);
        return new FriendsReply { Body = body.ToArray(), Command = "GetReceivedInvitations" };
    }

    static async Task<FriendsReply> IsFriend(BnotDb db, uint me, byte[] payload)
    {
        var body = new ProtoWriter();
        body.Bool(1, await AreFriends(db, me, (uint)Field(payload, 1)));
        return new FriendsReply { Body = body.ToArray(), Command = "IsFriend" };
    }

    static async Task<FriendsReply> Invite(BnotDb db, uint me, byte[] payload)
    {
        (ulong accountId, string name, string email, string tag) = Target(payload);
        var person = await db.Tags.FindBnetPerson(accountId, tag, email, name);
        if (person == null)
            return Fail(ErrorUndeliverable, "SendInvitation");
        if (person.Value.Id == me)
            return Fail(ErrorInvalidTarget, "SendInvitation");
        if (await db.Friends.LauncherBlocked(person.Value.Id, me))
            return Fail(ErrorBlockedByInvitee, "SendInvitation");
        if (await db.Friends.LauncherBlocked(me, person.Value.Id))
            return Fail(ErrorBlocked, "SendInvitation");
        if (await AreFriends(db, me, person.Value.Id))
            return Fail(ErrorAlreadyFriends, "SendInvitation");
        if (await InviteExists(db, me, person.Value.Id))
            return Fail(ErrorInvitationExists, "SendInvitation");
        if (await Count(db, "SELECT COUNT(*) FROM bnot_friend_invitations WHERE inviter_account_id=@id", me) >= MaxPending)
            return Fail(ErrorTooManySent, "SendInvitation");
        if (await Count(db, "SELECT COUNT(*) FROM bnot_friends WHERE account_id=@id", me) >= MaxFriends)
            return Fail(ErrorInviterFull, "SendInvitation");
        ulong theirs = await InviteId(db, person.Value.Id, me);
        if (theirs != 0)
            return await Accept(db, me, theirs);

        uint created = Now();
        ulong id = await db.Friends.InsertInvite(me, person.Value.Id, created);
        if (id == 0)
            return Fail(ErrorInternal, "SendInvitation");
        string mine = await db.Tags.GameBattleTag(me, "");
        var reply = new FriendsReply { Command = "SendInvitation " + person.Value.Tag };
        Push(reply, me, 5, Note(me, 2, SentInvite(id, person.Value.Tag, created)));
        Push(reply, person.Value.Id, 3, Note(person.Value.Id, 2, ReceivedInvite(id, me, mine, created)));
        Log.outDebug(LogFilter.Session, "Friends invite " + me + " -> " + person.Value.Id + " (" + person.Value.Tag + ")");
        return reply;
    }

    static async Task<FriendsReply> Accept(BnotDb db, uint me, ulong invitationId)
    {
        uint? inviter = await db.Friends.InviteInviter(invitationId, me);
        if (inviter == null)
            return Fail(ErrorInvalidInvitation, "AcceptInvitation");
        if (await Count(db, "SELECT COUNT(*) FROM bnot_friends WHERE account_id=@id", me) >= MaxFriends)
            return Fail(ErrorInviteeFull, "AcceptInvitation");
        uint created = Now();
        await db.Friends.AcceptGameFriends(inviter.Value, me, created);
        string mine = await db.Tags.StoredTag(me);
        string theirs = await db.Tags.StoredTag(inviter.Value);
        var reply = new FriendsReply { Command = "AcceptInvitation" };
        Push(reply, me, 4, Assignment(me, invitationId, 0, true));
        Push(reply, inviter.Value, 6, Assignment(inviter.Value, invitationId, 0, true));
        Push(reply, me, 1, Note(me, 2, Friend(inviter.Value, theirs, created, "")));
        Push(reply, inviter.Value, 1, Note(inviter.Value, 2, Friend(me, mine, created, "")));
        Log.outDebug(LogFilter.Session, "Friends accept " + me + " and " + inviter.Value);
        return reply;
    }

    static async Task<FriendsReply> RemoveInvite(BnotDb db, uint me, ulong invitationId, bool asInvitee, uint reason)
    {
        (uint Inviter, uint Invitee)? row = await db.Friends.InvitePair(invitationId, me, asInvitee);
        if (row == null)
            return Fail(ErrorInvalidInvitation, asInvitee ? "IgnoreInvitation" : "RevokeInvitation");
        await db.Friends.DeleteInvite(invitationId);
        var reply = new FriendsReply { Command = asInvitee ? "IgnoreInvitation" : "RevokeInvitation" };
        Push(reply, row.Value.Inviter, 6, Assignment(row.Value.Inviter, invitationId, reason, true));
        Push(reply, row.Value.Invitee, 4, Assignment(row.Value.Invitee, invitationId, reason, true));
        return reply;
    }

    static async Task<FriendsReply> RevokeAll(BnotDb db, uint me)
    {
        List<ulong> ids = await db.Friends.SentInviteIds(me);
        foreach (ulong id in ids)
            await RemoveInvite(db, me, id, false, 2);
        return Ok("RevokeAllInvitations");
    }

    static async Task<FriendsReply> Remove(BnotDb db, uint me, ulong otherId)
    {
        uint other = (uint)otherId;
        if (!await AreFriends(db, me, other))
            return Fail(ErrorNotFriends, "RemoveFriend");
        await db.Friends.DropGameFriends(me, other);
        var reply = new FriendsReply { Command = "RemoveFriend" };
        Push(reply, me, 2, Assignment(me, other, 0, false));
        Push(reply, other, 2, Assignment(other, me, 0, false));
        return reply;
    }

    static async Task<FriendsReply> Update(BnotDb db, uint me, byte[] payload)
    {
        ulong otherId = Field(payload, 1);
        string? note = NoteOf(payload);
        uint other = (uint)otherId;
        if (!await AreFriends(db, me, other))
            return Fail(ErrorNotFriends, "UpdateFriendState");
        if (note != null && note.Length > 127)
            return Fail(ErrorNoteTooLong, "UpdateFriendState");
        if (note != null)
            await db.Friends.SetFriendNote(me, other, note);
        var assignment = new ProtoWriter();
        assignment.Varint(1, other);
        if (note != null)
            assignment.String(4, note);
        var reply = new FriendsReply { Command = "UpdateFriendState" };
        Push(reply, me, 7, Note(me, 2, assignment));
        return reply;
    }

    static FriendsReply Fail(uint status, string command) => new() { Status = status, Command = command };

    static void Push(FriendsReply reply, uint account, uint method, ProtoWriter body)
    {
        reply.Push.Add(new FriendsPush
        {
            AccountId = account,
            Service = ListenerHash,
            Method = Listen | method,
            Body = body.ToArray()
        });
    }

    static ProtoWriter Note(uint agent, int field, ProtoWriter inner)
    {
        var note = new ProtoWriter();
        note.Varint(1, agent);
        note.Message(field, inner);
        return note;
    }

    static ProtoWriter Assignment(uint agent, ulong id, uint reason, bool invitation)
    {
        var row = new ProtoWriter();
        row.Varint(1, invitation ? id : id);
        if (invitation)
            row.Varint(2, reason);
        return Note(agent, 2, row);
    }

    static ProtoWriter Friend(uint id, string tag, uint created, string note)
    {
        var entry = new ProtoWriter();
        entry.Varint(1, id);
        entry.Varint(2, 1);
        if (tag.Length > 0)
            entry.String(3, tag);
        entry.Varint(6, created);
        if (note.Length > 0)
            entry.String(7, note);
        return entry;
    }

    static ProtoWriter SentInvite(ulong id, string tag, uint created)
    {
        var entry = new ProtoWriter();
        entry.Varint(1, id);
        entry.String(2, tag);
        entry.Varint(3, 1);
        entry.Fixed32(4, ProgramWoW);
        entry.Varint(6, created);
        return entry;
    }

    static ProtoWriter ReceivedInvite(ulong id, uint inviter, string tag, uint created)
    {
        var who = new ProtoWriter();
        who.Varint(1, inviter);
        if (tag.Length > 0)
            who.String(2, tag);
        var entry = new ProtoWriter();
        entry.Varint(1, id);
        entry.Message(2, who);
        entry.Varint(4, 1);
        entry.Fixed32(5, ProgramWoW);
        entry.Varint(7, created);
        entry.Varint(8, created + 30u * 86400u);
        return entry;
    }

    static (ulong Account, string Name, string Email, string Tag) Target(byte[] payload)
    {
        ulong account = 0;
        string name = "", email = "", tag = "";
        var reader = new ProtoReader(payload);
        while (reader.Next(out int field, out int wire))
        {
            if (field == 1 && wire == 2)
            {
                var inner = new ProtoReader(reader.ReadBytes());
                while (inner.Next(out int part, out int partWire))
                {
                    if (part == 1 && partWire == 2) name = inner.ReadString();
                    else if (part == 10 && partWire == 0) account = inner.ReadVarint();
                    else if (part == 11 && partWire == 2) email = inner.ReadString();
                    else if (part == 12 && partWire == 2) tag = inner.ReadString();
                    else inner.Skip(partWire);
                }
            }
            else
                reader.Skip(wire);
        }
        return (account, name, email, tag);
    }

    static string? NoteOf(byte[] payload)
    {
        var reader = new ProtoReader(payload);
        while (reader.Next(out int field, out int wire))
        {
            if (field == 2 && wire == 2)
            {
                var inner = new ProtoReader(reader.ReadBytes());
                while (inner.Next(out int part, out int partWire))
                {
                    if (part == 2 && partWire == 2)
                        return inner.ReadString();
                    inner.Skip(partWire);
                }
                return null;
            }
            reader.Skip(wire);
        }
        return null;
    }

    static ulong Field(byte[] payload, int wanted)
    {
        var reader = new ProtoReader(payload);
        while (reader.Next(out int field, out int wire))
        {
            if (field == wanted && wire == 0)
                return reader.ReadVarint();
            reader.Skip(wire);
        }
        return 0;
    }

    static async Task<bool> AreFriends(BnotDb db, uint a, uint b)
    {
        await using MySqlConnection conn = db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT 1 FROM bnot_friends WHERE account_id=@a AND friend_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@a", a);
        cmd.Parameters.AddWithValue("@b", b);
        return await cmd.ExecuteScalarAsync() != null;
    }

    static async Task<bool> InviteExists(BnotDb db, uint inviter, uint invitee) => await InviteId(db, inviter, invitee) != 0;

    static async Task<ulong> InviteId(BnotDb db, uint inviter, uint invitee)
    {
        await using MySqlConnection conn = db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id FROM bnot_friend_invitations WHERE inviter_account_id=@a AND invitee_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@a", inviter);
        cmd.Parameters.AddWithValue("@b", invitee);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? 0 : Convert.ToUInt64(value);
    }

    static async Task<long> Count(BnotDb db, string sql, uint id)
    {
        await using MySqlConnection conn = db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
    }

    static uint Now() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
