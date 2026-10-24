namespace BattleNot;

sealed class BnetAccount
{
    public uint Id;
    public string Email = "";
    public bool LockedToIp;
    public string LockCountry = "";
    public string LastIp = "";
    public uint TicketExpiry;
    public bool Banned;
    public bool PermanentlyBanned;
    public string Ticket = "";
    public List<GameAccount> Games = [];
}

sealed class GameAccount
{
    public uint Id;
    public string Name = "";
    public string DisplayName = "";
    public bool Banned;
    public bool PermanentlyBanned;
    public uint BanDate;
    public uint UnbanDate;
    public byte Security;
    public Dictionary<uint, byte> CharacterCounts = [];
    public Dictionary<string, LastPlayed> LastPlayed = [];
}

sealed class LastPlayed
{
    public uint RealmAddress;
    public string SubRegion = "";
    public string Name = "";
    public ulong Guid;
    public uint Time;
}
