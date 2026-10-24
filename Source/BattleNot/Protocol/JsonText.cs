using System.Text;

namespace BattleNot;

static class JsonText
{
    public static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public static byte[] Compress(string json)
    {
        byte[] raw = Encoding.UTF8.GetBytes(json);
        byte[] withNul = new byte[raw.Length + 1];
        raw.CopyTo(withNul, 0);
        using var output = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)withNul.Length);
        output.Write(length);
        using (var zlib = new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(withNul);
        return output.ToArray();
    }
}
