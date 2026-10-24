using System.Net;
using System.Text;
using MySqlConnector;

namespace BattleNot;

// Classic 1.60 opens these from the heap, after the launcher points the hosts at this process:
//   breaking-news.support.blizzard.com/service/...          server alert
//   account.battle.net/login/sso?token=&ref=                in-game browser sign-in
//   {region}.battle.net/support/{locale}/help|ticket/...    support window
//   support-{region}.stech.blizzard.net/{locale}/help/home
//   telemetry-in.battle.net and wow.ingest.gdp.blizzard.com
//   {region}.version.battle.net/v2/products/...
//   nydus.battle.net/WoW/...
sealed partial class SupportRest
{
    readonly BnotDb _db;

    public SupportRest(BnotDb db) => _db = db;

    public void Map(WebApplication app)
    {
        MapShopAdministration(app);
        app.MapMethods("/service/{product}/{env}/{region}/{locale}", new[] { "GET", "HEAD" }, (Delegate)Alert);
        app.MapMethods("/login/sso", new[] { "GET", "POST" }, (Delegate)Sso);
        app.MapMethods("/sso", new[] { "GET", "POST" }, (Delegate)Sso);
        app.MapGet("/", (Delegate)Root);
        app.MapMethods("/kb", new[] { "GET", "HEAD" }, (Delegate)Root);
        app.MapGet("/overview", (Delegate)Account);
        app.MapGet("/payment", (Delegate)Account);
        app.MapGet("/privacy", (Delegate)Account);
        app.MapGet("/transactions", (Delegate)Account);
        app.MapGet("/support", (Delegate)HelpBare);
        // Blizzard's embedded support UI varies its URL by client build. Keep the
        // known ticket routes above, but serve the support landing page for other
        // /support/... pages instead of letting Kestrel return an error page.
        app.MapMethods("/support/{locale}/{*rest}", new[] { "GET", "HEAD" }, (Delegate)HelpFallback);
        app.MapGet("/support/{locale}/help/home", (Delegate)Help);
        app.MapGet("/support/{locale}/games/{*rest}", (Delegate)Help);
        app.MapMethods("/support/{locale}/ticket/submit", new[] { "GET", "POST" }, (Delegate)Submit);
        app.MapMethods("/support/{locale}/ticket/status", new[] { "GET", "POST" }, (Delegate)Status);
        app.MapMethods("/help/ticket", new[] { "GET", "POST" }, (Delegate)SubmitDefault);
        app.MapMethods("/help/tickets", new[] { "GET", "POST" }, (Delegate)StatusDefault);
        app.MapGet("/{locale}/help/home", (Delegate)Help);
        app.MapMethods("/data", new[] { "GET", "POST" }, (Delegate)Sink);
        app.MapMethods("/flowrules", new[] { "GET", "POST" }, (Delegate)Sink);
        app.MapMethods("/flowrules/{*rest}", new[] { "GET", "POST" }, (Delegate)Sink);
        app.MapMethods("/v2/products/{product}/{config}", new[] { "GET", "HEAD" }, (Delegate)Version);
        app.MapGet("/WoW", (Delegate)Nydus);
        app.MapGet("/WoW/{*rest}", (Delegate)Nydus);
        app.MapMethods("/oauth/{*rest}", new[] { "GET", "POST" }, (Delegate)Sink);
    }

    IResult Alert(HttpContext ctx, string product, string env, string region, string locale)
    {
        Log.outDebug(LogFilter.Http, $"Support alert {product}/{env}/{region}/{locale}");
        return Results.Text("Battle.not is online.", "text/plain;charset=utf-8", Encoding.UTF8);
    }

    async Task<IResult> Sso(HttpContext ctx)
    {
        string token = ctx.Request.Query["token"].ToString();
        string refer = ctx.Request.Query["ref"].ToString();
        Log.outDebug(LogFilter.Http, $"Support sso token {(token.Length > 0 ? "yes" : "no")} ref {refer}");
        if (token.Length == 0)
            return Page(ctx, "Sign in", "<p>Open Support from inside the game. Battle.not will sign this browser in with the token the client was given.</p>", null);
        SsoSession? who = await Lookup(token);
        if (who == null)
            return Page(ctx, "Sign in", "<p>That support sign-in has expired. Open Support from the game again.</p>", null);
        ctx.Response.Cookies.Append("bnet_sso", token, new CookieOptions { HttpOnly = true, Path = "/", SameSite = SameSiteMode.Lax });
        if (SameHost(ctx, refer))
            return Results.Redirect(refer);
        return Results.Redirect("/kb");
    }

    Task<IResult> Root(HttpContext ctx) => Help(ctx, "en");

    Task<IResult> HelpBare(HttpContext ctx) => Help(ctx, "en");

    Task<IResult> HelpFallback(HttpContext ctx, string locale, string? rest)
    {
        string safeLocale = string.IsNullOrWhiteSpace(locale) ? "en" : locale;
        Log.outDebug(LogFilter.Http, $"Support fallback {safeLocale}/{rest ?? ""}");
        return Help(ctx, safeLocale);
    }

    async Task<IResult> Help(HttpContext ctx, string locale)
    {
        SsoSession? who = await Who(ctx);
        Log.outDebug(LogFilter.Http, $"Support home {locale} {(who == null ? "anonymous" : who.BattleTag)}");
        string section = ctx.Request.Query["section"].ToString().ToLowerInvariant();
        if (section is not ("knowledge" or "security" or "player" or "stuck" or "bug" or "suggestion"))
            section = "security";
        (string title, string copy) = section switch
        {
            "knowledge" => ("Knowledge Base", "<p>Find help for account security, gameplay problems, and common questions.</p><p>Choose a topic on the left, or open a ticket and include the character and realm involved.</p>"),
            "player" => ("Report Player", "<p>Tell us about harassment, cheating, or another player concern. Include the character name, realm, and what happened.</p>"),
            "stuck" => ("Character Stuck!", "<p>If your character cannot move or is trapped, open a ticket and choose the inn or graveyard destination when available.</p>"),
            "bug" => ("Submit Bug", "<p>Describe what happened, what you expected, and the steps that reproduce the problem. Your character and location are attached to the ticket.</p>"),
            "suggestion" => ("Submit Suggestion", "<p>Share an idea for improving the game. Include enough detail for the team to understand the change you have in mind.</p>"),
            _ => ("Account Security", "<h2>Phishing Whispers</h2><p>Battle.not staff will never ask for your password or authenticator code. Be cautious with messages claiming to be from staff, and report suspicious messages through a ticket.</p><h2>Keep Your Login Information Private</h2><p>Sharing account details can put your characters and items at risk. Use a unique password and never send it to another player.</p><h2>Scan Your Computer Regularly</h2><p>Keep your computer and security software up to date. Avoid unknown downloads and add-ons that ask for your login information.</p>")
        };
        return LandingPage(ctx, section, title, copy, who);
    }

    static IResult LandingPage(HttpContext ctx, string selected, string title, string copy, SsoSession? who)
    {
        var nav = new (string Id, string Label, string Icon, string Href)[]
        {
            ("knowledge", "Knowledge Base", "?", "/kb?section=knowledge"),
            ("security", "Account Security", "◆", "/kb?section=security"),
            ("player", "Report Player", "!", "/kb?section=player"),
            ("stuck", "Character Stuck!", "⚙", "/kb?section=stuck"),
            ("bug", "Submit Bug", "✹", "/kb?section=bug"),
            ("suggestion", "Submit Suggestion", "●", "/kb?section=suggestion")
        };
        string links = string.Join("", nav.Select(item =>
            "<a class=\"nav-item" + (item.Id == selected ? " selected" : "") + "\" href=\"" + item.Href + "\"><span class=\"nav-icon\">" + item.Icon + "</span><span>" + item.Label + "</span></a>"));
        if (who is { Security: >= 2 })
            links += "<a class=\"nav-item\" href=\"/support/admin/shop\"><span class=\"nav-icon\">⚙</span><span>Manage Shop</span></a>";
        string identity = who == null
            ? "Open Support from inside the game to sign in as your character."
            : "Signed in as <strong>" + Esc(who.BattleTag.Length > 0 ? who.BattleTag : who.Email) + "</strong>" +
                (who.CharacterName.Length == 0 ? "" : " · " + Esc(who.CharacterName));

        const string style = """
            <style>
            *{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden}
            body{color:#f1e2b3;background:radial-gradient(ellipse at 50% 0,#36485a 0,#1b2732 58%,#10161c 100%);font:15px Georgia,"Times New Roman",serif}
            .shell{height:100vh;min-height:300px;margin:0}
            .layout{height:100%;display:grid;grid-template-columns:202px minmax(0,1fr)}
            .sidebar{display:flex;flex-direction:column;gap:10px;padding:20px 10px 13px;border-right:4px ridge #625a4c;background:linear-gradient(90deg,#63401e,#9a662b 8%,#70471f 50%,#4c301b);box-shadow:inset 0 0 16px #170d07;overflow:auto}
            .nav-item{min-height:51px;display:flex;align-items:center;gap:10px;padding:7px 9px;color:#f6df9c;text-decoration:none;border:2px solid #482a14;border-radius:4px;background:linear-gradient(100deg,#5e2710,#9c3f18 42%,#61240f);box-shadow:inset 0 1px 2px #e4a34b66,0 2px 3px #140b05;text-shadow:0 1px 2px #1b0c04;font-size:14px;line-height:1.1}
            .nav-item:hover,.nav-item.selected{border-color:#e2b743;background:linear-gradient(#925116,#d29120 45%,#7a3b12);color:#fff0b4;box-shadow:inset 0 0 9px #ffdc5f88,0 0 4px #ffe17a}
            .nav-icon{display:grid;place-items:center;flex:0 0 32px;height:32px;border-radius:50%;color:#79d7ff;background:radial-gradient(circle at 35% 25%,#f3fbff,#3699d1 32%,#123757 75%);border:1px solid #8db4c2;text-shadow:0 1px 3px #06233d;font:bold 21px Arial;box-shadow:0 1px 3px #120b06}
            .nav-ticket{margin-top:auto;min-height:48px;justify-content:center;font-size:15px}
            .content{position:relative;overflow:auto;padding:42px clamp(22px,3vw,42px) 82px;background:radial-gradient(ellipse at 48% 10%,#292825,#171716 60%,#111110);box-shadow:inset 0 0 22px #000}
            .eyebrow{color:#9b8c69;font:11px Arial,sans-serif;letter-spacing:.14em;text-transform:uppercase}
            h1{margin:8px 0 22px;color:#f0c747;font-size:23px;font-weight:normal;text-shadow:0 2px 3px #000}
            h2{margin:17px 0 3px;color:#edc64e;font-size:15px;font-weight:normal}
            p{max-width:790px;margin:0 0 4px;color:#e4e0d5;font:14px/1.22 Arial,sans-serif;text-shadow:0 1px 2px #000}
            .copy{max-width:800px}.account{position:absolute;right:24px;top:13px;color:#a79b7d;font:12px Arial,sans-serif;text-align:right}
            .footer{position:absolute;left:0;right:0;bottom:22px;text-align:center}
            .open-ticket{display:inline-flex;align-items:center;gap:9px;padding:10px 18px;color:#ffe7a2;text-decoration:none;background:linear-gradient(#a0521c,#70290d);border:2px solid #4b2915;box-shadow:inset 0 1px 2px #f1b25488,0 2px 4px #000;font-size:15px}
            .open-ticket:hover{border-color:#e2b743;background:linear-gradient(#bb7824,#78320e)}.ticket-icon{color:#7fdafa;font-size:20px}
            @media(max-width:620px){.layout{grid-template-columns:145px minmax(0,1fr)}.sidebar{padding:18px 6px 8px;gap:7px}.nav-item{padding:5px;font-size:12px;gap:5px}.nav-icon{flex-basis:27px;height:27px}.content{padding:43px 14px 80px}.account{position:static;text-align:left;margin-bottom:12px}h1{font-size:20px}}
            </style>
            """;
        string html = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + Esc(title) + "</title>" + style +
            "</head><body><div class=\"layout\"><nav class=\"sidebar\" aria-label=\"Support topics\">" + links +
            "<a class=\"nav-item nav-ticket\" href=\"/help/ticket\"><span class=\"nav-icon\">✉</span><span>Open a Ticket</span></a></nav>" +
            "<main class=\"content\"><div class=\"account\">" + identity + "</div><div class=\"eyebrow\">Battle.not · Knowledge Base</div><h1>" + Esc(title) + "</h1><div class=\"copy\">" + copy +
            "</div><div class=\"footer\"><a class=\"open-ticket\" href=\"/help/ticket\"><span class=\"ticket-icon\">✉</span>Open a Ticket</a></div></main></div></body></html>";
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8);
    }

    async Task<IResult> Account(HttpContext ctx)
    {
        SsoSession? who = await Who(ctx);
        string path = ctx.Request.Path.Value ?? "/";
        Log.outDebug(LogFilter.Http, $"Support account {path}");
        string title = path.Trim('/');
        if (title.Length == 0) title = "account";
        var body = new StringBuilder();
        if (who == null)
            body.Append("<p>Open Support from inside the game to view this account page.</p>");
        else
        {
            body.Append("<p>");
            body.Append(Esc(who.BattleTag.Length > 0 ? who.BattleTag : who.Email));
            body.Append("</p><p>Game account ");
            body.Append(who.AccountId.ToString());
            body.Append(" · realm ");
            body.Append(who.RealmId.ToString());
            body.Append("</p>");
        }
        return Page(ctx, title, body.ToString(), who);
    }

    async Task<IResult> Submit(HttpContext ctx, string locale)
    {
        SsoSession? who = await Who(ctx);
        Log.outDebug(LogFilter.Http, $"Support submit {locale} {ctx.Request.Method}");
        if (who == null)
            return Page(ctx, "Submit a ticket", "<p>Open Support from inside the game, then submit the ticket.</p>", null);
        string note = "";
        if (HttpMethods.IsPost(ctx.Request.Method))
        {
            Dictionary<string, string> form = await ReadForm(ctx);
            string category = Field(form, "category");
            string subject = Clip(Field(form, "subject"), 120);
            string message = Clip(Field(form, "message"), 4000);
            string destination = Field(form, "destination");
            if (category != "bug" && category != "stuck" && category != "account" && category != "player" && category != "other")
                category = "other";
            if (subject.Length == 0 || message.Length == 0)
                note = "<p>A subject and a message are both required.</p>";
            else
            {
                await Ensure();
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await using MySqlConnection conn = _db.Connect();
                await conn.OpenAsync();
                await using var cmd = new MySqlCommand(
                    "INSERT INTO support_tickets (battlenetAccountId, accountId, realmId, characterGuid, characterName, category, subject, message, mapId, zoneId, posX, posY, posZ, status, created, updated) " +
                    "VALUES (@bnet, @account, @realm, @guid, @name, @category, @subject, @message, @map, @zone, @x, @y, @z, 'open', @now, @now)", conn);
                cmd.Parameters.AddWithValue("@bnet", who.BnetId);
                cmd.Parameters.AddWithValue("@account", who.AccountId);
                cmd.Parameters.AddWithValue("@realm", who.RealmId);
                cmd.Parameters.AddWithValue("@guid", who.CharacterGuid);
                cmd.Parameters.AddWithValue("@name", Clip(who.CharacterName, 12));
                cmd.Parameters.AddWithValue("@category", category);
                cmd.Parameters.AddWithValue("@subject", subject);
                cmd.Parameters.AddWithValue("@message", message);
                cmd.Parameters.AddWithValue("@map", who.MapId);
                cmd.Parameters.AddWithValue("@zone", who.ZoneId);
                cmd.Parameters.AddWithValue("@x", who.X);
                cmd.Parameters.AddWithValue("@y", who.Y);
                cmd.Parameters.AddWithValue("@z", who.Z);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync();
                if (category == "stuck" && (destination == "inn" || destination == "graveyard"))
                {
                    await using var log = new MySqlCommand(
                        "INSERT INTO support_unstuck_log (accountId, realmId, characterGuid, destination, result, time) VALUES (@account, @realm, @guid, @dest, @result, @now)", conn);
                    log.Parameters.AddWithValue("@account", who.AccountId);
                    log.Parameters.AddWithValue("@realm", who.RealmId);
                    log.Parameters.AddWithValue("@guid", who.CharacterGuid);
                    log.Parameters.AddWithValue("@dest", destination);
                    log.Parameters.AddWithValue("@result", "recorded");
                    log.Parameters.AddWithValue("@now", now);
                    await log.ExecuteNonQueryAsync();
                }
                return Results.Redirect("/support/" + Uri.EscapeDataString(locale) + "/ticket/status");
            }
        }
        string page = note +
            "<form method=\"post\">" +
            "<label>Category <select name=\"category\">" +
            "<option value=\"bug\">Bug</option><option value=\"stuck\">Stuck</option><option value=\"account\">Account</option>" +
            "<option value=\"player\">Player</option><option value=\"other\">Other</option></select></label>" +
            "<label>If you are stuck <select name=\"destination\"><option value=\"\">No move</option><option value=\"inn\">Inn</option><option value=\"graveyard\">Graveyard</option></select></label>" +
            "<label>Subject <input name=\"subject\" maxlength=\"120\" required></label>" +
            "<label>Message <textarea name=\"message\" rows=\"6\" required></textarea></label>" +
            "<button type=\"submit\">Send</button></form>";
        return Page(ctx, "Submit a ticket", page, who);
    }

    Task<IResult> SubmitDefault(HttpContext ctx) => Submit(ctx, "en");

    async Task<IResult> Status(HttpContext ctx, string locale)
    {
        SsoSession? who = await Who(ctx);
        Log.outDebug(LogFilter.Http, $"Support status {locale} {ctx.Request.Method}");
        if (who == null)
            return Page(ctx, "Ticket status", "<p>Open Support from inside the game to see your tickets.</p>", null);
        await Ensure();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        if (HttpMethods.IsPost(ctx.Request.Method))
        {
            Dictionary<string, string> form = await ReadForm(ctx);
            string message = Clip(Field(form, "message"), 4000);
            if (uint.TryParse(Field(form, "ticket"), out uint ticketId) && message.Length > 0 && await Owns(conn, who.BnetId, ticketId))
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                await using var reply = new MySqlCommand(
                    "INSERT INTO support_ticket_replies (ticketId, accountId, isStaff, message, created) VALUES (@ticket, @account, 0, @message, @now)", conn);
                reply.Parameters.AddWithValue("@ticket", ticketId);
                reply.Parameters.AddWithValue("@account", who.AccountId);
                reply.Parameters.AddWithValue("@message", message);
                reply.Parameters.AddWithValue("@now", now);
                await reply.ExecuteNonQueryAsync();
                await using var touch = new MySqlCommand("UPDATE support_tickets SET updated=@now, status='open' WHERE id=@ticket AND battlenetAccountId=@bnet", conn);
                touch.Parameters.AddWithValue("@now", now);
                touch.Parameters.AddWithValue("@ticket", ticketId);
                touch.Parameters.AddWithValue("@bnet", who.BnetId);
                await touch.ExecuteNonQueryAsync();
            }
        }
        var body = new StringBuilder();
        await using (var list = new MySqlCommand(
            "SELECT id, category, subject, message, status, created FROM support_tickets WHERE battlenetAccountId=@bnet ORDER BY id DESC LIMIT 30", conn))
        {
            list.Parameters.AddWithValue("@bnet", who.BnetId);
            await using MySqlDataReader reader = await list.ExecuteReaderAsync();
            bool any = false;
            while (await reader.ReadAsync())
            {
                any = true;
                uint id = Convert.ToUInt32(reader.GetValue(0));
                body.Append("<article><h2>#");
                body.Append(id.ToString());
                body.Append(" · ");
                body.Append(Esc(reader.GetString(1)));
                body.Append(" · ");
                body.Append(Esc(reader.GetString(4)));
                body.Append("</h2><h3>");
                body.Append(Esc(reader.GetString(2)));
                body.Append("</h3><p>");
                body.Append(Esc(reader.GetString(3)));
                body.Append("</p>");
                body.Append(await Replies(who.BnetId, id));
                body.Append("<form method=\"post\"><input type=\"hidden\" name=\"ticket\" value=\"");
                body.Append(id.ToString());
                body.Append("\"><label>Reply <textarea name=\"message\" rows=\"3\" required></textarea></label><button type=\"submit\">Reply</button></form></article>");
            }
            if (!any)
                body.Append("<p>No tickets yet.</p>");
        }
        return Page(ctx, "Ticket status", body.ToString(), who);
    }

    Task<IResult> StatusDefault(HttpContext ctx) => Status(ctx, "en");

    async Task<string> Replies(uint bnetId, uint ticketId)
    {
        var text = new StringBuilder();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT r.isStaff, r.message FROM support_ticket_replies r INNER JOIN support_tickets t ON t.id=r.ticketId WHERE r.ticketId=@ticket AND t.battlenetAccountId=@bnet ORDER BY r.id", conn);
        cmd.Parameters.AddWithValue("@ticket", ticketId);
        cmd.Parameters.AddWithValue("@bnet", bnetId);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bool staff = Convert.ToInt32(reader.GetValue(0)) != 0;
            text.Append("<p><strong>");
            text.Append(staff ? "Battle.not" : "You");
            text.Append(":</strong> ");
            text.Append(Esc(reader.GetString(1)));
            text.Append("</p>");
        }
        return text.ToString();
    }

    static async Task<bool> Owns(MySqlConnection conn, uint bnetId, uint ticketId)
    {
        await using var cmd = new MySqlCommand("SELECT 1 FROM support_tickets WHERE id=@id AND battlenetAccountId=@bnet", conn);
        cmd.Parameters.AddWithValue("@id", ticketId);
        cmd.Parameters.AddWithValue("@bnet", bnetId);
        object? found = await cmd.ExecuteScalarAsync();
        return found != null && found != DBNull.Value;
    }

    IResult Sink(HttpContext ctx)
    {
        Log.outDebug(LogFilter.Http, $"Support sink {ctx.Request.Method} {ctx.Request.Path}");
        return Results.Text("{}", "application/json;charset=utf-8", Encoding.UTF8);
    }

    IResult Version(HttpContext ctx, string product, string config)
    {
        Log.outDebug(LogFilter.Http, $"Support version {product}/{config}");
        return Results.Text("{\"disabled\":true}", "application/json;charset=utf-8", Encoding.UTF8);
    }

    IResult Nydus(HttpContext ctx)
    {
        Log.outDebug(LogFilter.Http, $"Support nydus {ctx.Request.Path}");
        return Page(ctx, "Battle.not", "<p>This notice is served by Battle.not.</p>", null);
    }

    async Task<SsoSession?> Who(HttpContext ctx)
    {
        if (!ctx.Request.Cookies.TryGetValue("bnet_sso", out string? token) || string.IsNullOrEmpty(token))
            return null;
        return await Lookup(token);
    }

    async Task<SsoSession?> Lookup(string token)
    {
        if (token.Length == 0 || token.Length > 64) return null;
        await Ensure();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT t.battlenetAccountId, t.accountId, t.realmId, t.characterGuid, IFNULL(t.characterName,''), IFNULL(t.mapId,0), IFNULL(t.zoneId,0), IFNULL(t.posX,0), IFNULL(t.posY,0), IFNULL(t.posZ,0), IFNULL(a.battle_tag,''), IFNULL(a.email,''), " +
            "COALESCE((SELECT MAX(aa.SecurityLevel) FROM account_access aa JOIN account game ON game.id=aa.AccountID WHERE aa.AccountID=t.accountId AND game.battlenet_account=t.battlenetAccountId AND aa.RealmID IN(-1,t.realmId)),0) " +
            "FROM bnot_sso_tokens t LEFT JOIN bnot_accounts a ON a.id=t.battlenetAccountId WHERE t.token=@token AND t.expires>@now", conn);
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@now", now);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new SsoSession
        {
            BnetId = Convert.ToUInt32(reader.GetValue(0)),
            AccountId = Convert.ToUInt32(reader.GetValue(1)),
            RealmId = Convert.ToUInt32(reader.GetValue(2)),
            CharacterGuid = Convert.ToUInt64(reader.GetValue(3)),
            CharacterName = reader.GetString(4),
            MapId = Convert.ToInt32(reader.GetValue(5)),
            ZoneId = Convert.ToInt32(reader.GetValue(6)),
            X = Convert.ToSingle(reader.GetValue(7)),
            Y = Convert.ToSingle(reader.GetValue(8)),
            Z = Convert.ToSingle(reader.GetValue(9)),
            BattleTag = reader.GetString(10),
            Email = reader.GetString(11),
            Security = Convert.ToByte(reader.GetValue(12))
        };
    }

    int _ready;

    async Task Ensure()
    {
        if (Volatile.Read(ref _ready) == 1) return;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await SsoTables(conn);
        foreach (string sql in new[]
        {
            "CREATE TABLE IF NOT EXISTS support_tickets (" +
            "id int unsigned NOT NULL AUTO_INCREMENT," +
            "battlenetAccountId int unsigned NOT NULL," +
            "accountId int unsigned NOT NULL," +
            "realmId int unsigned NOT NULL," +
            "characterGuid bigint unsigned NOT NULL DEFAULT 0," +
            "characterName varchar(12) NOT NULL DEFAULT ''," +
            "category enum('bug','stuck','account','player','other') NOT NULL," +
            "subject varchar(120) NOT NULL," +
            "message text NOT NULL," +
            "mapId int unsigned NOT NULL DEFAULT 0," +
            "zoneId int unsigned NOT NULL DEFAULT 0," +
            "posX float NOT NULL DEFAULT 0," +
            "posY float NOT NULL DEFAULT 0," +
            "posZ float NOT NULL DEFAULT 0," +
            "status enum('open','answered','closed') NOT NULL DEFAULT 'open'," +
            "created bigint NOT NULL," +
            "updated bigint NOT NULL," +
            "PRIMARY KEY (id), KEY idx_account (battlenetAccountId), KEY idx_status (status)" +
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
            "CREATE TABLE IF NOT EXISTS support_ticket_replies (" +
            "id int unsigned NOT NULL AUTO_INCREMENT," +
            "ticketId int unsigned NOT NULL," +
            "accountId int unsigned NOT NULL," +
            "isStaff tinyint unsigned NOT NULL DEFAULT 0," +
            "message text NOT NULL," +
            "created bigint NOT NULL," +
            "PRIMARY KEY (id), KEY idx_ticket (ticketId)" +
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
            "CREATE TABLE IF NOT EXISTS support_unstuck_log (" +
            "id int unsigned NOT NULL AUTO_INCREMENT," +
            "accountId int unsigned NOT NULL," +
            "realmId int unsigned NOT NULL," +
            "characterGuid bigint unsigned NOT NULL," +
            "destination enum('inn','graveyard') NOT NULL," +
            "result varchar(255) NOT NULL DEFAULT ''," +
            "time bigint NOT NULL," +
            "PRIMARY KEY (id), KEY idx_character (realmId, characterGuid, time)" +
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"
        })
        {
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        Volatile.Write(ref _ready, 1);
    }

    static async Task SsoTables(MySqlConnection conn)
    {
        await using (var create = new MySqlCommand(
            "CREATE TABLE IF NOT EXISTS bnot_sso_tokens (" +
            "token varchar(64) NOT NULL," +
            "battlenetAccountId int unsigned NOT NULL," +
            "accountId int unsigned NOT NULL," +
            "realmId int unsigned NOT NULL," +
            "characterGuid bigint unsigned NOT NULL DEFAULT 0," +
            "issued bigint NOT NULL," +
            "expires bigint NOT NULL," +
            "ip varchar(45) NOT NULL DEFAULT ''," +
            "PRIMARY KEY (token), KEY idx_expires (expires)" +
            ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci", conn))
            await create.ExecuteNonQueryAsync();
        await AddColumn(conn, "characterName", "varchar(12) NOT NULL DEFAULT ''");
        await AddColumn(conn, "mapId", "int unsigned NOT NULL DEFAULT 0");
        await AddColumn(conn, "zoneId", "int unsigned NOT NULL DEFAULT 0");
        await AddColumn(conn, "posX", "float NOT NULL DEFAULT 0");
        await AddColumn(conn, "posY", "float NOT NULL DEFAULT 0");
        await AddColumn(conn, "posZ", "float NOT NULL DEFAULT 0");
    }

    static async Task AddColumn(MySqlConnection conn, string column, string definition)
    {
        await using var check = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bnot_sso_tokens' AND COLUMN_NAME = @column", conn);
        check.Parameters.AddWithValue("@column", column);
        if (Convert.ToInt64(await check.ExecuteScalarAsync() ?? 0) > 0) return;
        await using var alter = new MySqlCommand("ALTER TABLE bnot_sso_tokens ADD COLUMN " + column + " " + definition, conn);
        await alter.ExecuteNonQueryAsync();
    }

    static IResult Page(HttpContext ctx, string title, string body, SsoSession? who)
    {
        string whoText = who == null ? "" : Esc(who.BattleTag.Length > 0 ? who.BattleTag : who.Email);
        string html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + Esc(title) + "</title>" +
            "<style>body{background:#1a140c;color:#f3e6c8;font-family:Georgia,serif;margin:0}header,main{max-width:40rem;margin:0 auto;padding:1rem 1.2rem}" +
            "a{color:#e0b15a}header{border-bottom:1px solid #3a2e1c}h1{font-size:1.4rem}label{display:block;margin:.6rem 0}input,select,textarea{width:100%;box-sizing:border-box;background:#2a2116;color:#f3e6c8;border:1px solid #5a4630;padding:.4rem}" +
            "button{background:#8a6230;color:#fff8ea;border:0;padding:.5rem 1rem;margin-top:.6rem}article{border-top:1px solid #3a2e1c;padding-top:.6rem}</style></head><body>" +
            "<header><h1>Battle.not</h1><nav><a href=\"/kb\">Support</a> · <a href=\"/help/ticket\">New ticket</a> · <a href=\"/help/tickets\">Status</a>" +
            (whoText.Length == 0 ? "" : " · " + whoText) + "</nav></header><main><h1>" + Esc(title) + "</h1>" + body + "</main></body></html>";
        return Results.Text(html, "text/html;charset=utf-8", Encoding.UTF8);
    }

    static bool SameHost(HttpContext ctx, string refer)
    {
        if (refer.Length == 0 || !Uri.TryCreate(refer, UriKind.Absolute, out Uri? uri)) return false;
        string host = ctx.Request.Host.Host;
        return uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase);
    }

    static async Task<Dictionary<string, string>> ReadForm(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
        string body = await reader.ReadToEndAsync();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (body.Length > 20000) body = body[..20000];
        foreach (string part in body.Split('&'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = Uri.UnescapeDataString(part[..eq].Replace('+', ' '));
            string value = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            map[key] = value;
        }
        return map;
    }

    static string Field(Dictionary<string, string> form, string key) => form.TryGetValue(key, out string? value) ? value.Trim() : "";

    static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    static string Esc(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    sealed class SsoSession
    {
        public byte Security;
        public uint BnetId;
        public uint AccountId;
        public uint RealmId;
        public ulong CharacterGuid;
        public string CharacterName = "";
        public string BattleTag = "";
        public string Email = "";
        public int MapId;
        public int ZoneId;
        public float X;
        public float Y;
        public float Z;
    }
}
