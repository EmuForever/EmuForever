namespace BattleNot;

sealed class AuthRow
{
    public uint Id;
    public int Version;
    public byte[] Salt = [];
    public byte[] Verifier = [];
    public uint FailedLogins;
    public string Ticket = "";
    public uint TicketExpiry;
    public bool Banned;
    public string AuthenticatorSecret = "";
}
