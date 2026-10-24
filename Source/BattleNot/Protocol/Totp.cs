using System.Security.Cryptography;
using System.Text;

namespace BattleNot;

static class Totp
{
    public static bool Matches(string base32, string code)
    {
        string digits = Digits(code);
        if (digits.Length != 6)
            return false;
        byte[] key = Base32(base32);
        if (key.Length == 0)
            return false;
        long step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (long delta = -1; delta <= 1; delta++)
            if (Code(key, step + delta) == digits)
                return true;
        return false;
    }

    public static string Digits(string code)
    {
        var digits = new StringBuilder();
        foreach (char c in code)
            if (c is >= '0' and <= '9')
                digits.Append(c);
        return digits.ToString();
    }

    public static void SelfTest()
    {
        byte[] key = Base32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        if (key.Length != 20 || Code(key, 59 / 30) != "287082")
            throw new InvalidOperationException("TOTP self-test failed");
    }

    static string Code(byte[] key, long step)
    {
        byte[] data = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            data[i] = (byte)(step & 0xff);
            step >>= 8;
        }
        byte[] hash = HMACSHA1.HashData(key, data);
        int offset = hash[^1] & 0x0f;
        int bin = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (bin % 1000000).ToString("D6");
    }

    static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        text = text.Trim().TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        if (text.Length == 0)
            return [];
        int buffer = 0;
        int bits = 0;
        var output = new List<byte>();
        foreach (char c in text)
        {
            int value = alphabet.IndexOf(c);
            if (value < 0)
                return [];
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xff));
            }
        }
        return output.ToArray();
    }
}
