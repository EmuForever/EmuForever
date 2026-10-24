using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BattleNot;

sealed partial class SupportRest
{
    static readonly byte[] ShopCsrfKey = RandomNumberGenerator.GetBytes(32);

    void MapShopAdministration(WebApplication app)
    {
        app.MapMethods("/support/admin/shop", ["GET", "POST"], (Delegate)ShopAdministration);
    }

    static string ShopCsrf(HttpContext context) => Convert.ToHexString(HMACSHA256.HashData(ShopCsrfKey,
        Encoding.UTF8.GetBytes(context.Request.Cookies["bnet_sso"] ?? "")));

    static bool ValidShopCsrf(HttpContext context, string supplied)
    {
        if (supplied.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ShopCsrf(context)), Encoding.ASCII.GetBytes(supplied));
    }

    async Task<IResult> ShopAdministration(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        SsoSession? who = await Who(context);
        if (who == null)
        {
            context.Response.StatusCode = 401;
            return Page(context, "Shop administration", "<p>Open Support from inside the game to sign in.</p>", null);
        }
        if (who.Security < 2)
        {
            context.Response.StatusCode = 403;
            return Page(context, "Shop administration", "<p>Game Master access is required.</p>", who);
        }
        string error = "";
        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (!context.Request.HasFormContentType || context.Request.ContentLength > 20000)
                return Results.BadRequest("Expected a form of at most 20 KB.");
            var form = await ReadForm(context);
            if (!ValidShopCsrf(context, Field(form, "csrf"))) return Results.StatusCode(403);
            try
            {
                string operation = Field(form, "operation");
                uint id = Unsigned(form, "id");
                string message;
                switch (operation)
                {
                    case "save-product":
                    {
                        var product = new SupportShopProduct(id, Unsigned(form, "group"), Field(form, "name"), Field(form, "description"),
                            Integer(form, "icon"), Unsigned(form, "price"), Unsigned(form, "normal"), Integer(form, "order"),
                            Field(form, "enabled") == "1", SupportShopStore.ParseActions(Field(form, "actions")));
                        uint saved = await SupportShopStore.SaveProduct(_db.Shop, product, who.AccountId);
                        message = $"Product {saved} saved. Reopen the in-game shop to load the updated catalog.";
                        break;
                    }
                    case "save-group":
                    {
                        uint saved = await SupportShopStore.SaveGroup(_db.Shop,
                            new(id, Field(form, "name"), Integer(form, "icon"), Integer(form, "order")), who.AccountId);
                        message = $"Group {saved} saved.";
                        break;
                    }
                    case "delete-product":
                    case "delete-group":
                        await SupportShopStore.Delete(_db.Shop, id, operation == "delete-group", who.AccountId);
                        message = $"{(operation == "delete-group" ? "Group" : "Product")} {id} removed.";
                        break;
                    default:
                        throw new InvalidDataException("Unknown catalog operation.");
                }
                return Results.Redirect("/support/admin/shop?message=" + Uri.EscapeDataString(message));
            }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException or FormatException)
            {
                context.Response.StatusCode = 400;
                error = exception.Message;
            }
            catch (Exception exception)
            {
                Log.outError(LogFilter.Http, $"Support shop GM account {who.AccountId}: {exception.GetType().Name}: {exception.Message}");
                context.Response.StatusCode = 503;
                error = "The catalog could not be saved. Your changes were rolled back. Try again.";
            }
        }
        var catalog = await SupportShopStore.Read(_db.Shop);
        uint.TryParse(context.Request.Query["product"], out uint productId);
        uint.TryParse(context.Request.Query["group"], out uint groupId);
        SupportShopProduct? selected = catalog.Products.FirstOrDefault(p => p.Id == productId);
        SupportShopGroup? selectedGroup = catalog.Groups.Where(g => g.Id == groupId).Cast<SupportShopGroup?>().FirstOrDefault();
        string csrf = ShopCsrf(context);
        var html = new StringBuilder("<style>main,header{max-width:72rem}table{width:100%;border-collapse:collapse;margin:1rem 0}td,th{text-align:left;border-bottom:1px solid #5a4630;padding:.5rem}form.editor{padding:1rem;background:#211a11;border:1px solid #5a4630;margin:1rem 0}.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:1rem}.notice{padding:1rem;border-left:4px solid #79c86e}.error{border-color:#ed7777}input[type=checkbox]{width:auto}small{color:#d1bd97}.inline{display:inline}button.remove{background:#823d35}</style>");
        string notice = error.Length != 0 ? error : Clip(context.Request.Query["message"].ToString(), 300);
        if (notice.Length != 0) html.Append("<p class=\"notice" + (error.Length != 0 ? " error" : "") + "\">" + Esc(notice) + "</p>");
        html.Append("<p>Manage the catalog used by the in-game shop. Prices are shop points; gold delivery amounts are copper. Disabled products stay editable here.</p>");
        html.Append("<h2>Products</h2><a href=\"/support/admin/shop\">Add a product</a><table><thead><tr><th>ID</th><th>Product</th><th>Group</th><th>Points</th><th>Status</th><th></th></tr></thead><tbody>");
        foreach (var product in catalog.Products)
            html.Append($"<tr><td>{product.Id}</td><td>{Esc(product.Name)}</td><td>{Esc(catalog.Groups.FirstOrDefault(g => g.Id == product.Group).Name ?? "Missing group")}</td><td>{product.Price}</td><td>{(product.Enabled ? "Enabled" : "Disabled")}</td><td><a href=\"?product={product.Id}#product-editor\">Edit</a></td></tr>");
        html.Append("</tbody></table><h2 id=\"product-editor\">" + (selected == null ? "Add product" : "Edit product " + selected.Id) + "</h2>");
        html.Append(FormStart("save-product", selected?.Id ?? 0, csrf));
        html.Append(Input("name", "Name", selected?.Name ?? "", required: true));
        html.Append("<label>Description<textarea name=\"description\" rows=\"3\">" + Esc(selected?.Description ?? "") + "</textarea></label><div class=\"fields\"><label>Group<select name=\"group\" required>");
        foreach (var group in catalog.Groups)
            html.Append($"<option value=\"{group.Id}\"{(selected?.Group == group.Id ? " selected" : "")}>{Esc(group.Name)}</option>");
        html.Append("</select></label>");
        html.Append(Input("price", "Price in points", (selected?.Price ?? 0).ToString(), true, "number"));
        html.Append(Input("normal", "Normal price (0 hides discount)", (selected?.NormalPrice ?? 0).ToString(), true, "number"));
        html.Append(Input("icon", "Icon file ID", (selected?.Icon ?? 134414).ToString(), true, "number"));
        html.Append(Input("order", "Display order", (selected?.Order ?? 0).ToString(), true, "number"));
        html.Append("</div><label><input type=\"checkbox\" name=\"enabled\" value=\"1\"" + (selected?.Enabled != false ? " checked" : "") + "> Enabled</label>");
        string delivery = selected == null ? "1,6948,1" : string.Join('\n', selected.Actions.Select(a => $"{a.Kind},{a.Param1},{a.Param2}"));
        html.Append("<label>Delivery actions<textarea name=\"actions\" rows=\"5\" required>" + Esc(delivery) + "</textarea></label>");
        html.Append("<small>One action,parameter1,parameter2 per line. 1: item ID,count; 2: copper,0; 3: spell ID,0; 4: service,0 (1 rename, 2 appearance, 3 race, 4 faction); 5: skill ID,rank; 6: level,0; 7: title ID,0; 8: faction ID,standing; 10: boost type 6,0. Example: 2,1000000,0 delivers 100 gold.</small>");
        html.Append("<p><button type=\"submit\">Save product</button></p></form>");
        if (selected != null) html.Append(DeleteForm("delete-product", selected.Id, csrf, "Remove product"));
        html.Append("<h2>Groups</h2><table><thead><tr><th>ID</th><th>Name</th><th>Order</th><th></th></tr></thead><tbody>");
        foreach (var group in catalog.Groups)
            html.Append($"<tr><td>{group.Id}</td><td>{Esc(group.Name)}</td><td>{group.Order}</td><td><a href=\"?group={group.Id}#group-editor\">Edit</a></td></tr>");
        html.Append("</tbody></table><h2 id=\"group-editor\">" + (selectedGroup == null ? "Add group" : "Edit group " + selectedGroup.Value.Id) + "</h2>");
        html.Append(FormStart("save-group", selectedGroup?.Id ?? 0, csrf));
        html.Append(Input("name", "Group name", selectedGroup?.Name ?? "", true));
        html.Append(Input("icon", "Icon file ID", (selectedGroup?.Icon ?? 134414).ToString(), true, "number"));
        html.Append(Input("order", "Display order", (selectedGroup?.Order ?? 0).ToString(), true, "number"));
        html.Append("<button type=\"submit\">Save group</button></form>");
        if (selectedGroup != null) html.Append(DeleteForm("delete-group", selectedGroup.Value.Id, csrf, "Remove empty group"));
        return Page(context, "Shop administration", html.ToString(), who);
    }

    static string FormStart(string operation, uint id, string csrf) =>
        $"<form class=\"editor\" method=\"post\" action=\"/support/admin/shop\"><input type=\"hidden\" name=\"operation\" value=\"{operation}\"><input type=\"hidden\" name=\"id\" value=\"{id}\"><input type=\"hidden\" name=\"csrf\" value=\"{csrf}\">";
    static string Input(string name, string label, string value, bool required = false, string type = "text") =>
        $"<label>{Esc(label)}<input name=\"{name}\" type=\"{type}\" value=\"{Esc(value)}\"{(required ? " required" : "")}></label>";
    static string DeleteForm(string operation, uint id, string csrf, string label) =>
        FormStart(operation, id, csrf) + "<button class=\"remove\" type=\"submit\">" + Esc(label) + "</button></form>";
    static uint Unsigned(Dictionary<string, string> form, string key) =>
        uint.TryParse(Field(form, key), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value :
            throw new InvalidDataException($"{key} must be a nonnegative whole number.");
    static int Integer(Dictionary<string, string> form, string key) =>
        int.TryParse(Field(form, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value :
            throw new InvalidDataException($"{key} must be a whole number.");
}
