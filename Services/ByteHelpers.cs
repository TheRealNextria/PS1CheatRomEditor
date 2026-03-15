using System.Text;

namespace XplorerCheatEditorWinForms.Services;

internal static class ByteHelpers
{
    public static bool IsLikelyAscii(byte b) => b is >= 0x20 and <= 0x7E;

    public static string ReadCStringAscii(byte[] rom, int offset, int maxLen, out int bytesReadIncludingNull)
    {
        bytesReadIncludingNull = 0;
        if (offset < 0 || offset >= rom.Length)
            return "";

        var sb = new StringBuilder();
        for (int i = 0; i < maxLen && offset + i < rom.Length; i++)
        {
            byte c = rom[offset + i];
            if (c == 0x00)
            {
                bytesReadIncludingNull = i + 1;
                return sb.ToString();
            }
            if (!IsLikelyAscii(c))
            {
                bytesReadIncludingNull = 0;
                return "";
            }
            sb.Append((char)c);
        }
        bytesReadIncludingNull = 0;
        return "";
    }

    public static bool LooksLikePaddingFF(byte[] rom, int offset, int minRun = 32)
    {
        int run = 0;
        for (int i = offset; i < rom.Length && run < minRun; i++)
        {
            if (rom[i] != 0xFF) return false;
            run++;
        }
        return run >= minRun;
    }

    public static uint ReadU32LE(byte[] rom, int offset)
        => (uint)(rom[offset] | (rom[offset + 1] << 8) | (rom[offset + 2] << 16) | (rom[offset + 3] << 24));

    public static ushort ReadU16LE(byte[] rom, int offset)
        => (ushort)(rom[offset] | (rom[offset + 1] << 8));

    public static uint ReadU32BE(byte[] rom, int offset)
        => (uint)((rom[offset] << 24) | (rom[offset + 1] << 16) | (rom[offset + 2] << 8) | rom[offset + 3]);

    public static ushort ReadU16BE(byte[] rom, int offset)
        => (ushort)((rom[offset] << 8) | rom[offset + 1]);

    public static void WriteU32LE(Span<byte> span, int offset, uint val)
    {
        span[offset] = (byte)(val & 0xFF);
        span[offset + 1] = (byte)((val >> 8) & 0xFF);
        span[offset + 2] = (byte)((val >> 16) & 0xFF);
        span[offset + 3] = (byte)((val >> 24) & 0xFF);
    }

    public static void WriteU16LE(Span<byte> span, int offset, ushort val)
    {
        span[offset] = (byte)(val & 0xFF);
        span[offset + 1] = (byte)((val >> 8) & 0xFF);
    }

    public static void WriteU32BE(Span<byte> span, int offset, uint val)
    {
        span[offset] = (byte)((val >> 24) & 0xFF);
        span[offset + 1] = (byte)((val >> 16) & 0xFF);
        span[offset + 2] = (byte)((val >> 8) & 0xFF);
        span[offset + 3] = (byte)(val & 0xFF);
    }

    public static void WriteU16BE(Span<byte> span, int offset, ushort val)
    {
        span[offset] = (byte)((val >> 8) & 0xFF);
        span[offset + 1] = (byte)(val & 0xFF);
    }
}
