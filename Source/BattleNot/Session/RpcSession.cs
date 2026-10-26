using System.Buffers;
using System.Net;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Framework.Configuration;

namespace BattleNot;

sealed class PlayerSession
{
    const int MaxRpcPayload = 1_048_576;
    readonly BnotDb _db;
    readonly RealmCatalog _realms;
    readonly string _ip;
    readonly bool _devCert;
    uint _token;
    string _ciid = "";
    bool _authed;
    bool _receivedPacket;
    BnetAccount? _account;
    SslStream? _stream;
    readonly SemaphoreSlim _write = new(1, 1);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, PlayerSession> Live = new();
    GameAccount? _game;
    uint _build;
    string _platform = "";
    string _locale = "enUS";
    short _timezone;
    byte[] _clientSecret = new byte[32];
    uint _platformType;
    uint _clientArch;
    uint _buildType;
    sealed class RealmJoinCredential(byte[] serverSecret, byte[] sessionKey, long expiresAt)
    {
        public byte[] ServerSecret { get; } = serverSecret;
        public byte[] SessionKey { get; } = sessionKey;
        public long ExpiresAt { get; } = expiresAt;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<(uint GameId, string ClientFingerprint), RealmJoinCredential> RealmJoinCredentials = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, SemaphoreSlim> RealmJoinWriteGates = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, RealmJoinCredential> PersistedRealmJoinCredentials = new();

    public bool ReceivedPacket => _receivedPacket;

    public PlayerSession(BnotDb db, RealmCatalog realms, string ip, bool devCert)
    {
        _db = db;
        _realms = realms;
        _ip = ip;
        _devCert = devCert;
    }

    public async Task Run(SslStream stream, NetworkStream raw)
    {
        _stream = stream;
        byte[] pending = ArrayPool<byte>.Shared.Rent(65536);
        byte[] scratch = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
        int length = 0;
        while (true)
        {
            int read = await stream.ReadAsync(scratch);
            if (read == 0)
                return;
            length = Append(ref pending, length, scratch, read);
            while (raw.DataAvailable)
            {
                read = await stream.ReadAsync(scratch);
                if (read == 0)
                    return;
                length = Append(ref pending, length, scratch, read);
            }
            int offset = 0;
            ReadOnlyMemory<byte> input = pending.AsMemory(0, length);
            while (TryTakePacket(input, ref offset, out ReadOnlyMemory<byte> packet))
                await Handle(stream, packet);
            if (offset > 0)
            {
                Buffer.BlockCopy(pending, offset, pending, 0, length - offset);
                length -= offset;
            }
        }
        }
        finally
        {
        ArrayPool<byte>.Shared.Return(pending, clearArray: true);
        ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
            if (_account != null && Live.TryGetValue(_account.Id, out PlayerSession? current) && current == this)
                Live.TryRemove(_account.Id, out _);
            _stream = null;
        }
    }

    static int Append(ref byte[] pending, int length, byte[] data, int count)
    {
        if (length + count > pending.Length)
        {
            byte[] resized = ArrayPool<byte>.Shared.Rent(Math.Max(pending.Length * 2, length + count));
            Buffer.BlockCopy(pending, 0, resized, 0, length);
            ArrayPool<byte>.Shared.Return(pending, clearArray: true);
            pending = resized;
        }
        Buffer.BlockCopy(data, 0, pending, length, count);
        return length + count;
    }

    static bool TryTakePacket(ReadOnlyMemory<byte> data, ref int offset, out ReadOnlyMemory<byte> packet)
    {
        packet = default;
        if (data.Length - offset < 2)
            return false;
        ReadOnlySpan<byte> span = data.Span;
        int headerSize = (span[offset] << 8) | span[offset + 1];
        if (headerSize is < 1 or > 4096)
            throw new InvalidDataException($"bad battlenet header length {headerSize}");
        if (data.Length - offset < 2 + headerSize)
            return false;
        uint size = 0;
        var reader = new ProtoReader(data.Slice(offset + 2, headerSize));
        while (reader.Next(out int field, out int wire))
        {
            if (field == 5 && wire == 0)
                size = (uint)reader.ReadVarint();
            else
                reader.Skip(wire);
        }
        if (size > MaxRpcPayload)
            throw new InvalidDataException($"oversized battlenet payload {size}");
        int total = 2 + headerSize + (int)size;
        if (data.Length - offset < total)
            return false;
        packet = data.Slice(offset, total);
        offset += total;
        return true;
    }

    async Task Handle(SslStream stream, ReadOnlyMemory<byte> packet)
    {
        _receivedPacket = true;
        int headerSize = (packet.Span[0] << 8) | packet.Span[1];
        var reader = new ProtoReader(packet.Slice(2, headerSize));
        uint serviceId = 0, method = 0, token = 0, hash = 0, size = 0;
        while (reader.Next(out int field, out int wire))
        {
            switch (field)
            {
                case 1 when wire == 0: serviceId = (uint)reader.ReadVarint(); break;
                case 2 when wire == 0: method = (uint)reader.ReadVarint(); break;
                case 3 when wire == 0: token = (uint)reader.ReadVarint(); break;
                case 5 when wire == 0: size = (uint)reader.ReadVarint(); break;
                case 11 when wire == 5: hash = reader.ReadFixed32(); break;
                default: reader.Skip(wire); break;
            }
        }
        if (serviceId == Rpc.ResponseServiceId)
            return;
        byte[] payload = packet.Slice(2 + headerSize, (int)size).ToArray();
        method &= 0x3FFFFFFF;
        long dispatchStarted = Stopwatch.GetTimestamp();
        try
        {
            await Dispatch(stream, hash, method, token, payload);
        }
        catch (Exception ex)
        {
            Log.outError(LogFilter.Network, $"RPC 0x{hash:X8} method {method} failed: {ex.Message}");
            await Respond(stream, token, Rpc.ErrorRpcMalformedRequest, null);
        }
        TimeSpan dispatchTime = Stopwatch.GetElapsedTime(dispatchStarted);
        if (dispatchTime >= TimeSpan.FromMilliseconds(100))
            Log.outWarn(LogFilter.Network, $"RPC slow {_ip} service 0x{hash:X8} method {method} {dispatchTime.TotalMilliseconds:0}ms");
    }

    async Task Dispatch(SslStream stream, uint hash, uint method, uint token, byte[] payload)
    {
        if (hash == Rpc.Connection)
        {
            await Connection(stream, method, token, payload);
            return;
        }
        if (hash == Rpc.AuthV1)
        {
            await AuthV1(stream, method, token, payload);
            return;
        }
        if (hash == Rpc.AuthV2)
        {
            await AuthV2(stream, method, token, payload);
            return;
        }
        if (hash == Rpc.AccountV1 || hash == Rpc.AccountV2)
        {
            await Account(stream, hash, method, token, payload);
            return;
        }
        if (hash == Rpc.GameUtilitiesV1 || hash == Rpc.GameUtilitiesV2)
        {
            await Utilities(stream, hash == Rpc.GameUtilitiesV2, method, token, payload);
            return;
        }
        if (hash == 0x138D200C)
        {
            await Respond(stream, token, Rpc.ErrorOk, null);
            return;
        }
        if (hash == BnetFriends.ServiceHash)
        {
            if (!_authed || _account == null)
            {
                await Respond(stream, token, Rpc.ErrorDenied, null);
                return;
            }
            FriendsReply reply = await BnetFriends.Call(_db, _account.Id, method, payload);
            Log.outDebug(LogFilter.Network, $"Friends {_ip} {reply.Command} status 0x{reply.Status:X}");
            await Respond(stream, token, reply.Status, reply.Status == Rpc.ErrorOk ? reply.Body : null);
            if (reply.Status == Rpc.ErrorOk)
                foreach (FriendsPush push in reply.Push)
                    await Push(push.AccountId, push.Service, push.Method, push.Body);
            return;
        }
        if (Rpc.StubServices.Contains(hash))
        {
            await Respond(stream, token, Rpc.ErrorRpcNotImplemented, null);
            return;
        }
        Log.outDebug(LogFilter.Network, $"Unknown service 0x{hash:X8} method {method}");
        await Respond(stream, token, Rpc.ErrorRpcInvalidService, null);
    }

    async Task Connection(SslStream stream, uint method, uint token, byte[] payload)
    {
        if (method == 1)
        {
            uint label = 0, epoch = 0;
            bool hasClient = false, bindless = false;
            foreach (var field in Messages.Fields(payload))
            {
                if (field.Field == 1)
                {
                    hasClient = true;
                    foreach (var id in Messages.Fields(field.Bytes))
                    {
                        if (id.Field == 1) label = (uint)id.Varint;
                        if (id.Field == 2) epoch = (uint)id.Varint;
                    }
                }
                else if (field.Field == 3 && field.Wire == 0)
                    bindless = field.Varint != 0;
            }
            uint serverEpoch = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            uint serverLabel = (uint)Environment.ProcessId;
            if (!hasClient)
            {
                label = token == 0 ? 1u : token;
                epoch = serverEpoch;
            }
            _ciid = $"{serverLabel:X8}{serverEpoch:X8}-{label:X8}{epoch:X8}";
            var client = new ProtoWriter();
            client.Varint(1, label);
            client.Varint(2, epoch);
            var server = new ProtoWriter();
            server.Varint(1, serverLabel);
            server.Varint(2, serverEpoch);
            var response = new ProtoWriter();
            response.Message(1, server);
            response.Message(2, client);
            response.Varint(6, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            response.Bool(7, bindless);
            response.String(9, _ciid);
            await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
            return;
        }
        if (method == 5)
        {
            await Respond(stream, token, Rpc.ErrorOk, []);
            return;
        }
        if (method == 7)
        {
            uint error = 0;
            foreach (var field in Messages.Fields(payload))
                if (field.Field == 1 && field.Wire == 0)
                    error = (uint)field.Varint;
            var note = new ProtoWriter();
            note.Varint(1, error);
            await Request(stream, Rpc.Connection, 4, note.ToArray());
            await stream.FlushAsync();
            return;
        }
        await Respond(stream, token, Rpc.ErrorRpcNotImplemented, null);
    }

    async Task AuthV1(SslStream stream, uint method, uint token, byte[] payload)
    {
        if (method != 1 && method != 7 && method != 8)
        {
            await Respond(stream, token, Rpc.ErrorRpcInvalidMethod, null);
            return;
        }
        if (method == 8)
        {
            if (!_authed || _account == null)
            {
                await Respond(stream, token, Rpc.ErrorDenied, null);
                return;
            }
            string? ticket = await _db.Auth.TicketById(_account.Id);
            var response = new ProtoWriter();
            response.String(1, ticket ?? "");
            await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
            return;
        }
        if (method == 1)
        {
            string program = "", platform = "", locale = "", device = "", cached = "";
            uint version = 0;
            foreach (var field in Messages.Fields(payload))
            {
                switch (field.Field)
                {
                    case 1: program = field.Text; break;
                    case 2: platform = field.Text; break;
                    case 3: locale = field.Text; break;
                    case 6: version = (uint)field.Varint; break;
                    case 12: cached = field.Text; break;
                    case 15: device = field.Text; break;
                }
            }
            uint status = BeginLogon(program == "WoW" ? Rpc.Wow : 0, platform, locale, version, device);
            if (status != Rpc.ErrorOk)
            {
                await Respond(stream, token, status, null);
                return;
            }
            if (cached.Length > 0)
            {
                await Verify(stream, token, cached, v2: false);
                return;
            }
            await SendWebChallenge(stream, v2: false);
            await Respond(stream, token, Rpc.ErrorOk, []);
            return;
        }
        string tokenText = "";
        foreach (var field in Messages.Fields(payload))
            if (field.Field == 1)
                tokenText = field.Text;
        if (tokenText.Length == 0)
        {
            await Respond(stream, token, Rpc.ErrorDenied, null);
            return;
        }
        await Verify(stream, token, tokenText, v2: false);
    }

    async Task AuthV2(SslStream stream, uint method, uint token, byte[] payload)
    {
        if (method is < 1 or > 3)
        {
            await Respond(stream, token, Rpc.ErrorRpcInvalidMethod, null);
            return;
        }
        if (method == 3)
        {
            if (!_authed || _account == null)
            {
                await Respond(stream, token, Rpc.ErrorDenied, null);
                return;
            }
            string? ticket = await _db.Auth.TicketById(_account.Id);
            var response = new ProtoWriter();
            response.String(1, ticket ?? "");
            await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
            return;
        }
        if (method == 1)
        {
            uint title = 0, version = 0;
            string platform = "", locale = "", device = "", cached = "";
            foreach (var field in Messages.Fields(payload))
            {
                switch (field.Field)
                {
                    case 1: title = (uint)field.Varint; break;
                    case 2: platform = field.Text; break;
                    case 3: locale = field.Text; break;
                    case 4: version = (uint)field.Varint; break;
                    case 10:
                        foreach (var option in Messages.Fields(field.Bytes))
                        {
                            if (option.Field == 1) cached = option.Text;
                            if (option.Field == 3) device = option.Text;
                        }
                        break;
                }
            }
            uint status = BeginLogon(title, platform, locale, version, device);
            if (status != Rpc.ErrorOk)
            {
                await Respond(stream, token, status, null);
                return;
            }
            if (cached.Length > 0)
            {
                await Verify(stream, token, cached, v2: true);
                return;
            }
            await SendWebChallenge(stream, v2: true);
            await Respond(stream, token, Rpc.ErrorOk, []);
            return;
        }
        string authToken = "";
        foreach (var field in Messages.Fields(payload))
            if (field.Field == 1)
                authToken = field.Text;
        if (authToken.Length == 0)
        {
            await Respond(stream, token, Rpc.ErrorDenied, null);
            return;
        }
        await Verify(stream, token, authToken, v2: true);
    }

    uint BeginLogon(uint title, string platform, string locale, uint version, string device)
    {
        if (title != Rpc.Wow)
            return Rpc.ErrorBadProgram;
        if (!Text.ValidPlatform(platform))
            return Rpc.ErrorBadPlatform;
        if (Text.Locale(locale) == 255)
            return Rpc.ErrorBadLocale;
        _platform = platform;
        _locale = locale;
        _build = version;
        Log.outDebug(LogFilter.Network, $"Logon build {version} platform {platform} locale {locale}");
        _timezone = 0;
        if (device.Length > 0)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(device);
                if (doc.RootElement.TryGetProperty("UTCO", out JsonElement utco) && utco.TryGetUInt32(out _))
                    _timezone = 0;
            }
            catch
            {
                _timezone = 0;
            }
        }
        return Rpc.ErrorOk;
    }

    async Task SendWebChallenge(SslStream stream, bool v2)
    {
        string host = ConfigMgr.GetDefaultValue(
            IPAddress.IsLoopback(IPAddress.Parse(_ip)) ? "LoginREST.LocalAddress" : "LoginREST.ExternalAddress", "trinity.actual.battle.net");
        int port = ConfigMgr.GetDefaultValue("LoginREST.Port", 8081);
        string url = $"{(_devCert ? "http" : "https")}://{host}:{port}/bnetserver/login/";
        var challenge = new ProtoWriter();
        challenge.String(2, "web_auth_url");
        challenge.String(3, url);
        if (v2)
            await Request(stream, Rpc.AuthListenerV2, 4, challenge.ToArray());
        else
            await Request(stream, Rpc.ChallengeListener, 3, challenge.ToArray());
    }

    async Task Verify(SslStream stream, uint token, string ticket, bool v2)
    {
        BnetAccount? account = await _db.Auth.AccountByTicket(ticket);
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (account == null)
        {
            await Respond(stream, token, Rpc.ErrorDenied, null);
            return;
        }
        if (account.TicketExpiry < now)
        {
            await Respond(stream, token, Rpc.ErrorTimedOut, null);
            return;
        }
        if (account.LockedToIp && account.LastIp.Length > 0 && account.LastIp != _ip)
        {
            await Respond(stream, token, Rpc.ErrorRiskAccountLocked, null);
            return;
        }
        if (account.Banned)
        {
            await Respond(stream, token, account.PermanentlyBanned ? Rpc.ErrorGameAccountBanned : Rpc.ErrorGameAccountSuspended, null);
            return;
        }
        _account = account;
        _authed = true;
        Live[account.Id] = this;
        string tag = "";
        try
        {
            tag = await _db.Tags.GameBattleTag(account.Id, account.Email);
        }
        catch (Exception ex)
        {
            Log.outError(LogFilter.Network, "BattleTag " + ex.Message);
        }
        byte[] sessionKey = RandomNumberGenerator.GetBytes(64);
        await Respond(stream, token, Rpc.ErrorOk, []);
        if (v2)
        {
            var record = new ProtoWriter();
            record.Varint(1, account.Id);
            foreach (GameAccount game in account.Games)
            {
                var handle = new ProtoWriter();
                handle.Varint(1, game.Id);
                handle.Varint(2, Rpc.Wow);
                handle.Varint(3, 2);
                record.Message(2, handle);
            }
            record.Bytes(5, sessionKey);
            if (tag.Length > 0)
                record.String(3, tag);
            var note = new ProtoWriter();
            note.Varint(1, 0);
            note.Message(2, record);
            await Request(stream, Rpc.AuthListenerV2, 1, note.ToArray());
        }
        else
        {
            var accountId = new ProtoWriter();
            accountId.Fixed64(1, 0x100000000000000);
            accountId.Fixed64(2, account.Id);
            var result = new ProtoWriter();
            result.Varint(1, 0);
            result.Message(2, accountId);
            foreach (GameAccount game in account.Games)
            {
                var id = new ProtoWriter();
                id.Fixed64(1, 0x200000200576F57);
                id.Fixed64(2, game.Id);
                result.Message(3, id);
            }
            result.Bytes(9, sessionKey);
            if (tag.Length > 0)
                result.String(7, tag);
            await Request(stream, Rpc.AuthListenerV1, 5, result.ToArray());
        }
        Log.outInfo(LogFilter.Network, $"Battle.not logon {_ip} account {account.Id} ({account.Email}) {tag}, {account.Games.Count} game account(s).");
    }

    async Task Account(SslStream stream, uint hash, uint method, uint token, byte[] payload)
    {
        if (!_authed || _account == null)
        {
            await Respond(stream, token, Rpc.ErrorDenied, null);
            return;
        }
        if (hash == Rpc.AccountV2 && method == 101)
        {
            var info = new ProtoWriter();
            info.Varint(1, _account.Id);
            info.Varint(2, 1);
            string tag = await _db.Tags.GameBattleTag(_account.Id, _account.Email);
            if (tag.Length > 0)
                info.String(5, tag);
            info.String(7, "USA");
            info.Varint(8, 1);
            info.String(9, "enUS");
            info.String(10, "USD");
            info.Varint(11, 1);
            info.Varint(14, 7);
            var response = new ProtoWriter();
            response.Message(1, info);
            await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
            return;
        }
        if (hash == Rpc.AccountV2 && method == 104)
        {
            await Respond(stream, token, Rpc.ErrorOk, []);
            return;
        }
        if (hash == Rpc.AccountV2 && method == 201)
        {
            uint id = 0;
            foreach (var field in Messages.Fields(payload))
                if (field.Field == 1)
                    foreach (var handle in Messages.Fields(field.Bytes))
                        if (handle.Field == 1)
                            id = (uint)handle.Varint;
            GameAccount? game = _account.Games.FirstOrDefault(g => g.Id == id);
            var response = new ProtoWriter();
            if (game != null)
            {
                var info = new ProtoWriter();
                info.Varint(1, game.Id);
                info.String(2, game.DisplayName);
                response.Message(1, info);
            }
            await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
            return;
        }
        if (hash == Rpc.AccountV2 && method == 203)
        {
            uint id = 0;
            foreach (var field in Messages.Fields(payload))
                if (field.Field == 1)
                    foreach (var handle in Messages.Fields(field.Bytes))
                        if (handle.Field == 1)
                            id = (uint)handle.Varint;
            GameAccount? game = _account.Games.FirstOrDefault(g => g.Id == id);
            var response = new ProtoWriter();
            if (game != null)
            {
                if (game.PermanentlyBanned)
                    response.Message(1, Restriction(1, game.BanDate));
                if (game.Banned)
                    response.Message(1, Restriction(2, game.BanDate));
            }
            await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
            return;
        }
        if (hash == Rpc.AccountV1 && method is 30 or 31)
        {
            await Respond(stream, token, Rpc.ErrorOk, []);
            return;
        }
        await Respond(stream, token, method == 0 ? Rpc.ErrorRpcInvalidMethod : Rpc.ErrorRpcNotImplemented, null);
    }

    static ProtoWriter Restriction(uint type, uint banDate)
    {
        var restriction = new ProtoWriter();
        restriction.Varint(2, Rpc.Wow);
        restriction.Varint(3, type);
        restriction.Varint(5, (ulong)banDate * 1000);
        return restriction;
    }

    async Task Utilities(SslStream stream, bool v2, uint method, uint token, byte[] payload)
    {
        if (!_authed || _account == null)
        {
            await Respond(stream, token, Rpc.ErrorDenied, null);
            return;
        }
        bool clientRequest = v2 ? method == 1 : method == 1;
        bool values = v2 ? method == 2 : method == 10;
        if (!clientRequest && !values)
        {
            await Respond(stream, token, Rpc.ErrorRpcNotImplemented, null);
            return;
        }
        if (values)
        {
            string key = "";
            foreach (var field in Messages.Fields(payload))
                if (field.Field == 1)
                    key = field.Text;
            key = Messages.Command(key);
            var response = new ProtoWriter();
            if (key is "Command_RealmListRequest" or "Command_RealmListRequest_v1")
            {
                foreach (string subRegion in _realms.Snapshot().Select(realm => realm.SubRegion).Distinct())
                    response.Message(1, Messages.Variant(v2, Kind.String, text: subRegion));
                await Respond(stream, token, Rpc.ErrorOk, response.ToArray());
                return;
            }
            await Respond(stream, token, Rpc.ErrorRpcNotImplemented, null);
            return;
        }

        var parameters = new List<(string Name, Variant Value)>();
        foreach (var field in Messages.Fields(payload))
        {
            if (field.Field != 1)
                continue;
            string name = "";
            Variant value = default;
            foreach (var attr in Messages.Fields(field.Bytes))
            {
                if (attr.Field == 1)
                    name = attr.Text;
                else if (attr.Field == 2)
                    value = Messages.ReadVariant(v2, attr.Bytes);
            }
            if (name.Length > 0)
                parameters.Add((Messages.Command(name), value));
        }
        (uint status, List<(string Name, Variant Value)> valuesOut) = await ClientRequest(parameters);
        if (status != Rpc.ErrorOk)
        {
            await Respond(stream, token, status, null);
            return;
        }
        var body = new ProtoWriter();
        foreach (var (name, value) in valuesOut)
        {
            var attr = new ProtoWriter();
            attr.String(1, name);
            attr.Message(2, Messages.WriteVariant(v2, value));
            body.Message(v2 ? 1 : 1, attr);
        }
        await Respond(stream, token, Rpc.ErrorOk, body.ToArray());
    }

    async Task<(uint Status, List<(string, Variant)> Values)> ClientRequest(List<(string Name, Variant Value)> parameters)
    {
        string command = parameters.Select(p => p.Name).FirstOrDefault(n => n.StartsWith("Command_", StringComparison.Ordinal)) ?? "";
        string bare = Messages.Bare(command);
        var values = new List<(string, Variant)>();
        switch (bare)
        {
            case "Command_LastCharPlayedRequest":
                return (LastChar(parameters, values), values);
            case "Command_RealmListTicketRequest":
                return (await RealmTicket(parameters, values), values);
            case "Command_RealmListRequest":
                return (RealmList(parameters, values), values);
            case "Command_RealmJoinRequest":
                return (await JoinRealm(parameters, values), values);
            case "Command_FetchBleepProxiesRequest":
                values.Add(("Param_BleepProxyList", Blob(JsonText.Compress("JSONBleepProxyList:{}"))));
                return (Rpc.ErrorOk, values);
            case "Command_SuperDistrictListRequest":
                values.Add(("Param_SuperDistrictList", Blob(JsonText.Compress("JSONSuperDistrictList:{\"superDistricts\":[" + _realms.SuperDistrictEntries() + "]}"))));
                return (Rpc.ErrorOk, values);
            default:
                Log.outDebug(LogFilter.Network, $"Unknown game utility command '{command}'.");
                return (Rpc.ErrorRpcNotImplemented, values);
        }
    }

    uint LastChar(List<(string Name, Variant Value)> parameters, List<(string, Variant)> values)
    {
        if (_game == null)
            return Rpc.ErrorUtilUnknownRealm;
        Variant? sub = Find(parameters, "Command_LastCharPlayedRequest_v1");
        if (sub?.Kind != Kind.String || sub.Value.Text == null)
            return Rpc.ErrorUtilUnknownRealm;
        LastPlayed? played = null;
        if (_game.LastPlayed.TryGetValue(sub.Value.Text, out LastPlayed? found))
            played = found;
        Variant? filter = Find(parameters, "Param_ContentSetIDFilter");
        if (filter != null && Number(filter.Value, out long content) && content >= 0)
        {
            RealmEntry? ruleset = _realms.FindContentSet((uint)content);
            if (ruleset != null && _game.LastPlayed.TryGetValue(ruleset.SubRegion, out LastPlayed? rulesetChar))
                played = rulesetChar;
        }
        else if (played == null)
        {
            // With several realms every one is its own sub-region, and the login request (no ruleset filter) names whichever one
            // the client selected (70-1-73, Hardcore). An empty answer there sends the client to the ruleset picker, so answer with
            // the most recent character on any realm; its realm entry carries the superDistrictID the client then joins.
            played = _game.LastPlayed.Values
                .Where(p => _realms.Find(p.RealmAddress) is RealmEntry r && _realms.RealmEntryJson(r, _build, _game.Security).Length > 0)
                .MaxBy(p => p.Time);
        }
        if (played != null)
        {
            RealmEntry? realm = _realms.Find(played.RealmAddress);
            string json = realm == null ? "" : _realms.RealmEntryJson(realm, _build, _game.Security);
            if (json.Length == 0)
                return Rpc.ErrorUtilSerialize;
            values.Add(("Param_RealmEntry", Blob(JsonText.Compress("JamJSONRealmEntry:" + json))));
            values.Add(("Param_CharacterName", TextVar(played.Name)));
            values.Add(("Param_CharacterGUID", Blob(BitConverter.GetBytes(played.Guid))));
            values.Add(("Param_LastPlayedTime", Int(played.Time)));
            values.Add(("Param_UtilityInfo", Blob(JsonText.Compress("JSONUtilityInfo:{\"realmPermissions\":512}"))));
            return Rpc.ErrorOk;
        }
        if (filter != null && Number(filter.Value, out long contentSet) && contentSet >= 0)
        {
            RealmEntry? realm = _realms.FindContentSet((uint)contentSet) ?? _realms.FirstOnline();
            if (realm != null)
            {
                string json = _realms.RealmEntryJson(realm, _build, _game.Security);
                if (json.Length > 0)
                {
                    values.Add(("Param_RealmEntry", Blob(JsonText.Compress("JamJSONRealmEntry:" + json))));
                    values.Add(("Param_LastPlayedTime", Int(DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
                    values.Add(("Param_UtilityInfo", Blob(JsonText.Compress("JSONUtilityInfo:{\"realmPermissions\":512}"))));
                }
            }
        }
        return Rpc.ErrorOk;
    }

    async Task<uint> RealmTicket(List<(string Name, Variant Value)> parameters, List<(string, Variant)> values)
    {
        string? identityJson = PayloadText(Find(parameters, "Param_Identity"));
        if (identityJson == null)
        {
            Log.outWarn(LogFilter.Network, "RealmListTicket denied: identity missing. " + Describe(parameters));
            return Rpc.ErrorUtilInvalidIdentity;
        }
        int colon = identityJson.IndexOf(':');
        if (colon < 0)
            return Rpc.ErrorUtilInvalidIdentity;
        uint gameId;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(identityJson[(colon + 1)..].TrimEnd('\0'));
            if (!doc.RootElement.TryGetProperty("gameAccountID", out JsonElement id))
                return Rpc.ErrorUtilInvalidIdentity;
            gameId = id.ValueKind == JsonValueKind.String ? uint.Parse(id.GetString()!) : id.GetUInt32();
        }
        catch (Exception ex)
        {
            Log.outWarn(LogFilter.Network, $"RealmListTicket denied: identity JSON ({ex.Message}).");
            return Rpc.ErrorUtilInvalidIdentity;
        }
        GameAccount? game = _account!.Games.FirstOrDefault(g => g.Id == gameId);
        if (game == null)
        {
            Log.outWarn(LogFilter.Network, $"RealmListTicket denied: game account {gameId} is not on this login.");
            return Rpc.ErrorUtilInvalidIdentity;
        }
        if (game.PermanentlyBanned)
            return Rpc.ErrorGameAccountBanned;
        if (game.Banned)
            return Rpc.ErrorGameAccountSuspended;
        string? infoJson = PayloadText(Find(parameters, "Param_ClientInfo"));
        if (infoJson == null || !ReadClientInfo(infoJson))
        {
            Log.outWarn(LogFilter.Network, "RealmListTicket denied: client secret was not 32 bytes.");
            return Rpc.ErrorDeniedRealmListTicket;
        }
        _game = game;
        await _db.Auth.UpdateLastLogin(_account.Id, _ip, Text.Locale(_locale), _platform);
        values.Add(("Param_RealmListTicket", Blob(Encoding.UTF8.GetBytes("AuthRealmListTicket\0"))));
        Log.outDebug(LogFilter.Network, $"RealmListTicket {game.Name} build {_build}");
        return Rpc.ErrorOk;
    }

    static string Describe(List<(string Name, Variant Value)> parameters) =>
        string.Join(", ", parameters.Select(p => p.Name + ":" + p.Value.Kind));

    static string? PayloadText(Variant? value)
    {
        if (value?.Kind == Kind.Blob && value.Value.Blob != null)
            return Encoding.UTF8.GetString(value.Value.Blob);
        if (value?.Kind == Kind.String)
            return value.Value.Text;
        return null;
    }

    bool ReadClientInfo(string json)
    {
        int colon = json.IndexOf(':');
        if (colon < 0)
            return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json[(colon + 1)..].TrimEnd('\0'));
            if (!doc.RootElement.TryGetProperty("info", out JsonElement info))
                return false;
            if (!info.TryGetProperty("secret", out JsonElement secret) || !TrySecret(secret, out byte[] raw) || raw.Length != 32)
                return false;
            _clientSecret = raw;
            if (info.TryGetProperty("platformType", out JsonElement platform))
                _platformType = platform.GetUInt32();
            if (info.TryGetProperty("clientArch", out JsonElement arch))
                _clientArch = arch.GetUInt32();
            if (info.TryGetProperty("type", out JsonElement type))
                _buildType = type.GetUInt32();
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool TrySecret(JsonElement secret, out byte[] raw)
    {
        if (secret.ValueKind == JsonValueKind.Array)
        {
            raw = new byte[secret.GetArrayLength()];
            int i = 0;
            foreach (JsonElement item in secret.EnumerateArray())
                raw[i++] = (byte)item.GetUInt32();
            return true;
        }
        if (secret.ValueKind == JsonValueKind.String)
        {
            raw = DecodeSecret(secret.GetString() ?? "");
            return true;
        }
        raw = [];
        return false;
    }

    static byte[] DecodeSecret(string text)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch
        {
            return Convert.FromHexString(text);
        }
    }

    uint RealmList(List<(string Name, Variant Value)> parameters, List<(string, Variant)> values)
    {
        if (_game == null)
            return Rpc.ErrorDenied;
        Variant? sub = Find(parameters, "Command_RealmListRequest_v1");
        if (sub?.Kind != Kind.String || sub.Value.Text == null)
            return Rpc.ErrorUtilSerialize;
        byte[] list = _realms.RealmListBlob(_build, _game.Security, sub.Value.Text);
        if (list.Length == 0)
            return Rpc.ErrorUtilSerialize;
        var counts = new StringBuilder();
        if (_game.CharacterCounts.Count == 0)
            counts.Append("{}");
        else
        {
            counts.Append("{\"counts\":[");
            bool first = true;
            foreach (var (address, count) in _game.CharacterCounts)
            {
                if (!first)
                    counts.Append(',');
                first = false;
                counts.Append("{\"wowRealmAddress\":").Append(address).Append(",\"count\":").Append(count).Append('}');
            }
            counts.Append("]}");
        }
        values.Add(("Param_RealmList", Blob(list)));
        values.Add(("Param_CharacterCountList", Blob(JsonText.Compress("JSONRealmCharacterCountList:" + counts))));
        return Rpc.ErrorOk;
    }

    async Task<uint> JoinRealm(List<(string Name, Variant Value)> parameters, List<(string, Variant)> values)
    {
        if (_game is not GameAccount game)
            return Rpc.ErrorDenied;
        Variant? address = Find(parameters, "Param_RealmAddress");
        if (address == null || !Number(address.Value, out long realmAddress) || realmAddress < 0)
            return Rpc.ErrorUtilUnknownRealm;
        RealmEntry? realm = _realms.Find((uint)realmAddress);
        if (realm == null)
            return Rpc.ErrorUtilUnknownRealm;
        if (realm.PopulationState == 0 || realm.Build != _build || game.Security < realm.AllowedSecurity)
        {
            Log.outDebug(LogFilter.Network, realm.WorldUp
                ? $"Realm join refused {game.Name} -> {realm.Name}"
                : $"Realm join refused {game.Name} -> {realm.Name}: world server is not accepting connections on port {realm.Port}");
            return Rpc.ErrorUserNotPermitted;
        }
        IPAddress ip = realm.AddressFor(IPAddress.Parse(_ip));
        int family = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 2 : 1;
        byte[] addresses = JsonText.Compress("JSONRealmListServerIPAddresses:{\"families\":[{\"family\":" + family +
            ",\"addresses\":[{\"ip\":" + JsonText.Quote(ip.ToString()) + ",\"port\":" + realm.Port + "}]}]}");
        if (addresses.Length == 0)
            return Rpc.ErrorUtilSerialize;
        // A client may open multiple Battle.net RPC connections and issue the same
        // RealmJoin concurrently. The auth schema has one session-key slot per game
        // account, so generating a new key for every RPC can invalidate the earlier
        // response before that client's world socket reaches AuthSession. Keep the
        // credential stable for short-lived retries from the same client secret.
        string clientFingerprint = Convert.ToHexString(SHA256.HashData(_clientSecret));
        var credentialKey = (game.Id, clientFingerprint);
        long now = Environment.TickCount64;
        RealmJoinCredential credential;
        while (true)
        {
            if (RealmJoinCredentials.TryGetValue(credentialKey, out credential!) && credential.ExpiresAt > now)
                break;

            byte[] server = RandomNumberGenerator.GetBytes(32);
            byte[] session = new byte[64];
            _clientSecret.CopyTo(session, 0);
            server.CopyTo(session, 32);
            var created = new RealmJoinCredential(server, session, now + (long)TimeSpan.FromMinutes(2).TotalMilliseconds);
            if (RealmJoinCredentials.TryAdd(credentialKey, created))
            {
                credential = created;
                break;
            }
            if (RealmJoinCredentials.TryGetValue(credentialKey, out RealmJoinCredential? existing)
                && existing.ExpiresAt <= now
                && RealmJoinCredentials.TryUpdate(credentialKey, created, existing))
            {
                credential = created;
                PersistedRealmJoinCredentials.TryRemove(game.Id, out _);
                break;
            }
        }

        SemaphoreSlim writeGate = RealmJoinWriteGates.GetOrAdd(game.Id, static _ => new SemaphoreSlim(1, 1));
        await writeGate.WaitAsync();
        try
        {
            if (!PersistedRealmJoinCredentials.TryGetValue(game.Id, out RealmJoinCredential? persisted)
                || !ReferenceEquals(persisted, credential))
            {
                await _db.Auth.UpdateGameLogin(game.Name, credential.SessionKey, _ip, _build, Text.Locale(_locale), _platform, _timezone);
                PersistedRealmJoinCredentials[game.Id] = credential;
            }
        }
        finally
        {
            writeGate.Release();
        }
        string ticket = "{\"gameAccount\":" + JsonText.Quote(game.Name) + ",\"platform\":" + _platformType +
                        ",\"type\":" + _buildType + ",\"clientArch\":" + _clientArch + "}";
        values.Add(("Param_RealmJoinTicket", Blob(Encoding.UTF8.GetBytes(ticket))));
        values.Add(("Param_ServerAddresses", Blob(addresses)));
        values.Add(("Param_JoinSecret", Blob(credential.ServerSecret)));
        Log.outInfo(LogFilter.Network, $"Realm join {game.Name} -> {realm.Name} {ip}:{realm.Port}");
        return Rpc.ErrorOk;
    }

    static Variant? Find(List<(string Name, Variant Value)> parameters, string name)
    {
        string bare = Messages.Bare(name);
        foreach (var parameter in parameters)
            if (parameter.Name == name || parameter.Name == bare || Messages.Bare(parameter.Name) == bare)
                return parameter.Value;
        return null;
    }

    static bool Number(Variant value, out long number)
    {
        switch (value.Kind)
        {
            case Kind.Int: number = value.Int; return true;
            case Kind.Uint: number = (long)value.Uint; return true;
            case Kind.Float: number = (long)value.Float; return true;
            default: number = 0; return false;
        }
    }

    static Variant Blob(byte[] data) => new(Kind.Blob, false, 0, 0, null, data, 0);
    static Variant TextVar(string text) => new(Kind.String, false, 0, 0, text, null, 0);
    static Variant Int(long value) => new(Kind.Int, false, value, 0, null, null, 0);

    async Task Respond(SslStream stream, uint token, uint status, byte[]? body)
    {
        var header = new ProtoWriter();
        header.Varint(1, Rpc.ResponseServiceId);
        header.Varint(3, token);
        if (status == Rpc.ErrorOk)
            header.Varint(5, (ulong)(body?.Length ?? 0));
        else
            header.Varint(6, status);
        if (_ciid.Length > 0)
            header.String(13, _ciid);
        await WritePacket(stream, header.ToArray(), status == Rpc.ErrorOk ? body ?? [] : []);
    }

    async Task Request(SslStream stream, uint hash, uint method, byte[] body)
    {
        var header = new ProtoWriter();
        header.Varint(1, 0);
        header.Varint(2, method);
        header.Varint(3, _token++);
        header.Varint(5, (ulong)body.Length);
        header.Fixed32(11, hash);
        if (_ciid.Length > 0)
            header.String(13, _ciid);
        await WritePacket(stream, header.ToArray(), body);
    }

    async Task WritePacket(SslStream stream, byte[] header, byte[] body)
    {
        byte[] frame = new byte[2 + header.Length + body.Length];
        frame[0] = (byte)(header.Length >> 8);
        frame[1] = (byte)header.Length;
        header.CopyTo(frame, 2);
        body.CopyTo(frame, 2 + header.Length);
        await _write.WaitAsync();
        try
        {
            await stream.WriteAsync(frame);
            await stream.FlushAsync();
        }
        finally
        {
            _write.Release();
        }
    }

    public static async Task Push(uint accountId, uint hash, uint method, byte[] body)
    {
        if (!Live.TryGetValue(accountId, out PlayerSession? session) || session._stream == null)
            return;
        await session.Request(session._stream, hash, method, body);
    }
}

enum Kind { None, Bool, Int, Float, String, Blob, Uint }

readonly record struct Variant(Kind Kind, bool Bool, long Int, float Float, string? Text, byte[]? Blob, ulong Uint);

static class Messages
{
    public readonly record struct Part(int Field, int Wire, ulong Varint, byte[] Bytes, string Text);

    public static List<Part> Fields(byte[] data)
    {
        var list = new List<Part>();
        var reader = new ProtoReader(data);
        while (reader.Next(out int field, out int wire))
        {
            switch (wire)
            {
                case 0:
                    list.Add(new Part(field, wire, reader.ReadVarint(), [], ""));
                    break;
                case 1:
                    list.Add(new Part(field, wire, reader.ReadFixed64(), [], ""));
                    break;
                case 2:
                    byte[] bytes = reader.ReadBytes();
                    list.Add(new Part(field, wire, 0, bytes, Encoding.UTF8.GetString(bytes)));
                    break;
                case 5:
                    list.Add(new Part(field, wire, reader.ReadFixed32(), [], ""));
                    break;
                default:
                    reader.Skip(wire);
                    break;
            }
        }
        return list;
    }

    public static string Command(string name)
    {
        if (!name.StartsWith("Command_", StringComparison.Ordinal))
            return name;
        int split = name.LastIndexOf('_');
        return split > 0 ? name[..split] : name;
    }

    public static string Bare(string name) => name.StartsWith("Command_", StringComparison.Ordinal) ? Command(name) : name;

    public static Variant ReadVariant(bool v2, byte[] data)
    {
        foreach (Part field in Fields(data))
        {
            int boolean = v2 ? 1 : 2;
            int integer = v2 ? 2 : 3;
            int floating = v2 ? 3 : 4;
            int text = v2 ? 4 : 5;
            int blob = v2 ? 5 : 6;
            int unsigned = v2 ? 6 : 9;
            if (field.Field == boolean && field.Wire == 0)
                return new Variant(Kind.Bool, field.Varint != 0, 0, 0, null, null, 0);
            if (field.Field == integer && field.Wire == 0)
                return new Variant(Kind.Int, false, (long)field.Varint, 0, null, null, 0);
            if (field.Field == floating && field.Wire == 5)
                return new Variant(Kind.Float, false, 0, BitConverter.UInt32BitsToSingle((uint)field.Varint), null, null, 0);
            if (field.Field == text)
                return new Variant(Kind.String, false, 0, 0, field.Text, null, 0);
            if (field.Field == blob)
                return new Variant(Kind.Blob, false, 0, 0, null, field.Bytes, 0);
            if (field.Field == unsigned && field.Wire == 0)
                return new Variant(Kind.Uint, false, 0, 0, null, null, field.Varint);
        }
        return default;
    }

    public static ProtoWriter WriteVariant(bool v2, Variant value)
    {
        var writer = new ProtoWriter();
        switch (value.Kind)
        {
            case Kind.Bool: writer.Bool(v2 ? 1 : 2, value.Bool); break;
            case Kind.Int: writer.Varint(v2 ? 2 : 3, (ulong)value.Int); break;
            case Kind.Float: writer.Fixed32(v2 ? 3 : 4, BitConverter.SingleToUInt32Bits(value.Float)); break;
            case Kind.String: writer.String(v2 ? 4 : 5, value.Text ?? ""); break;
            case Kind.Blob: writer.Bytes(v2 ? 5 : 6, value.Blob ?? []); break;
            case Kind.Uint: writer.Varint(v2 ? 6 : 9, value.Uint); break;
        }
        return writer;
    }

    public static ProtoWriter Variant(bool v2, Kind kind, string? text = null) =>
        WriteVariant(v2, new Variant(kind, false, 0, 0, text, null, 0));
}

sealed class BnetHost
{
    readonly BnotDb _db;
    readonly RealmCatalog _realms;
    readonly X509Certificate2 _certificate;
    readonly bool _devCert;

    public BnetHost(BnotDb db, RealmCatalog realms, string certificateFile, string privateKeyFile)
    {
        _db = db;
        _realms = realms;
        using X509Certificate2 pem = X509Certificate2.CreateFromPemFile(certificateFile, privateKeyFile);
        _certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
        _devCert = _certificate.GetNameInfo(X509NameType.SimpleName, false) == "*.*";
    }

    public bool DevCertificate => _devCert;
    public X509Certificate2 Certificate => _certificate;

    public async Task Listen(CancellationToken cancel)
    {
        IPAddress bindIP = IPAddress.Parse(ConfigMgr.GetDefaultValue("BindIP", "127.0.0.1"));
        int port = ConfigMgr.GetDefaultValue("BattlenetPort", 1119);
        var listener = new TcpListener(bindIP, port);
        listener.Start();
        Log.outInfo(LogFilter.Network, $"Battle.not RPC {bindIP}:{port}");
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancel);
                _ = Accept(client);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    async Task Accept(TcpClient client)
    {
        string ip = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address.ToString() ?? "0.0.0.0";
        var session = new PlayerSession(_db, _realms, ip, _devCert);
        using (client)
        {
            try
            {
                if (await _db.Auth.IsIpBanned(ip))
                {
                    Log.outWarn(LogFilter.Network, $"Rejected banned IP {ip}");
                    return;
                }
                NetworkStream raw = client.GetStream();
                await using var stream = new SslStream(raw, false);
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                });
                Log.outDebug(LogFilter.Network, $"TLS session {ip}");
                await session.Run(stream, raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (session.ReceivedPacket || !ClientClosedEarly(ex))
                    Log.outError(LogFilter.Network, $"Session {ip} closed: {ex.Message}");
            }
        }
    }

    static bool ClientClosedEarly(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur.Message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase)
                || cur.Message.Contains("0 bytes from the transport", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
