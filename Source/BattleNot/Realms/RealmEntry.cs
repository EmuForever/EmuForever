using System.Net;

namespace BattleNot;

sealed class RealmEntry
{
    public uint Id;
    public string Name = "";
    public List<IPAddress> Addresses = [];
    public ushort Port;
    public byte Icon;
    public byte Type;
    public byte LegacyFlags;
    public bool Hardcore;
    public byte Timezone;
    public byte AllowedSecurity;
    public float Population;
    public uint Build;
    public byte Region;
    public byte Battlegroup;
    public uint ContentSetId;
    public bool WorldUp = true;

    public uint Address => ((uint)Region << 24) | ((uint)Battlegroup << 16) | (ushort)Id;
    public string SubRegion => $"{Region}-{Battlegroup}-{Id}";
    public byte PopulationState => WorldUp ? LegacyPopulation() : (byte)0;
    public byte Flags => (byte)((LegacyFlags & 0x01) != 0 ? 0x01 : 0);

    byte LegacyPopulation()
    {
        byte flags = (byte)(LegacyFlags & ~0x02);
        if ((flags & 0x20) != 0) return 5;
        if ((flags & 0x40) != 0) return 4;
        if ((flags & 0x80) != 0 || Population > 0.95f) return 6;
        if (Population > 0.66f) return 3;
        if (Population > 0.33f) return 2;
        return 1;
    }

    public string RulesetLabel()
    {
        if (Hardcore)
            return "Hardcore";
        return Icon switch
        {
            1 => "PvP",
            4 or 6 => "RP",
            8 => "RP PvP",
            _ => "Normal"
        };
    }

    public IPAddress AddressFor(IPAddress client)
    {
        if (Addresses.Count > 1 && IPAddress.IsLoopback(client))
            return Addresses[1];
        return Addresses.Count > 0 ? Addresses[0] : IPAddress.Loopback;
    }
}
