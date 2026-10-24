using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Framework.Configuration;

namespace BattleNot;

static class Text
{
    public static string UpperLatin(string value)
    {
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is >= 'a' and <= 'z')
                chars[i] -= (char)32;
        return new string(chars);
    }

    public static string SrpUsername(string login) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(login)));

    public static byte Locale(string name) => name switch
    {
        "enUS" => 0,
        "koKR" => 1,
        "frFR" => 2,
        "deDE" => 3,
        "zhCN" => 4,
        "zhTW" => 5,
        "esES" => 6,
        "esMX" => 7,
        "ruRU" => 8,
        "ptBR" => 10,
        "itIT" => 11,
        _ => 255
    };

    public static bool ValidPlatform(string platform) => platform is "Win" or "Wn64" or "WinA" or "Mac" or "Mc64" or "MacA";

    public static string DisplayName(string username)
    {
        int hash = username.IndexOf('#');
        return hash >= 0 ? "WoW" + username[(hash + 1)..] : username;
    }

    public static string Ticket()
    {
        Span<byte> bytes = stackalloc byte[20];
        RandomNumberGenerator.Fill(bytes);
        return "TC-" + Convert.ToHexString(bytes);
    }
}

sealed class LoginRest
{
    readonly BnotDb _db;
    readonly RealmCatalog _realms;
    readonly bool _http;
    string _announcedVersion = "";

    public LoginRest(BnotDb db, RealmCatalog realms, bool devWildcard)
    {
        _db = db;
        _realms = realms;
        _http = devWildcard;
    }

    public void Map(WebApplication app)
    {
        app.MapGet("/bnetserver/login/", (Delegate)Form);
        app.MapGet("/bnetserver/gameAccounts/", (Delegate)GameAccounts);
        app.MapGet("/bnetserver/portal/", (Delegate)Portal);
        app.MapGet("/bnetserver/versions/", (Delegate)Versions);
        app.MapGet("/bnetserver/realms/", (Delegate)Realms);
        app.MapGet("/bnetserver/version/", (Delegate)Version);
        app.MapPost("/bnetserver/login/", (Delegate)Login);
        app.MapPost("/bnetserver/login/srp/", (Delegate)Srp);
        app.MapPost("/bnetserver/login/authenticator/", (Delegate)Authenticator);
        app.MapPost("/bnetserver/account/create/", (Delegate)Create);
        app.MapPost("/bnetserver/refreshLoginTicket/", (Delegate)Refresh);
    }

    string HostFor(IPAddress remote) => ConfigMgr.GetDefaultValue(
        IPAddress.IsLoopback(remote) ? "LoginREST.LocalAddress" : "LoginREST.ExternalAddress", "trinity.actual.battle.net");
    string Scheme => _http ? "http" : "https";

    IResult Form(HttpContext ctx)
    {
        SessionId(ctx);
        string host = HostFor(ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback);
        int port = ConfigMgr.GetDefaultValue("LoginREST.Port", 8081);
        string url = $"{Scheme}://{host}:{port}/bnetserver/login/srp/";
        string json =
            "{\"type\":\"LOGIN_FORM\",\"inputs\":[" +
            "{\"input_id\":\"account_name\",\"type\":\"text\",\"label\":\"E-mail\",\"max_length\":320}," +
            "{\"input_id\":\"password\",\"type\":\"password\",\"label\":\"Password\",\"max_length\":128}," +
            "{\"input_id\":\"log_in_submit\",\"type\":\"submit\",\"label\":\"Log In\"}]," +
            "\"srp_url\":" + JsonText.Quote(url) + "}";
        return Json(json);
    }

    IResult Version(HttpContext ctx)
    {
        const string product = "wow_forever";
        const string platform = "win64";
        BuildInfo? target = _realms.TargetVersion();
        if (target == null)
            return Json(VersionJson(product, "", 0, platform, "unavailable", true));
        string version = target.Major + "." + target.Minor + "." + target.Revision + "." + target.Build;
        if (version != _announcedVersion)
        {
            _announcedVersion = version;
            Log.outDebug(LogFilter.Http, "Client version: " + product + " " + version + " win64");
        }
        string client = ctx.Request.Query["client"].ToString();
        string asked = ctx.Request.Query["platform"].ToString();
        bool wrongPlatform = asked.Length > 0 && !string.Equals(asked, platform, StringComparison.OrdinalIgnoreCase);
        bool wrongBuild = client.Length > 0 && !string.Equals(client, version, StringComparison.OrdinalIgnoreCase);
        string status = wrongPlatform ? "unavailable" : wrongBuild ? "patch" : "playable";
        return Json(VersionJson(product, version, target.Build, platform, status, wrongPlatform || wrongBuild));
    }

    static string VersionJson(string product, string version, uint build, string platform, string status, bool patchRequired)
    {
        return "{\"product\":" + JsonText.Quote(product) +
            ",\"version\":" + JsonText.Quote(version) +
            ",\"build\":" + build +
            ",\"platform\":" + JsonText.Quote(platform) +
            ",\"status\":" + JsonText.Quote(status) +
            ",\"download\":\"\"" +
            ",\"patchRequired\":" + (patchRequired ? "true" : "false") + "}";
    }

    IResult Versions()
    {
        var parts = new List<string>();
        foreach (BuildInfo info in _realms.ClientVersions())
        {
            string version = info.Major + "." + info.Minor + "." + info.Revision + "." + info.Build;
            parts.Add("{\"version\":" + JsonText.Quote(version) + ",\"build\":" + info.Build + "}");
        }
        return Json("{\"versions\":[" + string.Join(",", parts) + "]}");
    }

    IResult Realms(HttpContext ctx)
    {
        var parts = new List<string>();
        uint major, minor, revision, build;
        if (TryVersion(ctx.Request.Query["version"].ToString(), out major, out minor, out revision, out build))
        {
            foreach (RealmEntry realm in _realms.OnlineForVersion(major, minor, revision, build))
            {
                string version;
                _realms.TryVersionText(realm.Build, out version);
                string address = realm.Addresses.Count > 0 ? realm.Addresses[0].ToString() : "";
                parts.Add("{\"id\":" + realm.Id +
                    ",\"name\":" + JsonText.Quote(realm.Name) +
                    ",\"address\":" + JsonText.Quote(address) +
                    ",\"port\":" + realm.Port +
                    ",\"type\":" + realm.Type +
                    ",\"population\":" + realm.Population.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"status\":\"online\"" +
                    ",\"version\":" + JsonText.Quote(version) +
                    ",\"build\":" + realm.Build +
                    ",\"flags\":" + realm.LegacyFlags +
                    ",\"ruleset\":" + JsonText.Quote(realm.RulesetLabel()) +
                    ",\"hardcore\":" + (realm.Hardcore ? "true" : "false") +
                    ",\"contentSetId\":" + realm.ContentSetId + "}");
            }
        }
        return Json("{\"realms\":[" + string.Join(",", parts) + "]}");
    }

    static bool TryVersion(string text, out uint major, out uint minor, out uint revision, out uint build)
    {
        major = minor = revision = build = 0;
        if (string.IsNullOrEmpty(text))
            return false;
        string[] parts = text.Split('.');
        if (parts.Length != 4)
            return false;
        return uint.TryParse(parts[0], out major)
            && uint.TryParse(parts[1], out minor)
            && uint.TryParse(parts[2], out revision)
            && uint.TryParse(parts[3], out build);
    }

    IResult Portal(HttpContext ctx)
    {
        string host = HostFor(ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback);
        int port = ConfigMgr.GetDefaultValue("BattlenetPort", 1119);
        return Results.Text($"{host}:{port}", "text/plain");
    }

    async Task<IResult> GameAccounts(HttpContext ctx)
    {
        string? ticket = Authorization(ctx);
        if (string.IsNullOrEmpty(ticket))
            return Results.StatusCode(401);
        var accounts = await _db.Auth.GameAccounts(ticket);
        var parts = new List<string>();
        foreach (var account in accounts)
        {
            string row = "{\"display_name\":" + JsonText.Quote(Text.DisplayName(account.Name)) + ",\"expansion\":" + account.Expansion;
            if (account.Suspended || account.Banned)
                row += ",\"is_suspended\":" + (account.Suspended ? "true" : "false") + ",\"is_banned\":" + (account.Banned ? "true" : "false") +
                       ",\"suspension_reason\":" + JsonText.Quote(account.Reason) + ",\"suspension_expires\":" + account.Expires;
            parts.Add(row + "}");
        }
        return Json("{\"game_accounts\":[" + string.Join(",", parts) + "]}");
    }

    async Task<IResult> Srp(HttpContext ctx)
    {
        JsonDocument? doc = await ReadJson(ctx);
        if (doc == null)
            return LoginError(400, "LOGIN", "UNABLE_TO_DECODE", "There was an internal error while connecting to Battle.not. Please try again later.");
        string login = Text.UpperLatin(Input(doc, "account_name"));
        var row = await _db.Auth.PasswordByEmail(login);
        if (row == null || row.Value.Verifier.Length == 0)
            return Json("{\"authentication_state\":\"DONE\"}");
        string username = Text.SrpUsername(login);
        var srp = new BnetSrp(row.Value.Version, username, row.Value.Salt, row.Value.Verifier);
        StoreSrp(ctx, login, srp);
        string json =
            "{\"version\":" + srp.Version +
            ",\"iterations\":" + srp.Iterations +
            ",\"modulus\":" + JsonText.Quote(srp.Modulus) +
            ",\"generator\":" + JsonText.Quote(srp.Generator) +
            ",\"hash_function\":\"SHA-256\"" +
            ",\"username\":" + JsonText.Quote(srp.Username) +
            ",\"salt\":" + JsonText.Quote(srp.SaltHex) +
            ",\"public_B\":" + JsonText.Quote(srp.PublicB) + "}";
        return Json(json);
    }

    async Task<IResult> Login(HttpContext ctx)
    {
        JsonDocument? doc = await ReadJson(ctx);
        if (doc == null)
            return LoginError(400, "LOGIN", "UNABLE_TO_DECODE", "There was an internal error while connecting to Battle.not. Please try again later.");
        string login = Text.UpperLatin(Input(doc, "account_name"));
        AuthRow? row = await _db.Auth.Authentication(login);
        if (row == null)
            return Json("{\"authentication_state\":\"DONE\"}");

        bool ok = false;
        string? m2 = null;
        BnetSrp? srp = TakeSrp(ctx, login);
        string publicA = Input(doc, "public_A");
        string clientM1 = Input(doc, "client_evidence_M1");
        if (srp == null)
        {
            Log.outDebug(LogFilter.Http, $"Login {login}: no SRP session from the challenge (cookie/connection miss).");
            if (row.Verifier.Length == 0)
                return Json("{\"authentication_state\":\"DONE\"}");
            srp = new BnetSrp(row.Version, Text.SrpUsername(login), row.Salt, row.Verifier);
            string password = Input(doc, "password");
            if (row.Version == 1)
                password = Text.UpperLatin(password);
            ok = srp.CheckPassword(Text.SrpUsername(login), password);
        }
        else if (srp.TryProve(publicA, clientM1, out string evidence))
        {
            ok = true;
            m2 = evidence;
        }
        else
            Log.outWarn(LogFilter.Http, $"Login {login}: SRP evidence rejected (public_A {publicA.Length} hex chars, M1 {clientM1.Length} hex chars).");

        LoginState state = States[SessionId(ctx)];
        if (!ok)
        {
            int maxCount = ConfigMgr.GetDefaultValue("WrongPass.MaxCount", 0);
            if (!row.Banned && maxCount > 0)
            {
                await _db.Auth.NoteFailedLogin(row.Id);
                if (row.FailedLogins + 1 >= maxCount)
                {
                    string ip = (ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
                    await _db.Auth.BanIp(ip, ConfigMgr.GetDefaultValue("WrongPass.BanTime", 600));
                    await _db.Auth.ResetFailedLogins(row.Id);
                }
            }
            return Json("{\"authentication_state\":\"DONE\"}");
        }

        if (row.AuthenticatorSecret.Length > 0)
        {
            state.AuthAccount = row.Id;
            state.AuthSecret = row.AuthenticatorSecret;
            state.AuthM2 = m2 ?? "";
            string typed = Input(doc, "authenticator_input");
            if (typed.Length == 0)
            {
                state.AuthTries = 0;
                return Json(AuthenticatorJson(ctx, m2));
            }
            return await FinishAuthenticator(ctx, state, typed);
        }

        string ticket = row.Ticket;
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ticket.Length == 0 || row.TicketExpiry < now)
            ticket = Text.Ticket();
        uint expiry = now + (uint)ConfigMgr.GetDefaultValue("LoginREST.TicketDuration", 3600);
        await _db.Auth.SetTicket(row.Id, ticket, expiry);
        string json = "{\"authentication_state\":\"DONE\",\"login_ticket\":" + JsonText.Quote(ticket);
        if (m2 != null)
            json += ",\"server_evidence_M2\":" + JsonText.Quote(m2);
        return Json(json + "}");
    }

    async Task<IResult> Authenticator(HttpContext ctx)
    {
        JsonDocument? doc = await ReadJson(ctx);
        if (doc == null)
            return LoginError(400, "LOGIN", "UNABLE_TO_DECODE", "There was an internal error while connecting to Battle.not. Please try again later.");
        if (!States.TryGetValue(SessionId(ctx), out LoginState? state) || state.AuthAccount == 0)
            return Json("{\"authentication_state\":\"DONE\"}");
        return await FinishAuthenticator(ctx, state, Input(doc, "authenticator_input"));
    }

    async Task<IResult> FinishAuthenticator(HttpContext ctx, LoginState state, string code)
    {
        if (state.AuthAccount == 0)
            return Json("{\"authentication_state\":\"DONE\"}");
        if (!Totp.Matches(state.AuthSecret, code))
        {
            state.AuthTries++;
            Log.outWarn(LogFilter.Http, "Authenticator rejected for account " + state.AuthAccount + " (" + state.AuthTries + ").");
            if (state.AuthTries >= 5)
            {
                state.AuthAccount = 0;
                state.AuthSecret = "";
                return Json("{\"authentication_state\":\"DONE\"}");
            }
            return Json(AuthenticatorJson(ctx, null));
        }
        uint id = state.AuthAccount;
        string m2 = state.AuthM2;
        state.AuthAccount = 0;
        state.AuthSecret = "";
        state.AuthM2 = "";
        string ticket = Text.Ticket();
        uint expiry = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (uint)ConfigMgr.GetDefaultValue("LoginREST.TicketDuration", 3600);
        await _db.Auth.SetTicket(id, ticket, expiry);
        string json = "{\"authentication_state\":\"DONE\",\"login_ticket\":" + JsonText.Quote(ticket);
        if (m2.Length > 0)
            json += ",\"server_evidence_M2\":" + JsonText.Quote(m2);
        Log.outDebug(LogFilter.Http, "Authenticator accepted for account " + id + ".");
        return Json(json + "}");
    }

    string AuthenticatorJson(HttpContext ctx, string? m2)
    {
        string next = ctx.Request.Scheme + "://" + ctx.Request.Host + "/bnetserver/login/authenticator/";
        string json = "{\"authentication_state\":\"AUTHENTICATOR\",\"next_url\":" + JsonText.Quote(next);
        if (!string.IsNullOrEmpty(m2))
            json += ",\"server_evidence_M2\":" + JsonText.Quote(m2);
        return json + "}";
    }

    async Task<IResult> Create(HttpContext ctx)
    {
        JsonDocument? doc = await ReadJson(ctx);
        if (doc == null)
            return Json("{\"ok\":false,\"error\":\"bad\"}");
        using (doc)
        {
            string username = Input(doc, "username");
            string email = Input(doc, "email");
            string? error = await _db.Auth.CreateAccount(username, email, Input(doc, "password"));
            if (error != null)
            {
                Log.outError(LogFilter.Http, "Account create refused (" + error + ").");
                return Json("{\"ok\":false,\"error\":" + JsonText.Quote(error) + "}");
            }
            Log.outDebug(LogFilter.Http, "Account created " + Text.UpperLatin(email) + " / " + Text.UpperLatin(username) + ".");
            return Json("{\"ok\":true}");
        }
    }

    async Task<IResult> Refresh(HttpContext ctx)
    {
        string? ticket = Authorization(ctx);
        if (string.IsNullOrEmpty(ticket))
            return Results.StatusCode(401);
        uint? expiry = await _db.Auth.TicketExpiry(ticket);
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expiry == null || expiry <= now)
            return Json("{\"is_expired\":true}");
        uint next = now + (uint)ConfigMgr.GetDefaultValue("LoginREST.TicketDuration", 3600);
        await _db.Auth.RefreshTicket(ticket, next);
        return Json("{\"login_ticket_expiry\":" + next + "}");
    }

    static async Task<JsonDocument?> ReadJson(HttpContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    static string Input(JsonDocument doc, string id)
    {
        if (!doc.RootElement.TryGetProperty("inputs", out JsonElement inputs))
            return "";
        foreach (JsonElement input in inputs.EnumerateArray())
        {
            if (input.TryGetProperty("input_id", out JsonElement name) && name.GetString() == id &&
                input.TryGetProperty("value", out JsonElement value))
                return value.GetString() ?? "";
        }
        return "";
    }

    static string? Authorization(HttpContext ctx)
    {
        if (!ctx.Request.Headers.TryGetValue("Authorization", out var header))
            return null;
        string value = header.ToString();
        if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            value = value[6..];
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            int colon = decoded.IndexOf(':');
            return colon >= 0 ? decoded[..colon] : decoded;
        }
        catch
        {
            return null;
        }
    }

    sealed class LoginState
    {
        public BnetSrp? Srp;
        public uint AuthAccount;
        public string AuthSecret = "";
        public string AuthM2 = "";
        public int AuthTries;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, LoginState> States = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ConnectionToState = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BnetSrp> SrpByEmail = new();

    string SessionId(HttpContext ctx)
    {
        string? cookie = CookieValue(ctx, "JSESSIONID");
        if (cookie != null && States.ContainsKey(cookie))
        {
            ConnectionToState[ctx.Connection.Id] = cookie;
            return cookie;
        }
        if (ConnectionToState.TryGetValue(ctx.Connection.Id, out string? existing))
            return existing;

        string id = Guid.NewGuid().ToString();
        States[id] = new LoginState();
        ConnectionToState[ctx.Connection.Id] = id;
        ctx.Response.Cookies.Append("JSESSIONID", id, new CookieOptions
        {
            Path = "/bnetserver",
            Domain = ctx.Request.Host.Host,
            Secure = !_http,
            HttpOnly = true,
            SameSite = SameSiteMode.None
        });
        return id;
    }

    void StoreSrp(HttpContext ctx, string email, BnetSrp srp)
    {
        States[SessionId(ctx)].Srp = srp;
        SrpByEmail[email] = srp;
    }

    BnetSrp? TakeSrp(HttpContext ctx, string email)
    {
        if (States.TryGetValue(SessionId(ctx), out LoginState? state) && state.Srp != null)
            return state.Srp;
        return SrpByEmail.TryGetValue(email, out BnetSrp? srp) ? srp : null;
    }

    static string? CookieValue(HttpContext ctx, string name)
    {
        if (ctx.Request.Cookies.TryGetValue(name, out string? parsed) && !string.IsNullOrEmpty(parsed))
            return parsed;
        if (!ctx.Request.Headers.TryGetValue("Cookie", out var header))
            return null;
        foreach (string part in header.ToString().Split(';'))
        {
            string item = part.Trim();
            if (item.StartsWith(name + "=", StringComparison.Ordinal))
                return item[(name.Length + 1)..];
        }
        return null;
    }

    static IResult Json(string json) => Results.Text(json, "application/json;charset=utf-8");

    static IResult LoginError(int status, string state, string code, string message) =>
        Results.Text(
            "{\"authentication_state\":\"" + state + "\",\"error_code\":" + JsonText.Quote(code) + ",\"error_message\":" + JsonText.Quote(message) + "}",
            "application/json;charset=utf-8",
            Encoding.UTF8,
            status);
}
