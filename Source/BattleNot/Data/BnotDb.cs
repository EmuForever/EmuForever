using MySqlConnector;

namespace BattleNot;

// Entry point for every query BattleNot runs. Each table group has its own class under Data/.
sealed class BnotDb
{
    readonly string _cs;
    readonly string _shopWorldDatabase;

    public BnotDb(string connectionString, string shopWorldDatabase = "world")
    {
        _cs = connectionString;
        if (shopWorldDatabase.Length == 0 || shopWorldDatabase.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Invalid shop world database", nameof(shopWorldDatabase));
        _shopWorldDatabase = shopWorldDatabase;

        Schema = new BnetSchema(this);
        Auth = new AuthDb(this);
        Realms = new RealmDb(this);
        Shop = new ShopDb(this);
        Tags = new BattleTagDb(this);
        Friends = new FriendsDb(this);
        Notices = new NoticeDb(this);
        Profiles = new ProfileDb(this);
        Chat = new ChatDb(this);
        Gifts = new GiftDb(this);
    }

    public BnetSchema Schema { get; }
    public AuthDb Auth { get; }
    public RealmDb Realms { get; }
    public ShopDb Shop { get; }
    public BattleTagDb Tags { get; }
    public FriendsDb Friends { get; }
    public NoticeDb Notices { get; }
    public ProfileDb Profiles { get; }
    public ChatDb Chat { get; }
    public GiftDb Gifts { get; }

    // Auth database.
    public MySqlConnection Connect() => new(_cs);

    // World database that holds the shop_* tables.
    public MySqlConnection ConnectShop() => new(new MySqlConnectionStringBuilder(_cs) { Database = _shopWorldDatabase }.ConnectionString);
}
