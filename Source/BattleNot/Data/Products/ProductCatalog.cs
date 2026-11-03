using System.Text.Json;

namespace BattleNot;

sealed class BnotProductCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<BnotProduct> Products { get; set; } = [];
}

sealed class BnotProduct
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "World of Warcraft";
    public bool Enabled { get; set; }
    public ProductDistribution Distribution { get; set; } = new();
    public ProductClient Client { get; set; } = new();
    public ProductServices Services { get; set; } = new();
    public ProductUpdates Updates { get; set; } = new();

    public bool Accepts(uint build) => build >= Client.MinimumBuild &&
        (Client.SupportedBuilds.Length == 0 || Client.SupportedBuilds.Contains(build));
    public bool SupportsRealmBuild(uint build) => Client.SupportedBuilds.Contains(build);
}

sealed class ProductDistribution
{
    public string Provider { get; set; } = "blizzard-tact";
    public string ProductCode { get; set; } = "";
    public string Region { get; set; } = "us";
    public string? VersionsUrl { get; set; }
    public string? CdnsUrl { get; set; }
}

sealed class ProductClient
{
    public string Directory { get; set; } = "";
    public string Executable { get; set; } = "Wow.exe";
    public string Architecture { get; set; } = "x64";
    public uint MinimumBuild { get; set; }
    public uint[] SupportedBuilds { get; set; } = [];
    public string Locale { get; set; } = "enUS";
}

sealed class ProductServices
{
    public string Provider { get; set; } = "blizzard";
    public string? Portal { get; set; }
    public int AuthPort { get; set; } = 1119;
    public string? RealmDiscovery { get; set; }
    public string? WorldEndpointSource { get; set; }
}

sealed class ProductUpdates
{
    public bool Enabled { get; set; }
    public string Strategy { get; set; } = "manifest";
    public string? ManifestUrl { get; set; }
}

static class ProductCatalogJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static BnotProductCatalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<BnotProductCatalog>(json, Options)
            ?? throw new InvalidDataException("Missing product catalog.");
        Validate(catalog);
        return catalog;
    }

    public static void Validate(BnotProductCatalog catalog)
    {
        if (catalog.SchemaVersion != 1 || catalog.Products == null)
            throw new InvalidDataException("Unsupported product catalog.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in catalog.Products)
        {
            if (product == null) throw new InvalidDataException("Catalog contains a null product.");
            void Require(bool condition, string field, string reason)
            {
                if (!condition) throw new InvalidDataException($"Product '{product.Id}': {field} {reason}");
            }
            Require(!string.IsNullOrWhiteSpace(product.Id) && product.Id.Length <= 64 &&
                product.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'), "id", "must contain only letters, digits, '-' or '_'.");
            Require(ids.Add(product.Id), "id", "is duplicated.");
            Require(!string.IsNullOrWhiteSpace(product.Name), "name", "must not be empty.");
            Require(product.Distribution != null && product.Client != null && product.Services != null && product.Updates != null,
                "configuration", "must include distribution, client, services and updates objects.");
            Require(SafeName(product.Client!.Directory), "client.directory", "must be one relative folder name.");
            Require(SafeName(product.Client.Executable), "client.executable", "must be a file name, not a path.");
            Require(product.Services!.AuthPort >= 1 && product.Services.AuthPort <= 65535, "services.authPort", "must be between 1 and 65535.");
            Require(string.IsNullOrEmpty(product.Services.Portal) || Uri.CheckHostName(product.Services.Portal) != UriHostNameType.Unknown,
                "services.portal", "must be a hostname or IP without a scheme or port.");
            product.Client.SupportedBuilds ??= [];
        }
    }

    static bool SafeName(string value) => !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." &&
        !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));
}
