using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Text.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace BattleNot;

// Classic 1.60 shop2 (Wow.exe CatalogUtilities). Auth is POST /sso. The storefront and catalog
// are StorefrontService/v1/GetCurrentPages and ProductCatalogService/v1/GetProductsByStoreId.
// An empty placements list and an empty products list is a store with nothing for sale.
sealed class ShopRest
{
    public void Map(WebApplication app)
    {
        app.MapMethods("/sso", new[] { "GET", "POST" }, (Delegate)Sso);
        app.MapMethods("/StorefrontService/v1/GetCurrentPages", new[] { "GET", "POST" }, (Delegate)Pages);
        app.MapMethods("/ProductCatalogService/v1/GetProductsByStoreId", new[] { "GET", "POST" }, (Delegate)Products);
        app.MapMethods("/PurchaseService/v1/GetAccountDefaultCurrency", new[] { "GET", "POST" }, Currency);
        app.MapMethods("/VirtualCurrencyLedgerService/v1/GetBalance", new[] { "GET", "POST" }, Balance);
        app.MapMethods("/PurchaseService/v1/QuoteDynamicBundle", new[] { "GET", "POST" }, Unavailable);
        app.MapMethods("/PublicOrderService/v1/GetOrderStatusByExternalTransactionId", new[] { "GET", "POST" }, Unavailable);
        app.MapFallback((Delegate)Fallback);
    }

    static ShopDb? _db;

    public static void Use(ShopDb db) => _db = db;

    static async Task<IResult> Sso(HttpContext ctx) => Json(await Catalog(ctx.Request.Path.Value ?? "/sso", "") ?? "{}");

    static async Task<IResult> Pages(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        return Json(await Catalog(ctx.Request.Path.Value ?? "", await reader.ReadToEndAsync()) ?? "{}");
    }

    static async Task<IResult> Products(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        return Json(await Catalog(ctx.Request.Path.Value ?? "", await reader.ReadToEndAsync()) ?? "{}");
    }

    static async Task<IResult> Fallback(HttpContext ctx)
    {
        if (ctx.Connection.LocalPort != 8443)
            return Results.NotFound();
        string path = ctx.Request.Path.Value ?? "";
        Log.outDebug(LogFilter.Http, $"Shop unmatched {ctx.Request.Method} {path}{ctx.Request.QueryString}");
        if (path.Contains("GetCurrentPages", StringComparison.OrdinalIgnoreCase))
            return await Pages(ctx);
        if (path.Contains("GetProductsByStoreId", StringComparison.OrdinalIgnoreCase))
            return await Products(ctx);
        if (path.EndsWith("/sso", StringComparison.OrdinalIgnoreCase))
            return await Sso(ctx);
        if (path.Contains("GetAccountDefaultCurrency", StringComparison.OrdinalIgnoreCase))
            return Currency(ctx);
        if (path.Contains("GetBalance", StringComparison.OrdinalIgnoreCase))
            return Balance(ctx);
        if (path.Contains("QuoteDynamicBundle", StringComparison.OrdinalIgnoreCase)
            || path.Contains("GetOrderStatusByExternalTransactionId", StringComparison.OrdinalIgnoreCase))
            return Unavailable(ctx);
        return Results.NotFound();
    }

    static IResult Currency(HttpContext ctx)
    {
        Log.outDebug(LogFilter.Http, $"Shop {ctx.Request.Method} {ctx.Request.Path}");
        return Json("{\"error\":null,\"currencyAlphaCode\":\"USD\",\"currencyCode\":\"USD\"}");
    }

    static IResult Balance(HttpContext ctx)
    {
        Log.outDebug(LogFilter.Http, $"Shop {ctx.Request.Method} {ctx.Request.Path}");
        return Json("{\"error\":null,\"balance\":{\"balance\":0,\"softCapped\":false}}");
    }

    static IResult Unavailable(HttpContext ctx)
    {
        Log.outDebug(LogFilter.Http, $"Shop {ctx.Request.Method} {ctx.Request.Path}");
        return Json("{\"error\":{\"category\":\"NON_RETRYABLE_FAILURE\",\"code\":\"NOT_FOUND\",\"message\":\"unavailable\"}}");
    }

    static IResult Json(string json) => Results.Text(json, "application/json;charset=utf-8", Encoding.UTF8);

    public static async Task ListenCrl(IPAddress address, int port, string crlFile, CancellationToken cancel)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancel);
                _ = ServeCrl(client, crlFile);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task ServeCrl(TcpClient client, string crlFile)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var buffer = new byte[1024];
                await stream.ReadAsync(buffer);
                byte[] body = File.Exists(crlFile) ? await File.ReadAllBytesAsync(crlFile) : Array.Empty<byte>();
                int status = body.Length == 0 ? 404 : 200;
                string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: application/pkix-crl\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                if (body.Length > 0)
                    await stream.WriteAsync(body);
            }
            catch (Exception ex)
            {
                Log.outError(LogFilter.Http, $"CRL {ex.Message}");
            }
        }
    }

    public static async Task Listen(IPAddress address, int port, X509Certificate2 certificate, bool plain, CancellationToken cancel)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancel);
                _ = Accept(client, certificate, plain);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task Accept(TcpClient client, X509Certificate2 certificate, bool plain)
    {
        string remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        using (client)
        {
            try
            {
                Stream stream = client.GetStream();
                if (!plain)
                {
                    var ssl = new SslStream(stream, false);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls12
                    });
                    Log.outDebug(LogFilter.Http, $"Shop tls {remote} {ssl.SslProtocol}");
                    stream = ssl;
                }
                else
                {
                    Log.outDebug(LogFilter.Http, $"Shop tcp {remote} plain");
                }
                await Serve(stream, remote);
            }
            catch (Exception ex)
            {
                Log.outError(LogFilter.Http, $"Shop tls {remote} {Describe(ex)}");
            }
        }
    }

    static async Task Serve(Stream stream, string remote)
    {
        var buffer = new byte[16384];
        var pending = new StringBuilder();
        while (true)
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                Log.outDebug(LogFilter.Http, $"Shop {remote} closed");
                return;
            }
            pending.Append(Encoding.ASCII.GetString(buffer, 0, read));
            string request = pending.ToString();
            int headerEnd = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
                continue;
            int bodyStart = headerEnd + 4;
            int length = ContentLength(request[..headerEnd]);
            if (request.Length < bodyStart + length)
                continue;
            int lineEnd = request.IndexOf("\r\n", StringComparison.Ordinal);
            string start = lineEnd >= 0 ? request[..lineEnd] : request;
            string[] parts = start.Split(' ');
            string target = parts.Length >= 2 ? parts[1] : "/";
            int query = target.IndexOf('?');
            string path = query >= 0 ? target[..query] : target;
            string reqBody = length > 0 ? request.Substring(bodyStart, length) : "";
            Log.outDebug(LogFilter.Http, $"Shop {parts[0]} {target}");
            string? body = await Catalog(path, reqBody);
            int status = body == null ? 404 : 200;
            body ??= "{\"error\":\"not found\"}";
            byte[] payload = Encoding.UTF8.GetBytes(body);
            string type = body.Length > 0 && body[0] == '<' ? "text/html;charset=utf-8" : "application/json;charset=utf-8";
            string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {payload.Length}\r\nConnection: keep-alive\r\n\r\n";
            Log.outDebug(LogFilter.Http, $"Shop {remote} response {status} {path} {payload.Length} bytes");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(payload);
            await stream.FlushAsync();
            pending.Clear();
            if (request.Length > bodyStart + length)
                pending.Append(request[(bodyStart + length)..]);
        }
    }

    static int ContentLength(string headers)
    {
        foreach (string line in headers.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan("Content-Length:".Length).Trim(), out int length))
                return length;
        }
        return 0;
    }

    static string Describe(Exception ex)
    {
        var text = new StringBuilder();
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (text.Length > 0)
                text.Append(" <- ");
            text.Append(cur.GetType().Name);
            text.Append(": ");
            text.Append(cur.Message);
        }
        return text.ToString();
    }

    public static async Task WritePlain(ConnectionContext context)
    {
        var text = new StringBuilder();
        while (true)
        {
            ReadResult read = await context.Transport.Input.ReadAsync(context.ConnectionClosed);
            foreach (ReadOnlyMemory<byte> segment in read.Buffer)
                text.Append(Encoding.ASCII.GetString(segment.Span));
            context.Transport.Input.AdvanceTo(read.Buffer.End);
            if (text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal) || read.IsCompleted)
                break;
        }
        string request = text.ToString();
        int line = request.IndexOf("\r\n", StringComparison.Ordinal);
        string start = line >= 0 ? request[..line] : request;
        string[] parts = start.Split(' ');
        string target = parts.Length >= 2 ? parts[1] : "/";
        int query = target.IndexOf('?');
        string path = query >= 0 ? target[..query] : target;
        Log.outDebug(LogFilter.Http, $"Shop plain {parts[0]} {target}");
        string? body = await Catalog(path, "");
        int status = body == null ? 404 : 200;
        body ??= "{\"error\":\"not found\"}";
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string type = body.Length > 0 && body[0] == '<' ? "text/html;charset=utf-8" : "application/json;charset=utf-8";
        string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
        await context.Transport.Output.WriteAsync(Encoding.ASCII.GetBytes(head));
        await context.Transport.Output.WriteAsync(payload);
        await context.Transport.Output.CompleteAsync();
    }

    static async Task<string?> Catalog(string path, string requestBody)
    {
        if (path.Contains("GetCurrentPages", StringComparison.OrdinalIgnoreCase))
        {
            string shown = requestBody.Length > 500 ? requestBody[..500] : requestBody;
            Log.outDebug(LogFilter.Http, $"Shop pages body {shown}");
            return await PagesJson(PlacementIds(requestBody));
        }
        if (path.Contains("GetProductsByStoreId", StringComparison.OrdinalIgnoreCase))
        {
            string shown = requestBody.Length > 500 ? requestBody[..500] : requestBody;
            Log.outDebug(LogFilter.Http, $"Shop products body {shown}");
            return await ProductsJson(requestBody);
        }
        if (path.EndsWith("/sso", StringComparison.OrdinalIgnoreCase))
            return "{\"access_token\":\"shop\",\"token_type\":\"Bearer\",\"jwt\":\"shop\"}";
        if (path.Contains("GetAccountDefaultCurrency", StringComparison.OrdinalIgnoreCase))
            return "{\"error\":null,\"currencyAlphaCode\":\"USD\",\"currencyCode\":\"USD\"}";
        if (path.Contains("GetBalance", StringComparison.OrdinalIgnoreCase))
            return "{\"error\":null,\"balance\":{\"balance\":0,\"softCapped\":false}}";
        if (path.Contains("QuoteDynamicBundle", StringComparison.OrdinalIgnoreCase)
            || path.Contains("GetOrderStatusByExternalTransactionId", StringComparison.OrdinalIgnoreCase))
            return "{\"error\":{\"category\":\"NON_RETRYABLE_FAILURE\",\"code\":\"NOT_FOUND\",\"message\":\"unavailable\"}}";
        if (path.Contains("/shop/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/product/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/family/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("gametime", StringComparison.OrdinalIgnoreCase))
            return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Battle.not shop</title></head><body style=\"background:#1a140c;color:#f3e6c8;font-family:Georgia,serif;margin:2rem\"><h1>Battle.not shop</h1><p>This storefront is served by Battle.not. The game catalog loads from this host.</p></body></html>";
        return null;
    }

    static List<string> PlacementIds(string body)
    {
        var ids = new List<string>();
        if (body.Length > 0)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("placementIds", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement el in arr.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.String && el.GetString() is string id && id.Length > 0)
                            ids.Add(id);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
        if (ids.Count == 0)
            ids.Add(PopPlacementId);
        return ids;
    }

    static async Task<List<ShopItem>> Items()
    {
        if (_db == null)
            return [];
        try
        {
            return await _db.LoadShopItems();
        }
        catch (Exception ex)
        {
            Log.outError(LogFilter.Http, $"Shop catalog {ex.Message}");
            return [];
        }
    }

    // CatalogShop_C.cpp: the only placement WoWB asks for first is shop2POPStr.
    // Each POP section must carry a placementID attribute. Those ids are the real
    // storefront pages, fetched with a second GetCurrentPages. An empty attribute
    // list leaves m_storefrontData empty and the shop UI says it is unavailable.
    const string PopPlacementId = "b94aa31b-f910-4ec8-a180-3fb6bdac3215";

    static string ChildPlacementId(string sectionId)
    {
        uint hash = 2166136261;
        foreach (char c in sectionId)
            hash = (hash ^ c) * 16777619;
        return string.Create(CultureInfo.InvariantCulture, $"a1000000-0000-4000-8000-{hash:x8}0001");
    }

    static async Task<string> PagesJson(List<string> placementIds)
    {
        List<ShopItem> items = await Items();
        var groups = items.GroupBy(item => item.SectionId)
            .OrderBy(group => group.Min(item => item.SectionOrder))
            .ToArray();
        bool pop = placementIds.Any(id => string.Equals(id, PopPlacementId, StringComparison.OrdinalIgnoreCase));
        Log.outDebug(LogFilter.Http, $"Shop pages {string.Join(",", placementIds)} items {items.Count} {(pop ? "pop" : "storefront")} section locale object");
        var placements = placementIds.Select(id =>
        {
            bool isPop = string.Equals(id, PopPlacementId, StringComparison.OrdinalIgnoreCase);
            var sections = groups
                .Where(group => isPop || string.Equals(ChildPlacementId(group.Key), id, StringComparison.OrdinalIgnoreCase))
                .Select(group =>
                {
                    ShopItem first = group.First();
                    string child = ChildPlacementId(first.SectionId);
                    // A declared object[] serializes each entry as {} and the client drops the placement.
                    // Matching a product returns the storefront section's category at +0xC0.
                    // The POP section's category is not the one that call reads, so an empty
                    // storefront attribute list comes back as category 0 and the panel stays empty.
                    SectionAttribute[] attributes = isPop
                        ? new[]
                        {
                            new SectionAttribute("placementID", child),
                            new SectionAttribute("placementId", child),
                            new SectionAttribute("categoryID", "1")
                        }
                        : new[]
                        {
                            new SectionAttribute("categoryID", "1")
                        };
                    // orderInPage is stored at section+0xC0 and returned as the category id.
                    // The not-found path returns 0, so an order of 0 is treated as no category.
                    int order = first.SectionOrder + 1;
                    Log.outDebug(LogFilter.Http, isPop
                        ? $"Shop pop section {first.SectionId} category 1 order {order} -> {child}"
                        : $"Shop storefront section {first.SectionId} category 1 order {order}");
                    return new
                    {
                        sectionId = "1",
                        name = first.SectionName,
                        orderInPage = order,
                        localization = new ShopText("enUS", first.SectionName, first.SectionName),
                        attributes,
                        productCollections = new[]
                        {
                            new
                            {
                                productCollectionId = "1",
                                personalizationStatus = 0,
                                orderInSection = 0,
                                productCollectionTypeId = 1,
                                items = group.Select(item => new
                                {
                                    name = item.Name,
                                    termDuration = 0,
                                    termTypeId = 0,
                                    isServerValidationRequired = false,
                                    orderInProductCollection = item.SortOrder,
                                    // 0 is no end. 2000000000 is already behind the client clock, and a microsecond end is not a 32-bit int.
                                    displayEndTimeMs = SaleEnd,
                                    // A quoted id is not IsInt(), so the item is dropped and the section stays empty.
                                    productCollectionItemValue = item.ProductId,
                                    productCollectionItemTypeId = 1,
                                    productClaimsToken = "",
                                    personalizedPercentageDiscount = 0,
                                    productData = new { productId = item.ProductId }
                                }).ToArray()
                            }
                        }
                    };
                }).ToArray();
            return new
            {
                placementId = id,
                nextScheduledPageDisplayTimeMs = 0,
                page = new { pageId = isPop ? "pop" : "home", sections }
            };
        }).ToArray();
        // A missing error member fails IsValid(). An error object is a failed call, and
        // CatalogShop_C.cpp then asserts the placement list is empty. null is success.
        string body = JsonSerializer.Serialize(new { placements });
        string json = "{\"error\":null," + body.TrimStart('{');
        if (pop)
            Log.outDebug(LogFilter.Http, json.Contains("\"sectionAttributeKey\":\"placementID\"", StringComparison.Ordinal)
                ? "Shop pages attributes ok"
                : "Shop pages attributes missing");
        return json;
    }

    readonly record struct SectionAttribute(string sectionAttributeKey, string sectionAttributeValue);

    readonly record struct ShopText(string locale, string name, string description);

    const int SaleEnd = 0;

    static long ServerTimeMs(string body)
    {
        // The client sends modifiedSinceFilterMs as unix milliseconds * 1000.
        // The last catalog clock was a raw millisecond value, and the client stored it as seconds.
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("modifiedSinceFilterMs", out JsonElement el)
                    && el.TryGetInt64(out long filter))
                    now = Math.Max(now, filter + 1);
            }
            catch (JsonException)
            {
            }
        }
        return now;
    }

    static async Task<string> ProductsJson(string requestBody)
    {
        List<ShopItem> items = await Items();
        long now = ServerTimeMs(requestBody);
        string catalog = string.Join(", ", items.Select(item => $"{item.ProductId}:{item.Name} {item.PriceCents} {item.Currency} {item.ProductType} license {item.LicenseId}"));
        Log.outDebug(LogFilter.Http, $"Shop products {items.Count} serverTimeMs {now} saleEnd {SaleEnd} [{catalog}]");
        var products = items.Select(item =>
        {
            decimal price = item.PriceCents / 100m;
            string amount = price.ToString("0.00", CultureInfo.InvariantCulture);
            string shown = "$" + amount;
            return new
            {
                productId = item.ProductId,
                name = item.Name,
                isServerValidationRequired = false,
                // CatalogShop asserts product->licenses.size() == 1. An empty array drops the product.
                // licenseId is the catalog action's spell ID. Black War Wolf uses source spell 22724.
                licenses = new[]
                {
                    new { licenseId = item.LicenseId, licenseType = 1, name = item.Name }
                },
                prices = new[]
                {
                    new
                    {
                        currencyCode = item.Currency,
                        currencyTypeId = 1,
                        // currencyTypeId is the int getter. currentPrice is the same getter as name.
                        currentPrice = amount,
                        originalPrice = amount,
                        localizedCurrentPrice = shown,
                        localizedOriginalPrice = shown
                    }
                },
                productAvailabilities = new[]
                {
                    new { startTimeMs = 0, purchaseEndTimeMs = SaleEnd, displayEndTimeMs = SaleEnd }
                },
                // The product parser reads "value". typeAttributeValue alone never filled it.
                productTypeAttributes = new[]
                {
                    new { typeAttributeName = "ProductType", value = item.ProductType, typeAttributeValue = item.ProductType }
                },
                productBundles = Array.Empty<object>(),
                storeContent = new { productImageUri = "" },
                virtualCurrencyGrants = Array.Empty<object>(),
                relatedProducts = Array.Empty<object>(),
                productSubscriptions = Array.Empty<object>(),
                storeSetting = new { attributes = Array.Empty<object>() },
                regionalSetting = new { gameServiceRegionId = 1, attributes = Array.Empty<object>() },
                // FindMember(localization) then locale, name, description. A locale-keyed map has none of those members.
                localization = new ShopText("enUS", item.Name, item.Description),
                additionalAssets = Array.Empty<object>()
            };
        }).ToArray();
        string productsBody = JsonSerializer.Serialize(new { paginationToken = "", serverTimeMs = now, products });
        return "{\"error\":null," + productsBody.TrimStart('{');
    }
}
