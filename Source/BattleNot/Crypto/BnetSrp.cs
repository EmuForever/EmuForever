using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace BattleNot;

static class Big
{
    public static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        BigInteger remainder = value % modulus;
        return remainder.Sign < 0 ? remainder + modulus : remainder;
    }

    public static BigInteger FromBigEndian(ReadOnlySpan<byte> bytes) =>
        bytes.IsEmpty ? BigInteger.Zero : new BigInteger(bytes, isUnsigned: true, isBigEndian: true);

    public static BigInteger FromHex(string hex)
    {
        hex = hex.Trim();
        if (hex.Length == 0)
            return BigInteger.Zero;
        if ((hex.Length & 1) == 1)
            hex = "0" + hex;
        return FromBigEndian(Convert.FromHexString(hex));
    }

    public static string ToHex(BigInteger value)
    {
        if (value.Sign == 0)
            return "0";
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        return Convert.ToHexString(bytes);
    }

    public static byte[] ToFixed(BigInteger value, int size)
    {
        byte[] raw = value.Sign == 0 ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == size)
            return raw;
        byte[] padded = new byte[size];
        if (raw.Length > size)
            raw.AsSpan(raw.Length - size).CopyTo(padded);
        else
            raw.CopyTo(padded.AsSpan(size - raw.Length));
        return padded;
    }

    public static byte[] Broken(BigInteger value)
    {
        int bits = value.Sign == 0 ? 0 : (int)value.GetBitLength();
        int bytes = Math.Max(value.Sign == 0 ? 0 : value.GetByteCount(isUnsigned: true), (bits + 8) >> 3);
        if (bytes == 0)
            bytes = 1;
        return ToFixed(value, bytes);
    }

    public static BigInteger RandomBelow(BigInteger modulus)
    {
        int bits = (int)modulus.GetBitLength();
        int size = (bits + 7) / 8;
        int spare = size * 8 - bits;
        Span<byte> buffer = size <= 256 ? stackalloc byte[size] : new byte[size];
        for (int attempt = 0; attempt < 8; attempt++)
        {
            RandomNumberGenerator.Fill(buffer);
            buffer[0] &= (byte)(0xFF >> spare);
            buffer[0] |= (byte)(0x80 >> spare);
            BigInteger value = FromBigEndian(buffer);
            value = Mod(value, modulus);
            if (!value.IsZero)
                return value;
        }
        return BigInteger.One;
    }
}

sealed class BnetSrp
{
    public const string Nv1 = "86A7F6DEEB306CE519770FE37D556F29944132554DED0BD68205E27F3231FEF5A10108238A3150C59CAF7B0B6478691C13A6ACF5E1B5ADAFD4A943D4A21A142B800E8A55F8BFBAC700EB77A7235EE5A609E350EA9FC19F10D921C2FA832E4461B7125D38D254A0BE873DFC27858ACB3F8B9F258461E4373BC3A6C2A9634324AB";
    public const string Nv2 = "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF747359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB694B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73";

    readonly int _version;
    readonly int _width;
    readonly BigInteger _n;
    readonly BigInteger _g;
    readonly BigInteger _k;
    readonly BigInteger _v;
    readonly BigInteger _b;
    readonly byte[] _salt;
    bool _used;

    public byte Version => (byte)_version;
    public uint Iterations => _version == 2 ? 15000u : 1u;
    public string Modulus => Big.ToHex(_n);
    public string Generator => "2";
    public string Username { get; }
    public string SaltHex => Convert.ToHexString(_salt);
    public string PublicB => Big.ToHex(_publicB);
    readonly BigInteger _publicB;

    public BnetSrp(int version, string username, byte[] salt, byte[] verifier)
    {
        _version = version == 2 ? 2 : 1;
        _width = _version == 2 ? 256 : 128;
        _n = Big.FromHex(_version == 2 ? Nv2 : Nv1);
        _g = new BigInteger(2);
        _k = Big.FromBigEndian(SHA256.HashData(Join(Big.ToFixed(_n, _width), Big.ToFixed(_g, _width))));
        // bnot_accounts.verifier is stored little-endian (BigNumber::ToByteVector default).
        _v = verifier.Length == 0 ? BigInteger.Zero : new BigInteger(verifier, isUnsigned: true, isBigEndian: false);
        _salt = salt;
        Username = username;
        _b = Big.RandomBelow(_n - 1);
        _publicB = Big.Mod(BigInteger.ModPow(_g, _b, _n) + _v * _k, _n);
    }

    public bool CheckPassword(string username, string password) =>
        _v == BigInteger.ModPow(_g, CalculateX(username, password, _salt), _n);

    public bool TryProve(string publicAHex, string clientM1Hex, out string serverM2)
    {
        serverM2 = "";
        if (_used)
            return false;
        _used = true;

        BigInteger a = Big.FromHex(publicAHex);
        BigInteger clientM1 = Big.FromHex(clientM1Hex);
        if (Big.Mod(a, _n).IsZero)
            return false;

        BigInteger u = Big.FromBigEndian(SHA256.HashData(Join(Big.ToFixed(a, _width), Big.ToFixed(_publicB, _width))));
        if (Big.Mod(u, _n).IsZero)
            return false;

        BigInteger s = BigInteger.ModPow(a * BigInteger.ModPow(_v, u, _n), _b, _n);
        BigInteger ourM1 = HashBroken(a, _publicB, s);
        if (ourM1 != clientM1)
            return false;

        serverM2 = Big.ToHex(HashBroken(a, clientM1, s));
        return true;
    }

    public static byte[] Verifier(int version, string username, string password, byte[] salt)
    {
        BigInteger n = Big.FromHex(version == 2 ? Nv2 : Nv1);
        BigInteger x = CalculateX(version, username, password, salt);
        return LittleEndian(BigInteger.ModPow(new BigInteger(2), x, n));
    }

    static BigInteger CalculateX(int version, string username, string password, byte[] salt)
    {
        if (version != 2)
            return Big.FromBigEndian(SHA256.HashData(Join(salt, SHA256.HashData(Encoding.UTF8.GetBytes(username + ":" + password)))));

        byte[] material = Encoding.UTF8.GetBytes(username + ":" + password);
        byte[] xBytes = Rfc2898DeriveBytes.Pbkdf2(material, salt, 15000, HashAlgorithmName.SHA512, 64);
        BigInteger x = Big.FromBigEndian(xBytes);
        if ((xBytes[0] & 0x80) != 0)
            x -= BigInteger.One << 512;
        return Big.Mod(x, Big.FromHex(Nv2) - 1);
    }

    BigInteger CalculateX(string username, string password, byte[] salt) => CalculateX(_version, username, password, salt);

    static BigInteger HashBroken(params BigInteger[] values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (BigInteger value in values)
            hash.AppendData(Big.Broken(value));
        return Big.FromBigEndian(hash.GetHashAndReset());
    }

    static byte[] Join(byte[] left, byte[] right)
    {
        byte[] joined = new byte[left.Length + right.Length];
        left.CopyTo(joined, 0);
        right.CopyTo(joined.AsSpan(left.Length));
        return joined;
    }

    public static void SelfTest()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        const string user = "USER";
        const string pass = "secret";
        byte[] verifier = LittleEndian(BigInteger.ModPow(new BigInteger(2), CalculateX(1, user, pass, salt), Big.FromHex(Nv1)));
        var server = new BnetSrp(1, user, salt, verifier);
        if (!server.CheckPassword(user, pass) || server.CheckPassword(user, "nope"))
            throw new InvalidOperationException("SRP password check failed");

        BigInteger n = Big.FromHex(Nv1);
        BigInteger g = new BigInteger(2);
        BigInteger x = CalculateX(1, user, pass, salt);
        BigInteger v = BigInteger.ModPow(g, x, n);
        BigInteger k = Big.FromBigEndian(SHA256.HashData(Join(Big.ToFixed(n, 128), Big.ToFixed(g, 128))));
        BigInteger a = Big.RandomBelow(n - 1);
        BigInteger capitalA = BigInteger.ModPow(g, a, n);
        BigInteger capitalB = Big.FromHex(server.PublicB);
        BigInteger u = Big.FromBigEndian(SHA256.HashData(Join(Big.ToFixed(capitalA, 128), Big.ToFixed(capitalB, 128))));
        BigInteger s = BigInteger.ModPow(Big.Mod(capitalB - k * v, n), a + u * x, n);
        BigInteger m1 = HashBroken(capitalA, capitalB, s);
        if (!server.TryProve(Big.ToHex(capitalA), Big.ToHex(m1), out string m2))
            throw new InvalidOperationException("SRP proof failed");
        if (m2 != Big.ToHex(HashBroken(capitalA, m1, s)))
            throw new InvalidOperationException("SRP server evidence mismatch");
    }

    static byte[] LittleEndian(BigInteger value)
    {
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        int end = raw.Length;
        while (end > 1 && raw[end - 1] == 0)
            end--;
        return raw.AsSpan(0, end).ToArray();
    }
}

// WoW game-account SRP6 (SHA-1). The world session itself uses the bnet key;
// this verifier is what the account row stores.
static class GameSrp
{
    const string Nhex = "894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7";

    public static (byte[] Salt, byte[] Verifier) Make(string username, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] inner = SHA1.HashData(Encoding.UTF8.GetBytes(username + ":" + password));
        byte[] joined = new byte[salt.Length + inner.Length];
        salt.CopyTo(joined, 0);
        inner.CopyTo(joined.AsSpan(salt.Length));
        BigInteger x = Big.FromBigEndian(SHA1.HashData(joined));
        BigInteger v = BigInteger.ModPow(new BigInteger(7), x, Big.FromHex(Nhex));
        byte[] raw = v.ToByteArray(isUnsigned: true, isBigEndian: false);
        byte[] verifier = new byte[32];
        raw.AsSpan(0, Math.Min(32, raw.Length)).CopyTo(verifier);
        return (salt, verifier);
    }
}
