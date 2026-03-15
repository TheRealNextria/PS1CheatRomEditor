using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace XplorerCheatEditorWinForms.Services;

/// <summary>
/// Recompressor for Xplorer/Xploder FX packed cheat blocks.
/// Implements fixed 14-bit LZW encoding and bitpacking compatible with FxTokenDecoder.
/// </summary>
public static class FxRecompressor
{
    public readonly struct FxVariant
    {
        public FxVariant(int offset, int compressedEnd, bool msbFirstBits, bool swapBytesInWord, bool resetOnDictFull)
        {
            Offset = offset;
            CompressedEnd = compressedEnd;
            MsbFirstBits = msbFirstBits;
            SwapBytesInWord = swapBytesInWord;
            ResetOnDictFull = resetOnDictFull;
        }

        public int Offset { get; }
        public int CompressedEnd { get; }
        public bool MsbFirstBits { get; }
        public bool SwapBytesInWord { get; }
        public bool ResetOnDictFull { get; }
    }

    public static bool TryLoadVariantFile(string path, out FxVariant variant, out string error)
    {
        variant = default;
        error = "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = "File not found: " + path;
            return false;
        }

        int offset = -1;
        int end = -1;
        bool? msb = null;
        bool? swap = null;
        bool? reset = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();

            if (key.Equals("Offset", StringComparison.OrdinalIgnoreCase))
                offset = ParseHexInt(val);
            else if (key.Equals("CompressedEnd", StringComparison.OrdinalIgnoreCase))
                end = ParseHexInt(val);
            else if (key.Equals("MsbFirstBits", StringComparison.OrdinalIgnoreCase))
                msb = ParseBool(val);
            else if (key.Equals("SwapBytesInWord", StringComparison.OrdinalIgnoreCase))
                swap = ParseBool(val);
            else if (key.Equals("ResetOnDictFull", StringComparison.OrdinalIgnoreCase))
                reset = ParseBool(val);
        }

        if (offset < 0 || end < 0)
        {
            error = "Offset/CompressedEnd missing in decoded_variant.txt";
            return false;
        }
        if (msb is null || swap is null || reset is null)
        {
            error = "Variant flags missing in decoded_variant.txt";
            return false;
        }

        variant = new FxVariant(offset, end, msb.Value, swap.Value, reset.Value);
        return true;
    }

    private static int ParseHexInt(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(2);
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return -1;
        return v;
    }

    private static bool ParseBool(string s)
    {
        if (bool.TryParse(s, out var b)) return b;
        if (s == "1") return true;
        if (s == "0") return false;
        return false;
    }

    public static byte[] Recompress(byte[] decodedPayload, FxVariant variant)
    {
        if (decodedPayload == null) throw new ArgumentNullException(nameof(decodedPayload));
        var codes = LzwEncode14(decodedPayload, variant.ResetOnDictFull);
        return Pack14BitCodes(codes, variant.MsbFirstBits, variant.SwapBytesInWord);
    }

    private static List<int> LzwEncode14(byte[] data, bool resetOnFull)
    {
        // Fixed-width 14-bit LZW.
        // Dictionary starts with 0..255.
        // Next codes from 256..16383.
        const int MaxCode = 16383;

        var dict = new Dictionary<(int prefix, byte b), int>(capacity: 32768);
        int nextCode = 256;

        var outCodes = new List<int>(data.Length);
        if (data.Length == 0) return outCodes;

        int w = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            byte k = data[i];
            if (dict.TryGetValue((w, k), out int wk))
            {
                w = wk;
                continue;
            }

            outCodes.Add(w);

            if (nextCode <= MaxCode)
            {
                dict[(w, k)] = nextCode++;
            }
            else if (resetOnFull)
            {
                dict.Clear();
                nextCode = 256;
            }

            w = k;
        }

        outCodes.Add(w);
        return outCodes;
    }

    private static byte[] Pack14BitCodes(List<int> codes, bool msbFirst, bool swapBytesInWord)
    {
        // Inverse of Read14Codes from decoder.
        var outBytes = new List<byte>((codes.Count * 14 + 7) / 8 + 8);

        int bitBuf = 0;
        int bitCount = 0;

        void FlushWordIfNeeded()
        {
            if (!swapBytesInWord) return;
            // When swapping, output is expected to be in swapped 16-bit words.
            // We implement this by swapping each pair of bytes after writing.
            int n = outBytes.Count;
            if (n >= 2 && (n % 2 == 0))
            {
                byte a = outBytes[n - 2];
                outBytes[n - 2] = outBytes[n - 1];
                outBytes[n - 1] = a;
            }
        }

        if (msbFirst)
        {
            foreach (var c in codes)
            {
                int code = c & 0x3FFF;
                bitBuf = (bitBuf << 14) | code;
                bitCount += 14;

                while (bitCount >= 8)
                {
                    int shift = bitCount - 8;
                    byte b = (byte)((bitBuf >> shift) & 0xFF);
                    outBytes.Add(b);
                    bitCount -= 8;
                    bitBuf &= (1 << bitCount) - 1;
                    FlushWordIfNeeded();
                }
            }
            if (bitCount > 0)
            {
                byte b = (byte)((bitBuf << (8 - bitCount)) & 0xFF);
                outBytes.Add(b);
                FlushWordIfNeeded();
            }
        }
        else
        {
            foreach (var c in codes)
            {
                int code = c & 0x3FFF;
                bitBuf |= (code << bitCount);
                bitCount += 14;

                while (bitCount >= 8)
                {
                    byte b = (byte)(bitBuf & 0xFF);
                    outBytes.Add(b);
                    bitBuf >>= 8;
                    bitCount -= 8;
                    FlushWordIfNeeded();
                }
            }
            if (bitCount > 0)
            {
                outBytes.Add((byte)(bitBuf & 0xFF));
                FlushWordIfNeeded();
            }
        }

        return outBytes.ToArray();
    }
}
