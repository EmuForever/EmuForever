using BattleNot;
using Framework.Configuration;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using MySqlConnector;
using System.Net;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Framework.Logging.ConsoleTimestamps.Enable();
Console.Title = "Battle.not Server";

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
if (!ConfigMgr.Load("Configs/BattleNot.conf"))
{
    Environment.ExitCode = 1;
    return;
}

Log.outHeader(LogFilter.Server, " ");
Log.outHeader(LogFilter.Server, @"╔╗  ╔═╗ ╔╦╗ ╔╦╗ ╦   ╔═╗   ╔╗╔ ╔═╗ ╔╦╗   ╔═╗ ╔═╗ ╦═╗ ╦  ╦ ╔═╗ ╦═╗");
Log.outHeader(LogFilter.Server, @"╠╩╗ ╠═╣  ║   ║  ║   ║╣    ║║║ ║ ║  ║    ╚═╗ ║╣  ╠╦╝ ╚╗╔╝ ║╣  ╠╦╝");
Log.outHeader(LogFilter.Server, @"╚═╝ ╩ ╩  ╩   ╩  ╩═╝ ╚═╝ . ╝╚╝ ╚═╝  ╩    ╚═╝ ╚═╝ ╩╚═  ╚╝  ╚═╝ ╩╚═");
Log.outHeader(LogFilter.Server, " ");

try
{
    string connectionString = new MySqlConnectionStringBuilder
    {
        Server = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.Host", "127.0.0.1"),
        Port = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.Port", 3306u),
        UserID = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.Username", "root"),
        Password = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.Password", ""),
        Database = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.Database", "auth"),
        SslMode = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.SSL", false) ? MySqlSslMode.Preferred : MySqlSslMode.None,
        AllowPublicKeyRetrieval = true,
        MaximumPoolSize = (uint)Math.Clamp(ConfigMgr.GetDefaultValue("LoginDatabaseInfo.MaxPoolSize", 16), 1, 128),
        MinimumPoolSize = 0,
        ConnectionIdleTimeout = 60,
        ConnectionTimeout = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.ConnectionTimeout", 15u),
        DefaultCommandTimeout = ConfigMgr.GetDefaultValue("LoginDatabaseInfo.CommandTimeout", 60u)
    }.ConnectionString;
    var db = new BnotDb(connectionString, ConfigMgr.GetDefaultValue("Shop.WorldDatabase", "world"));
    var realms = new RealmCatalog();
    
    if (args.Contains("--friends-check"))
    {
        Totp.SelfTest();
        await BnetFriends.SelfTest(db);
        return;
    }
    
    if (args.Contains("--check"))
    {
        BnetSrp.SelfTest();
        await realms.Refresh(db.Realms);
        foreach (RealmEntry realm in realms.Snapshot())
            Log.outInfo(LogFilter.Server, $"  {realm.Name} {realm.SubRegion} build {realm.Build} content {realm.ContentSetId} {realm.Addresses[0]}:{realm.Port}");
        Log.outInfo(LogFilter.Server, "SRP self-test passed and the auth database is readable.");
        return;
    }
    
    await realms.Refresh(db.Realms);
    Log.outInfo(LogFilter.Server, $"Loaded {realms.Snapshot().Count} realm(s) from auth.");
    // Do not accept login, REST, or RPC traffic until all Battle.net/social schema work is complete.
    // This keeps an early launcher request from racing the lazy first-use table migration.
    await db.Schema.EnsureBnetIdentity();
    ShopRest.Use(db.Shop);
    try
    {
        List<ShopItem> shopItems = await db.Shop.LoadShopItems(refresh: true);
        Log.outInfo(LogFilter.Server, $"Shop catalog warmed with {shopItems.Count} enabled product(s).");
    }
    catch (Exception ex)
    {
        Log.outWarn(LogFilter.Server, $"Shop catalog warm-up deferred: {ex.GetType().Name}: {ex.Message}");
    }
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancel.Cancel();
    };
    
    IPAddress bindIP = IPAddress.Parse(ConfigMgr.GetDefaultValue("BindIP", "127.0.0.1"));
    int restPort = ConfigMgr.GetDefaultValue("LoginREST.Port", 8081);
    int realmsStateUpdateDelay = ConfigMgr.GetDefaultValue("RealmsStateUpdateDelay", 10);
    string certificateFile = ResolvePath(ConfigMgr.GetDefaultValue("CertificateFile", ""));
    string privateKeyFile = ResolvePath(ConfigMgr.GetDefaultValue("PrivateKeyFile", ""));
    var host = new BnetHost(db, realms, certificateFile, privateKeyFile);
    // All server settings come from BattleNot.conf; do not load ASP.NET's appsettings JSON files.
    var rest = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });
    rest.Host.UseConsoleLifetime();
    rest.Services.AddRouting();
    rest.Logging.ClearProviders();
    rest.Logging.AddProvider(new FrameworkLogProvider());
    rest.Logging.SetMinimumLevel((Microsoft.Extensions.Logging.LogLevel)ConfigMgr.GetDefaultValue("Logging.AspNetLevel", 3));
    rest.WebHost.UseKestrel(options =>
    {
        options.Listen(bindIP, restPort, listen =>
        {
            if (!host.DevCertificate)
                listen.UseHttps(host.Certificate);
        });
    });
    WebApplication app = rest.Build();
    app.Use(async (ctx, next) =>
    {
        bool supportRequest = ctx.Request.Path.StartsWithSegments("/support", StringComparison.OrdinalIgnoreCase)
            || ctx.Request.Path.StartsWithSegments("/login/sso", StringComparison.OrdinalIgnoreCase);
        if (ctx.Connection.LocalPort == 8443)
            Log.outDebug(LogFilter.Server, $"Shop {ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString}");
        await next();
        if (supportRequest)
            Log.outDebug(LogFilter.Server, $"Support HTTP {ctx.Response.StatusCode} {ctx.Request.Method} {ctx.Request.Host}{ctx.Request.Path}");
        else if (ctx.Response.StatusCode == StatusCodes.Status403Forbidden)
            Log.outWarn(LogFilter.Server, $"REST HTTP 403 {ctx.Request.Method} {ctx.Request.Host}{ctx.Request.Path}{ctx.Request.QueryString}");
        if (ctx.Response.StatusCode == 404 && ctx.Connection.LocalPort == restPort)
            Log.outDebug(LogFilter.Server, $"REST unmatched {ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString}");
    });
    new LoginRest(db, realms, host.DevCertificate).Map(app);
    new SupportRest(db).Map(app);
    new SocialRest(db).Map(app);
    new ShopRest().Map(app);
    
    _ = Task.Run(async () =>
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, realmsStateUpdateDelay)), cancel.Token);
                await realms.Refresh(db.Realms);
                await db.Auth.ExpireBans();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.outError(LogFilter.Server, $"Realm refresh failed: {ex.Message}");
            }
        }
    }, cancel.Token);
    
    Log.outInfo(LogFilter.Server, $"Login REST {bindIP}:{restPort} ({(host.DevCertificate ? "http" : "https")} login form)");
    string crlFile = Path.Combine(Path.GetDirectoryName(certificateFile) ?? "", "forever.crl");
    Log.outInfo(LogFilter.Server, $"Shop {bindIP}:8443 ({(host.DevCertificate ? "http" : "https")} storefront)");
    Log.outInfo(LogFilter.Server, $"CRL {bindIP}:8082/forever.crl");
    await Task.WhenAll(
        host.Listen(cancel.Token),
        app.RunAsync(cancel.Token),
        ShopRest.Listen(bindIP, 8443, host.Certificate, host.DevCertificate, cancel.Token),
        ShopRest.ListenCrl(bindIP, 8082, crlFile, cancel.Token));

}
catch (OperationCanceledException)
{
    Log.outInfo(LogFilter.Server, "Battle.not server stopped.");
}
catch (Exception exception)
{
    Log.outFatal(LogFilter.Server, $"Battle.not server stopped unexpectedly: {exception}");
    Environment.ExitCode = 1;
}

static string ResolvePath(string path)
{
    if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
        return path;
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
