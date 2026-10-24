#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using MySqlConnector;
using Framework.Constants;
namespace Game.Services.Classic;
sealed class BnetCharacter
{
    public uint GameAccountId;
    public string AccountName = "";
    public string Name = "";
    public byte Race;
    public byte Gender;
    public byte Class;
    public byte Level;
    public uint Zone;
    public uint GuidLow;
    public uint RealmAddress;
    public uint RealmId;
}

static class WorldFriends
{
    public const uint ServiceHash = 0x5869BE8C;
    public const uint ListenerHash = 0xDBC01A89;
    const uint PresenceListener = 0xF4729709;
    const uint ProgramWoW = 0x576F57;
    const uint ProgramBN = 0x424E;
    const uint PlatformWin = 0x576E3634;
    const uint Listen = 0xC0000000;
    const int MaxFriends = 200;
    const int MaxPending = 100;

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

    static int _ready;
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<BnetCall> Handle(ServiceConfiguration cfg, uint me, uint method, byte[] payload)
    {
        if (me == 0)
            return Fail(0xBC7, "friends no account");
        try
        {
            await Ensure(cfg);
            BnetCall call = await Dispatch(cfg, me, method, payload);
            if (method is 8 or 9 or 13)
                call.FriendIds = (await FriendRows(cfg, me)).Keys.ToHashSet();
            return call;
        }
        catch (Exception ex)
        {
            Log.outError(LogFilter.SessionRpc, "World friends " + ex.Message);
            return Fail(ErrorInternal, "friends");
        }
    }

    public static async Task<Dictionary<uint, (string Tag, uint Created, string Note)>> FriendRows(ServiceConfiguration cfg, uint me)
    {
        var rows = new Dictionary<uint, (string Tag, uint Created, string Note)>();
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT f.friend_account_id, IFNULL(a.battle_tag,''), f.created, f.note FROM bnot_friends f " +
            "JOIN bnot_accounts a ON a.id = f.friend_account_id WHERE f.account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows[reader.GetUInt32(0)] = (reader.GetString(1), reader.GetUInt32(2), reader.GetString(3));
        return rows;
    }

    public static async Task<List<BnetNote>> AddedFriendNotifications(ServiceConfiguration cfg, uint account, uint friendId,
        string tag, uint created, string note)
    {
        var call = new BnetCall();
        Push(call, account, 1, Note(account, Friend(friendId, tag, created, note)));
        await AddPresence(cfg, call, account, friendId, tag);
        return call.Notes;
    }

    public static BnetNote RemovedFriendNotification(uint account, uint friendId)
    {
        var call = new BnetCall();
        Push(call, account, 2, Assignment(account, friendId, 0, false));
        return call.Notes[0];
    }

    public static bool IsFriendListNotification(BnetNote note) =>
        note.Service == ListenerHash && note.Method is 0xC0000001 or 0xC0000002;

    static async Task<BnetCall> Dispatch(ServiceConfiguration cfg, uint me, uint method, byte[] payload)
    {
        return method switch
        {
            1 => Ok("Subscribe"),
            2 => Ok("Unsubscribe"),
            3 => await Sent(cfg, me),
            4 => await Received(cfg, me),
            5 => await Friends(cfg, me),
            6 => await IsFriend(cfg, me, Field(payload, 1)),
            7 => Ok("ViewFriends"),
            8 => await Invite(cfg, me, payload),
            9 => await Accept(cfg, me, Field(payload, 1)),
            10 => await RemoveInvite(cfg, me, Field(payload, 1), false, 2),
            11 => await RevokeAll(cfg, me),
            12 => await RemoveInvite(cfg, me, Field(payload, 1), true, 1),
            13 => await Remove(cfg, me, Field(payload, 1)),
            14 => await Update(cfg, me, payload),
            _ => Fail(0xBC3, "method " + method)
        };
    }

    static async Task Ensure(ServiceConfiguration cfg)
    {
        if (Volatile.Read(ref _ready) == 1)
            return;
        await Gate.WaitAsync();
        try
        {
            if (_ready == 1)
                return;
            await using MySqlConnection conn = Open(cfg);
            await conn.OpenAsync();
            if (await Missing(conn, "bnot_accounts", "battle_tag"))
                await Exec(conn, "ALTER TABLE bnot_accounts ADD COLUMN battle_tag VARCHAR(32) NULL DEFAULT NULL");
            if (await Missing(conn, "bnot_accounts", "authenticator_secret"))
                await Exec(conn, "ALTER TABLE bnot_accounts ADD COLUMN authenticator_secret VARCHAR(64) NULL DEFAULT NULL");
            await Exec(conn, "CREATE TABLE IF NOT EXISTS bnot_friends (" +
                "account_id INT UNSIGNED NOT NULL, friend_account_id INT UNSIGNED NOT NULL, created INT UNSIGNED NOT NULL DEFAULT 0, " +
                "note VARCHAR(127) NOT NULL DEFAULT '', PRIMARY KEY (account_id, friend_account_id), KEY idx_friend (friend_account_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            await Exec(conn, "CREATE TABLE IF NOT EXISTS bnot_friend_invitations (" +
                "id INT UNSIGNED NOT NULL AUTO_INCREMENT, inviter_account_id INT UNSIGNED NOT NULL, invitee_account_id INT UNSIGNED NOT NULL, " +
                "created INT UNSIGNED NOT NULL DEFAULT 0, PRIMARY KEY (id), UNIQUE KEY uk_pair (inviter_account_id, invitee_account_id), " +
                "KEY idx_invitee (invitee_account_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            if (await Missing(conn, "bnot_friends", "favorite"))
                await Exec(conn, "ALTER TABLE bnot_friends ADD COLUMN favorite TINYINT UNSIGNED NOT NULL DEFAULT 0");
            await Exec(conn, "CREATE TABLE IF NOT EXISTS bnot_blocks (" +
                "account_id INT UNSIGNED NOT NULL, blocked_account_id INT UNSIGNED NOT NULL, " +
                "PRIMARY KEY (account_id, blocked_account_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
            Volatile.Write(ref _ready, 1);
        }
        finally
        {
            Gate.Release();
        }
    }

    static async Task<BnetCall> Friends(ServiceConfiguration cfg, uint me)
    {
        var body = new ProtoWriter();
        var rows = new List<(uint Id, string Tag, uint Created, string Note)>();
        await using (MySqlConnection conn = Open(cfg))
        {
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT f.friend_account_id, IFNULL(a.battle_tag,''), f.created, f.note FROM bnot_friends f " +
                "JOIN bnot_accounts a ON a.id = f.friend_account_id WHERE f.account_id=@id", conn);
            cmd.Parameters.AddWithValue("@id", me);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetUInt32(0), reader.GetString(1), reader.GetUInt32(2), reader.GetString(3)));
        }
        foreach ((uint id, string tag, uint created, string note) in rows)
            body.Message(1, Friend(id, tag, created, note));
        body.Varint(2, 0);
        var call = new BnetCall
        {
            Body = body.ToArray(),
            Command = "GetFriends " + rows.Count,
            FriendIds = rows.Select(row => row.Id).ToHashSet()
        };
        HashSet<uint> onlineFriends = await OnlineFriends(cfg, rows.Select(row => row.Id));
        foreach ((uint id, string tag, uint _, string _) in rows)
            await AddPresence(cfg, call, me, id, tag, onlineFriends.Contains(id));
        return call;
    }

    static async Task<BnetCall> Sent(ServiceConfiguration cfg, uint me)
    {
        var body = new ProtoWriter();
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.id, IFNULL(a.battle_tag,''), i.created FROM bnot_friend_invitations i " +
            "JOIN bnot_accounts a ON a.id = i.invitee_account_id WHERE i.inviter_account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            body.Message(1, SentInvite(reader.GetUInt32(0), reader.GetString(1), reader.GetUInt32(2)));
        body.Varint(2, 0);
        return new BnetCall { Body = body.ToArray(), Command = "GetSentInvitations" };
    }

    static async Task<BnetCall> Received(ServiceConfiguration cfg, uint me)
    {
        var body = new ProtoWriter();
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT i.id, i.inviter_account_id, IFNULL(a.battle_tag,''), i.created FROM bnot_friend_invitations i " +
            "JOIN bnot_accounts a ON a.id = i.inviter_account_id WHERE i.invitee_account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            body.Message(1, ReceivedInvite(reader.GetUInt32(0), reader.GetUInt32(1), reader.GetString(2), reader.GetUInt32(3)));
        body.Varint(2, 0);
        return new BnetCall { Body = body.ToArray(), Command = "GetReceivedInvitations" };
    }

    static async Task<BnetCall> IsFriend(ServiceConfiguration cfg, uint me, ulong other)
    {
        var body = new ProtoWriter();
        body.Bool(1, await AreFriends(cfg, me, (uint)other));
        return new BnetCall { Body = body.ToArray(), Command = "IsFriend" };
    }

    static async Task<BnetCall> Invite(ServiceConfiguration cfg, uint me, byte[] payload)
    {
        (ulong accountId, string name, string email, string tag) = Target(payload);
        (uint Id, string Tag)? person = await Find(cfg, accountId, tag, email, name);
        if (person == null)
            return Fail(ErrorUndeliverable, "SendInvitation");
        if (person.Value.Id == me)
            return Fail(ErrorInvalidTarget, "SendInvitation");
        if (await Blocked(cfg, person.Value.Id, me))
            return Fail(ErrorBlockedByInvitee, "SendInvitation");
        if (await Blocked(cfg, me, person.Value.Id))
            return Fail(ErrorBlocked, "SendInvitation");
        if (await AreFriends(cfg, me, person.Value.Id))
            return Fail(ErrorAlreadyFriends, "SendInvitation");
        if (await InviteId(cfg, me, person.Value.Id) != 0)
            return Fail(ErrorInvitationExists, "SendInvitation");
        if (await Count(cfg, "SELECT COUNT(*) FROM bnot_friend_invitations WHERE inviter_account_id=@id", me) >= MaxPending)
            return Fail(ErrorTooManySent, "SendInvitation");
        if (await Count(cfg, "SELECT COUNT(*) FROM bnot_friends WHERE account_id=@id", me) >= MaxFriends)
            return Fail(ErrorInviterFull, "SendInvitation");
        ulong theirs = await InviteId(cfg, person.Value.Id, me);
        if (theirs != 0)
            return await Accept(cfg, me, theirs);

        uint created = Now();
        ulong id = await InsertInvite(cfg, me, person.Value.Id, created);
        if (id == 0)
            return Fail(ErrorInternal, "SendInvitation");
        string mine = await TagOf(cfg, me);
        var call = new BnetCall { Command = "SendInvitation " + person.Value.Tag };
        Push(call, me, 5, Note(me, SentInvite(id, person.Value.Tag, created)));
        Push(call, person.Value.Id, 3, Note(person.Value.Id, ReceivedInvite(id, me, mine, created)));
        Log.outInfo(LogFilter.SessionRpc, "World friends invite " + me + " -> " + person.Value.Id + " (" + person.Value.Tag + ")");
        return call;
    }

    static async Task<BnetCall> Accept(ServiceConfiguration cfg, uint me, ulong invitationId)
    {
        uint inviter = await Inviter(cfg, invitationId, me);
        if (inviter == 0)
            return Fail(ErrorInvalidInvitation, "AcceptInvitation");
        if (await Count(cfg, "SELECT COUNT(*) FROM bnot_friends WHERE account_id=@id", me) >= MaxFriends)
            return Fail(ErrorInviteeFull, "AcceptInvitation");
        uint created = Now();
        await AcceptRows(cfg, inviter, me, created);
        string mine = await TagOf(cfg, me);
        string theirs = await TagOf(cfg, inviter);
        var call = new BnetCall { Command = "AcceptInvitation" };
        Push(call, me, 4, Assignment(me, invitationId, 0, true));
        Push(call, inviter, 6, Assignment(inviter, invitationId, 0, true));
        Push(call, me, 1, Note(me, Friend(inviter, theirs, created, "")));
        Push(call, inviter, 1, Note(inviter, Friend(me, mine, created, "")));
        await AddPresence(cfg, call, me, inviter, theirs);
        await AddPresence(cfg, call, inviter, me, mine);
        Log.outInfo(LogFilter.SessionRpc, "World friends accept " + me + " and " + inviter);
        return call;
    }

    static async Task<BnetCall> RemoveInvite(ServiceConfiguration cfg, uint me, ulong invitationId, bool asInvitee, uint reason)
    {
        (uint Inviter, uint Invitee) pair = await Pair(cfg, invitationId, me, asInvitee);
        if (pair.Inviter == 0)
            return Fail(ErrorInvalidInvitation, asInvitee ? "IgnoreInvitation" : "RevokeInvitation");
        await ExecId(cfg, "DELETE FROM bnot_friend_invitations WHERE id=@id", invitationId);
        var call = new BnetCall { Command = asInvitee ? "IgnoreInvitation" : "RevokeInvitation" };
        Push(call, pair.Inviter, 6, Assignment(pair.Inviter, invitationId, reason, true));
        Push(call, pair.Invitee, 4, Assignment(pair.Invitee, invitationId, reason, true));
        return call;
    }

    static async Task<BnetCall> RevokeAll(ServiceConfiguration cfg, uint me)
    {
        var ids = new List<ulong>();
        await using (MySqlConnection conn = Open(cfg))
        {
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT id FROM bnot_friend_invitations WHERE inviter_account_id=@id", conn);
            cmd.Parameters.AddWithValue("@id", me);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(reader.GetUInt64(0));
        }
        BnetCall call = Ok("RevokeAllInvitations");
        foreach (ulong id in ids)
        {
            BnetCall one = await RemoveInvite(cfg, me, id, false, 2);
            if (one.Status != 0)
                return one;
            call.Notes.AddRange(one.Notes);
        }
        return call;
    }

    static async Task<BnetCall> Remove(ServiceConfiguration cfg, uint me, ulong otherId)
    {
        uint other = (uint)otherId;
        if (!await AreFriends(cfg, me, other))
            return Fail(ErrorNotFriends, "RemoveFriend");
        await ExecPair(cfg, "DELETE FROM bnot_friends WHERE (account_id=@a AND friend_account_id=@b) OR (account_id=@b AND friend_account_id=@a)", me, other);
        var call = new BnetCall { Command = "RemoveFriend" };
        Push(call, me, 2, Assignment(me, other, 0, false));
        Push(call, other, 2, Assignment(other, me, 0, false));
        return call;
    }

    static async Task<BnetCall> Update(ServiceConfiguration cfg, uint me, byte[] payload)
    {
        uint other = (uint)Field(payload, 1);
        if (!await AreFriends(cfg, me, other))
            return Fail(ErrorNotFriends, "UpdateFriendState");
        var assignment = new ProtoWriter();
        assignment.Varint(1, other);
        if (TryNote(payload, out string note))
        {
            if (note.Length > 127)
                return Fail(ErrorNoteTooLong, "UpdateFriendState");
            await using MySqlConnection conn = Open(cfg);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("UPDATE bnot_friends SET note=@note WHERE account_id=@a AND friend_account_id=@b", conn);
            cmd.Parameters.AddWithValue("@note", note);
            cmd.Parameters.AddWithValue("@a", me);
            cmd.Parameters.AddWithValue("@b", other);
            await cmd.ExecuteNonQueryAsync();
            assignment.String(4, note);
        }
        var call = new BnetCall { Command = "UpdateFriendState" };
        Push(call, me, 7, Note(me, assignment));
        return call;
    }

    static async Task AddPresence(ServiceConfiguration cfg, BnetCall call, uint subscriber, uint friendId, string tag, bool? knownOnline = null)
    {
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000UL;
        bool local = Game.WorldSession.BnetSessions.TryGetValue(friendId, out Game.WorldSession? session);
        BnetCharacter character = new();
        bool playing = local && session != null && session.TryBnetCharacter(out character);
        bool online = local || (knownOnline ?? await Online(cfg, friendId));
        var states = new List<ProtoWriter> { AccountState(friendId, tag, online, playing, character.GameAccountId, now) };
        if (playing)
            states.Add(GameState(friendId, tag, character, now));
        call.Notes.Add(new BnetNote
        {
            AccountId = subscriber,
            Service = PresenceListener,
            Method = Listen | 1,
            Body = Presence(subscriber, states)
        });
    }

    static ProtoWriter AccountState(uint id, string tag, bool online, bool playing, uint gameAccountId, ulong now)
    {
        var state = new ProtoWriter();
        state.Varint(1, id);
        state.Varint(4, now);
        if (tag.Length > 0)
            Field(state, ProgramBN, 1, 4, now, Text(tag), false);
        Field(state, ProgramBN, 1, 6, now, Int((long)now), false);
        Field(state, ProgramBN, 1, 12, now, Bool(false), false);
        if (playing)
        {
            var entry = new ProtoWriter();
            entry.Message(1, GameHandle(gameAccountId));
            entry.Varint(2, now);
            var onlineAccounts = new ProtoWriter();
            onlineAccounts.Message(1, entry);
            Field(state, ProgramBN, 1, 21, now, Blob(GameHandle(gameAccountId).ToArray()), false);
            Field(state, ProgramBN, 1, 20, now, Blob(onlineAccounts.ToArray()), false);
        }
        else
        {
            Field(state, ProgramBN, 1, 21, now, null, true);
            Field(state, ProgramBN, 1, 20, now, Blob([]), false);
        }
        Field(state, ProgramBN, 1, 22, now, Uint(online ? 1ul : 0ul), false);
        Field(state, ProgramBN, 1, 23, now, Int((long)now), false);
        return state;
    }

    static ProtoWriter GameState(uint bnetId, string tag, BnetCharacter character, ulong now)
    {
        var state = new ProtoWriter();
        state.Varint(1, bnetId);
        state.Message(2, GameHandle(character.GameAccountId));
        state.Varint(4, now);
        Field(state, ProgramBN, 2, 1, now, Bool(true), false);
        Field(state, ProgramBN, 2, 4, now, Int((long)now), false);
        if (tag.Length > 0)
            Field(state, ProgramBN, 2, 5, now, Text(tag), false);
        Field(state, ProgramBN, 2, 6, now, Text(character.AccountName), false);
        Field(state, ProgramBN, 2, 10, now, Bool(false), false);
        Field(state, ProgramBN, 2, 12, now, Bool(false), false);
        Field(state, ProgramBN, 2, 13, now, Uint(1), false);
        Field(state, ProgramBN, 2, 15, now, Uint(PlatformWin), false);
        Field(state, ProgramBN, 2, 17, now, Uint(1), false);
        Field(state, ProgramBN, 2, 20, now, Uint(bnetId), false);
        Field(state, ProgramBN, 2, 21, now, Text("WoW"), false);
        Field(state, ProgramWoW, 2, 1, now, Uint(character.RealmAddress), false);
        Field(state, ProgramWoW, 2, 2, now, Uint(character.Race), false);
        Field(state, ProgramWoW, 2, 3, now, Uint(character.Class), false);
        Field(state, ProgramWoW, 2, 5, now, Uint(character.Level), false);
        Field(state, ProgramWoW, 2, 6, now, Uint(character.Zone), false);
        Field(state, ProgramWoW, 2, 7, now, Int(Faction(character.Race)), false);
        Field(state, ProgramWoW, 2, 8, now, Uint(character.RealmAddress), false);
        Field(state, ProgramWoW, 2, 9, now, Uint(character.Gender), false);
        Field(state, ProgramWoW, 2, 10, now, Text(character.Name), false);
        Field(state, ProgramWoW, 2, 11, now, Uint(character.RealmId), false);
        Field(state, ProgramWoW, 2, 12, now, Uint(character.GuidLow), false);
        Field(state, ProgramWoW, 2, 13, now, Bool(false), false);
        Field(state, ProgramWoW, 2, 14, now, Uint(18), false);
        Field(state, ProgramWoW, 2, 15, now, Int(0), false);
        return state;
    }

    static long Faction(byte race) => race is 1 or 3 or 4 or 7 or 11 or 22 or 25 or 29 or 30 or 32 or 34 or 37 or 52 ? 3 : 5;

    static byte[] Presence(uint subscriber, List<ProtoWriter> states)
    {
        var note = new ProtoWriter();
        note.Varint(1, subscriber);
        foreach (ProtoWriter state in states)
            note.Message(2, state);
        return note.ToArray();
    }

    static ProtoWriter GameHandle(uint gameAccountId)
    {
        var handle = new ProtoWriter();
        handle.Varint(1, gameAccountId);
        handle.Varint(2, ProgramWoW);
        handle.Varint(3, 1);
        return handle;
    }

    static void Field(ProtoWriter state, uint program, uint group, uint field, ulong now, ProtoWriter? value, bool deleted)
    {
        var row = new ProtoWriter();
        var key = new ProtoWriter();
        key.Varint(1, program);
        key.Varint(2, group);
        key.Varint(3, field);
        key.Varint(4, 0);
        row.Message(1, key);
        if (value != null && !deleted)
            row.Message(2, value);
        if (deleted)
            row.Bool(3, true);
        row.Varint(4, now);
        state.Message(3, row);
    }

    static ProtoWriter Text(string value) { var w = new ProtoWriter(); w.String(4, value); return w; }
    static ProtoWriter Int(long value) { var w = new ProtoWriter(); w.Varint(2, (ulong)value); return w; }
    static ProtoWriter Bool(bool value) { var w = new ProtoWriter(); w.Bool(1, value); return w; }
    static ProtoWriter Uint(ulong value) { var w = new ProtoWriter(); w.Varint(6, value); return w; }
    static ProtoWriter Blob(byte[] value) { var w = new ProtoWriter(); w.Bytes(5, value); return w; }

    static void Push(BnetCall call, uint account, uint method, ProtoWriter body)
    {
        call.Notes.Add(new BnetNote { AccountId = account, Service = ListenerHash, Method = Listen | method, Body = body.ToArray() });
    }

    static ProtoWriter Note(uint agent, ProtoWriter inner)
    {
        var note = new ProtoWriter();
        note.Varint(1, agent);
        note.Message(2, inner);
        return note;
    }

    static ProtoWriter Assignment(uint agent, ulong id, uint reason, bool invitation)
    {
        var row = new ProtoWriter();
        row.Varint(1, id);
        if (invitation)
            row.Varint(2, reason);
        return Note(agent, row);
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

    static BnetCall Ok(string command) => new() { Command = command };
    static BnetCall Fail(uint status, string command) => new() { Status = status, Command = command };

    static MySqlConnection Open(ServiceConfiguration cfg) => new(cfg.Database.Connection(cfg.Database.Auth));

    static async Task<bool> Missing(MySqlConnection conn, string table, string column)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column", conn);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0) == 0;
    }

    static async Task Exec(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<(uint Id, string Tag)?> Find(ServiceConfiguration cfg, ulong accountId, string tag, string email, string name)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        MySqlCommand cmd;
        if (accountId != 0)
        {
            cmd = new MySqlCommand("SELECT id, IFNULL(battle_tag,'') FROM bnot_accounts WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", accountId);
        }
        else
        {
            string key = (tag.Length > 0 ? tag : email.Length > 0 ? email : name).Trim().ToUpperInvariant();
            if (key.Length == 0)
                return null;
            cmd = new MySqlCommand("SELECT id, IFNULL(battle_tag,'') FROM bnot_accounts WHERE UPPER(battle_tag)=@key OR email=@key LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@key", key);
        }
        await using (cmd)
        await using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return null;
            return (reader.GetUInt32(0), reader.GetString(1));
        }
    }

    static async Task<bool> AreFriends(ServiceConfiguration cfg, uint a, uint b)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT 1 FROM bnot_friends WHERE account_id=@a AND friend_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@a", a);
        cmd.Parameters.AddWithValue("@b", b);
        return await cmd.ExecuteScalarAsync() != null;
    }

    static async Task<bool> Blocked(ServiceConfiguration cfg, uint owner, uint other)
    {
        try
        {
            await using MySqlConnection conn = Open(cfg);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT 1 FROM bnot_blocks WHERE account_id=@a AND blocked_account_id=@b", conn);
            cmd.Parameters.AddWithValue("@a", owner);
            cmd.Parameters.AddWithValue("@b", other);
            object? value = await cmd.ExecuteScalarAsync();
            return value != null && value is not DBNull;
        }
        catch
        {
            return false;
        }
    }

    static async Task<ulong> InviteId(ServiceConfiguration cfg, uint inviter, uint invitee)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT id FROM bnot_friend_invitations WHERE inviter_account_id=@a AND invitee_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@a", inviter);
        cmd.Parameters.AddWithValue("@b", invitee);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? 0 : Convert.ToUInt64(value);
    }

    static async Task<long> Count(ServiceConfiguration cfg, string sql, uint id)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0);
    }

    static async Task<ulong> InsertInvite(ServiceConfiguration cfg, uint inviter, uint invitee, uint created)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO bnot_friend_invitations (inviter_account_id, invitee_account_id, created) VALUES (@a, @b, @created)", conn);
        cmd.Parameters.AddWithValue("@a", inviter);
        cmd.Parameters.AddWithValue("@b", invitee);
        cmd.Parameters.AddWithValue("@created", created);
        await cmd.ExecuteNonQueryAsync();
        if (cmd.LastInsertedId > 0)
            return (ulong)cmd.LastInsertedId;
        return await InviteId(cfg, inviter, invitee);
    }

    static async Task<uint> Inviter(ServiceConfiguration cfg, ulong invitationId, uint invitee)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT inviter_account_id FROM bnot_friend_invitations WHERE id=@id AND invitee_account_id=@invitee", conn);
        cmd.Parameters.AddWithValue("@id", invitationId);
        cmd.Parameters.AddWithValue("@invitee", invitee);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? 0 : Convert.ToUInt32(value);
    }

    static async Task<(uint Inviter, uint Invitee)> Pair(ServiceConfiguration cfg, ulong invitationId, uint me, bool asInvitee)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        string column = asInvitee ? "invitee_account_id" : "inviter_account_id";
        await using var cmd = new MySqlCommand(
            "SELECT inviter_account_id, invitee_account_id FROM bnot_friend_invitations WHERE id=@id AND " + column + "=@me", conn);
        cmd.Parameters.AddWithValue("@id", invitationId);
        cmd.Parameters.AddWithValue("@me", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (0, 0);
        return (reader.GetUInt32(0), reader.GetUInt32(1));
    }

    static async Task AcceptRows(ServiceConfiguration cfg, uint inviter, uint invitee, uint created)
    {
        await ExecPair(cfg, "DELETE FROM bnot_friend_invitations WHERE (inviter_account_id=@a AND invitee_account_id=@b) OR (inviter_account_id=@b AND invitee_account_id=@a)", inviter, invitee);
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO bnot_friends (account_id, friend_account_id, created) VALUES (@a, @b, @created), (@b, @a, @created) ON DUPLICATE KEY UPDATE created=created", conn);
        cmd.Parameters.AddWithValue("@a", inviter);
        cmd.Parameters.AddWithValue("@b", invitee);
        cmd.Parameters.AddWithValue("@created", created);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task ExecId(ServiceConfiguration cfg, string sql, ulong id)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task ExecPair(ServiceConfiguration cfg, string sql, uint a, uint b)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@a", a);
        cmd.Parameters.AddWithValue("@b", b);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<string> TagOf(ServiceConfiguration cfg, uint id)
    {
        await using MySqlConnection conn = Open(cfg);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT IFNULL(battle_tag,'') FROM bnot_accounts WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? "" : value.ToString() ?? "";
    }

    static async Task<bool> Online(ServiceConfiguration cfg, uint id)
    {
        try
        {
            await using MySqlConnection conn = Open(cfg);
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand("SELECT 1 FROM account WHERE battlenet_account=@id AND online<>0 LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteScalarAsync() != null;
        }
        catch
        {
            return false;
        }
    }

    static async Task<HashSet<uint>> OnlineFriends(ServiceConfiguration cfg, IEnumerable<uint> friendIds)
    {
        uint[] ids = friendIds.Where(id => id != 0).Distinct().ToArray();
        var online = ids.Where(id => Game.WorldSession.BnetSessions.ContainsKey(id)).ToHashSet();
        uint[] queryIds = ids.Where(id => !online.Contains(id)).ToArray();
        if (queryIds.Length == 0)
            return online;

        try
        {
            await using MySqlConnection conn = Open(cfg);
            await conn.OpenAsync();
            string parameters = string.Join(",", queryIds.Select((_, index) => $"@friend{index}"));
            await using var cmd = new MySqlCommand(
                $"SELECT DISTINCT battlenet_account FROM account WHERE online<>0 AND battlenet_account IN ({parameters})", conn);
            for (int i = 0; i < queryIds.Length; i++)
                cmd.Parameters.AddWithValue($"@friend{i}", queryIds[i]);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                online.Add(reader.GetUInt32(0));
        }
        catch
        {
            // Presence is best-effort, as in the single-account fallback above.
        }
        return online;
    }

    static (ulong Account, string Name, string Email, string Tag) Target(byte[] payload)
    {
        foreach (ProtoField field in ProtoField.Walk(payload))
        {
            if (field.Field != 1 || field.Wire != 2)
                continue;
            ulong account = 0;
            string name = "", email = "", tag = "";
            foreach (ProtoField part in ProtoField.Walk(field.Bytes))
            {
                if (part.Field == 1 && part.Wire == 2)
                    name = Encoding.UTF8.GetString(part.Bytes);
                else if (part.Field == 10 && part.Wire == 0)
                    account = part.Varint;
                else if (part.Field == 11 && part.Wire == 2)
                    email = Encoding.UTF8.GetString(part.Bytes);
                else if (part.Field == 12 && part.Wire == 2)
                    tag = Encoding.UTF8.GetString(part.Bytes);
            }
            return (account, name, email, tag);
        }
        return (0, "", "", "");
    }

    static bool TryNote(byte[] payload, out string note)
    {
        note = "";
        foreach (ProtoField field in ProtoField.Walk(payload))
        {
            if (field.Field != 2 || field.Wire != 2)
                continue;
            foreach (ProtoField part in ProtoField.Walk(field.Bytes))
            {
                if (part.Field == 2 && part.Wire == 2)
                {
                    note = Encoding.UTF8.GetString(part.Bytes);
                    return true;
                }
            }
        }
        return false;
    }

    static ulong Field(byte[] payload, int wanted)
    {
        foreach (ProtoField field in ProtoField.Walk(payload))
            if (field.Field == wanted && field.Wire == 0)
                return field.Varint;
        return 0;
    }

    static uint Now() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}


