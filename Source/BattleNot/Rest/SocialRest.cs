using System.Text;
using System.Text.Json;

namespace BattleNot;

sealed class SocialRest
{
    readonly BnotDb _db;

    public SocialRest(BnotDb db) => _db = db;

    public void Map(WebApplication app)
    {
        app.MapGet("/bnetserver/profile/", (Delegate)Profile);
        app.MapPost("/bnetserver/profile/", (Delegate)SaveProfile);
        app.MapGet("/bnetserver/friends/", (Delegate)Friends);
        app.MapPost("/bnetserver/presence/", (Delegate)Presence);
        app.MapPost("/bnetserver/friends/add/", (Delegate)Add);
        app.MapPost("/bnetserver/friends/accept/", (Delegate)Accept);
        app.MapPost("/bnetserver/friends/reject/", (Delegate)Reject);
        app.MapPost("/bnetserver/friends/remove/", (Delegate)Remove);
        app.MapPost("/bnetserver/friends/block/", (Delegate)Block);
        app.MapPost("/bnetserver/friends/unblock/", (Delegate)Unblock);
        app.MapPost("/bnetserver/friends/favorite/", (Delegate)Favorite);
        app.MapGet("/bnetserver/messages/", (Delegate)Messages);
        app.MapPost("/bnetserver/messages/", (Delegate)Send);
        app.MapGet("/bnetserver/notifications/", (Delegate)Notices);
        app.MapPost("/bnetserver/notifications/read/", (Delegate)ReadNotices);
        app.MapGet("/bnetserver/shop/", (Delegate)Shop);
        app.MapPost("/bnetserver/gifts/", (Delegate)Gift);
        app.MapGet("/bnetserver/groups/", (Delegate)Groups);
        app.MapPost("/bnetserver/groups/", (Delegate)CreateGroup);
        app.MapPost("/bnetserver/groups/invite/", (Delegate)Invite);
        app.MapGet("/bnetserver/groups/messages/", (Delegate)GroupMessages);
        app.MapPost("/bnetserver/groups/messages/", (Delegate)SendGroup);
    }

    async Task<IResult> Profile(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        string wanted = ctx.Request.Query["tag"].ToString();
        if (wanted.Length == 0)
        {
            var own = await _db.Profiles.OwnSocial(who.Value.Id, who.Value.Email);
            if (own == null)
                return Results.StatusCode(401);
            var chars = await _db.Profiles.CharactersOf(who.Value.Id);
            return Json(ProfileJson(own.Value.Tag, own.Value.Status, own.Value.Bio, own.Value.Visibility, chars));
        }
        var other = await _db.Profiles.SocialByTag(wanted);
        if (other == null)
            return Json("{\"battletag\":\"\",\"status\":\"offline\",\"bio\":\"\",\"visibility\":2,\"characters\":[]}");
        bool show = other.Value.Id == who.Value.Id
            || other.Value.Visibility == 0
            || (other.Value.Visibility == 1 && await _db.Friends.AreFriends(who.Value.Id, other.Value.Id));
        var visible = show ? await _db.Profiles.CharactersOf(other.Value.Id) : new List<(string Name, int Level, string Guild, int Achievements)>();
        string bio = show ? other.Value.Bio : "";
        return Json(ProfileJson(other.Value.Tag, other.Value.Status, bio, other.Value.Visibility, visible));
    }

    async Task<IResult> SaveProfile(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        await _db.Tags.EnsureTag(who.Value.Id, who.Value.Email);
        JsonDocument? doc = await Read(ctx);
        int visibility = 0;
        int.TryParse(Text(doc, "visibility"), out visibility);
        await _db.Profiles.SaveProfile(who.Value.Id, Text(doc, "bio"), visibility);
        return Json("{\"ok\":true}");
    }

    static string ProfileJson(string tag, string status, string bio, int visibility, List<(string Name, int Level, string Guild, int Achievements)> chars)
    {
        var parts = new List<string>();
        foreach (var row in chars)
        {
            parts.Add("{\"name\":" + JsonText.Quote(row.Name) +
                ",\"level\":" + row.Level +
                ",\"guild\":" + JsonText.Quote(row.Guild) +
                ",\"achievements\":" + row.Achievements + "}");
        }
        return "{\"battletag\":" + JsonText.Quote(tag) +
            ",\"status\":" + JsonText.Quote(status) +
            ",\"bio\":" + JsonText.Quote(bio) +
            ",\"visibility\":" + visibility +
            ",\"characters\":[" + string.Join(",", parts) + "]}";
    }

    async Task<IResult> Friends(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
        {
            Log.outDebug(LogFilter.Http, "Social friends request unauthorized");
            return Results.StatusCode(401);
        }
        var parts = new List<string>();
        var friends = await _db.Friends.FriendList(who.Value.Id);
        foreach (var friend in friends)
        {
            parts.Add("{\"tag\":" + JsonText.Quote(friend.Tag) +
                ",\"status\":" + JsonText.Quote(friend.Status) +
                ",\"game\":" + JsonText.Quote(friend.Game) +
                ",\"character\":" + JsonText.Quote(friend.Character) +
                ",\"realm\":" + JsonText.Quote(friend.Realm) +
                ",\"favorite\":" + (friend.Favorite ? "true" : "false") +
                ",\"incoming\":" + (friend.State == 1 ? "true" : "false") +
                ",\"outgoing\":" + (friend.State == 0 ? "true" : "false") +
                ",\"blocked\":" + (friend.State == 3 ? "true" : "false") + "}");
        }
        Log.outDebug(LogFilter.Http, $"Social friends account {who.Value.Id}: {friends.Count} entr(y/ies)");
        return Json("{\"friends\":[" + string.Join(",", parts) + "]}");
    }

    async Task<IResult> Presence(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        JsonDocument? doc = await Read(ctx);
        string status = Text(doc, "status");
        string game = Text(doc, "game");
        string character = Text(doc, "character");
        string realm = Text(doc, "realm");
        string tag = await _db.Profiles.SetPresence(who.Value.Id, who.Value.Email, status, game, character, realm);
        return Json("{\"ok\":true,\"battletag\":" + JsonText.Quote(tag) + "}");
    }

    async Task<IResult> Add(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        await _db.Tags.EnsureTag(who.Value.Id, who.Value.Email);
        string error = await _db.Friends.AddFriend(who.Value.Id, Text(await Read(ctx), "tag"));
        if (error == "ok")
            Log.outDebug(LogFilter.Http, "Friend request from account " + who.Value.Id);
        return Json("{\"ok\":" + (error == "ok" ? "true" : "false") + ",\"error\":" + JsonText.Quote(error) + "}");
    }

    async Task<IResult> Accept(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        string error = await _db.Friends.AcceptFriend(who.Value.Id, Text(await Read(ctx), "tag"));
        return Json("{\"ok\":" + (error == "ok" ? "true" : "false") + ",\"error\":" + JsonText.Quote(error) + "}");
    }

    async Task<IResult> Remove(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        await _db.Friends.RemoveFriend(who.Value.Id, Text(await Read(ctx), "tag"));
        return Json("{\"ok\":true}");
    }

    async Task<IResult> Reject(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        string error = await _db.Friends.RejectFriend(who.Value.Id, Text(await Read(ctx), "tag"));
        return Done(error);
    }

    async Task<IResult> Block(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        string error = await _db.Friends.BlockFriend(who.Value.Id, Text(await Read(ctx), "tag"));
        return Done(error);
    }

    async Task<IResult> Unblock(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        await _db.Friends.UnblockFriend(who.Value.Id, Text(await Read(ctx), "tag"));
        return Json("{\"ok\":true}");
    }

    async Task<IResult> Favorite(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        JsonDocument? doc = await Read(ctx);
        await _db.Friends.FavoriteFriend(who.Value.Id, Text(doc, "tag"), Flag(doc, "favorite"));
        return Json("{\"ok\":true}");
    }

    async Task<IResult> Messages(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        return Json(MessageJson(await _db.Chat.Messages(who.Value.Id, ctx.Request.Query["tag"].ToString())));
    }

    async Task<IResult> Send(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        JsonDocument? doc = await Read(ctx);
        return Done(await _db.Chat.SendMessage(who.Value.Id, Text(doc, "tag"), Text(doc, "body")));
    }

    async Task<IResult> Notices(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        var parts = new List<string>();
        foreach (var note in await _db.Notices.Notices(who.Value.Id))
        {
            parts.Add("{\"id\":" + note.Id +
                ",\"kind\":" + JsonText.Quote(note.Kind) +
                ",\"text\":" + JsonText.Quote(note.Text) +
                ",\"unread\":" + (note.Unread ? "true" : "false") + "}");
        }
        return Json("{\"notices\":[" + string.Join(",", parts) + "]}");
    }

    async Task<IResult> ReadNotices(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        await _db.Notices.ReadNotices(who.Value.Id);
        return Json("{\"ok\":true}");
    }

    async Task<IResult> Shop(HttpContext ctx)
    {
        var parts = new List<string>();
        foreach (ShopItem item in await _db.Shop.LoadShopItems())
        {
            parts.Add("{\"id\":" + item.ProductId +
                ",\"section\":" + JsonText.Quote(item.SectionName) +
                ",\"name\":" + JsonText.Quote(item.Name) +
                ",\"price\":" + item.PriceCents +
                ",\"currency\":" + JsonText.Quote(item.Currency) + "}");
        }
        return Json("{\"products\":[" + string.Join(",", parts) + "]}");
    }

    async Task<IResult> Gift(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        JsonDocument? doc = await Read(ctx);
        uint.TryParse(Text(doc, "product"), out uint product);
        return Done(await _db.Gifts.SendGift(who.Value.Id, Text(doc, "tag"), product, Text(doc, "note")));
    }

    async Task<IResult> Groups(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        var parts = new List<string>();
        foreach (var group in await _db.Chat.Groups(who.Value.Id))
            parts.Add("{\"id\":" + group.Id + ",\"name\":" + JsonText.Quote(group.Name) + "}");
        return Json("{\"groups\":[" + string.Join(",", parts) + "]}");
    }

    async Task<IResult> CreateGroup(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        uint id = await _db.Chat.CreateGroup(who.Value.Id, Text(await Read(ctx), "name"));
        return Json("{\"ok\":" + (id > 0 ? "true" : "false") + ",\"id\":" + id + "}");
    }

    async Task<IResult> Invite(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        JsonDocument? doc = await Read(ctx);
        uint.TryParse(Text(doc, "group"), out uint group);
        return Done(await _db.Chat.InviteGroup(who.Value.Id, group, Text(doc, "tag")));
    }

    async Task<IResult> GroupMessages(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        uint.TryParse(ctx.Request.Query["group"].ToString(), out uint group);
        return Json(MessageJson(await _db.Chat.GroupMessages(who.Value.Id, group)));
    }

    async Task<IResult> SendGroup(HttpContext ctx)
    {
        (uint Id, string Email)? who = await Who(ctx);
        if (who == null)
            return Results.StatusCode(401);
        JsonDocument? doc = await Read(ctx);
        uint.TryParse(Text(doc, "group"), out uint group);
        return Done(await _db.Chat.SendGroup(who.Value.Id, group, Text(doc, "body")));
    }

    static string MessageJson(List<(ulong Id, string Tag, string Body, uint Sent, bool Mine)> rows)
    {
        var parts = new List<string>();
        foreach (var row in rows)
        {
            parts.Add("{\"id\":" + row.Id +
                ",\"tag\":" + JsonText.Quote(row.Tag) +
                ",\"body\":" + JsonText.Quote(row.Body) +
                ",\"sent\":" + row.Sent +
                ",\"mine\":" + (row.Mine ? "true" : "false") + "}");
        }
        return "{\"messages\":[" + string.Join(",", parts) + "]}";
    }

    static IResult Done(string error) =>
        Json("{\"ok\":" + (error == "ok" ? "true" : "false") + ",\"error\":" + JsonText.Quote(error) + "}");

    async Task<(uint Id, string Email)?> Who(HttpContext ctx)
    {
        string? ticket = Ticket(ctx);
        if (string.IsNullOrEmpty(ticket))
            return null;
        return await _db.Auth.SocialAccount(ticket);
    }

    static string? Ticket(HttpContext ctx)
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

    static async Task<JsonDocument?> Read(HttpContext ctx)
    {
        try
        {
            using var reader = new StreamReader(ctx.Request.Body);
            string body = await reader.ReadToEndAsync();
            if (body.Length == 0)
                return null;
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    static string Text(JsonDocument? doc, string name)
    {
        if (doc == null || !doc.RootElement.TryGetProperty(name, out JsonElement value))
            return "";
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetRawText();
        if (value.ValueKind == JsonValueKind.True)
            return "true";
        if (value.ValueKind == JsonValueKind.False)
            return "false";
        return value.GetString() ?? "";
    }

    static bool Flag(JsonDocument? doc, string name)
    {
        return Text(doc, name) == "true";
    }

    static IResult Json(string json) => Results.Text(json, "application/json;charset=utf-8");
}
