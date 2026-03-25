using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using XplorerCheatEditorWinForms.Models;

namespace XplorerCheatEditorWinForms.Services;

public sealed class Equalizer : IRomFormat
{
    public string FormatId => "equalizer_1.0_256kb";

    public static int? ManualCheatBlockStart { get; set; }

    private static readonly ConditionalWeakTable<CheatEntry, byte[]> s_rawCheatRecords = new();
    private static readonly ConditionalWeakTable<GameEntry, byte[]> s_rawGameTitles = new();
    private static readonly ConditionalWeakTable<RomDocument, byte[]> s_rawTrailingTail = new();

    private static readonly Dictionary<byte, string> s_tokenToWord = new()
    {
        [0xFF] = "Infinite",
        [0xFC] = "Unlimited",
        [0xF7] = "Have",
        [0xFA] = "Health",
        [0xF9] = "Energy",
        [0xF8] = "Lives",
        [0xFD] = "Player",
        [0xF6] = "Key",
		[0xFE] = "Always",
		[0xFB] = "Activate",
		[0x92] = "'",
		
	};

    private static readonly (string Phrase, byte Token)[] s_wordToToken = new[]
    {
        ("Infinite", (byte)0xFF),
		("Unlimited", (byte)0xFC),
        ("Have", (byte)0xF7),
        ("Health", (byte)0xFA),
        ("Energy", (byte)0xF9),
        ("Lives", (byte)0xF8),
        ("Player", (byte)0xFD),
        ("Key", (byte)0xF6),
		("Always", (byte)0xFE),
		("Activate", (byte)0xFB),
		("'", (byte)0x92),
    };


    private static readonly Dictionary<byte, char> s_symbolTokenToChar = new()
    {
        [0x9C] = '△',
    };

    private static readonly Dictionary<char, byte> s_symbolCharToToken = new()
    {
        ['△'] = 0x9C,
    };

    public bool TryProbe(byte[] rom, out string reason)
    {
        reason = "";
        if (rom == null || rom.Length < 0x1000)
        {
            reason = "ROM is too small.";
            return false;
        }

        if (ManualCheatBlockStart.HasValue)
        {
            int manual = ManualCheatBlockStart.Value;
            if (manual >= 0 && manual < rom.Length)
            {
                reason = $"Matched Equalizer-style cheat database via manual/json override at 0x{manual:X}.";
                return true;
            }
        }

        if (TryFindDatabaseStart(rom, out int start))
        {
            reason = $"Matched Equalizer-style cheat database (start=0x{start:X}).";
            return true;
        }

        reason = "Could not locate an Equalizer-style cheat database.";
        return false;
    }

    public RomDocument Parse(string sourcePath, byte[] rom)
    {
        if (!TryFindDatabaseStart(rom, out int start))
            throw new InvalidOperationException("Could not locate Equalizer cheat database.");

        var doc = new RomDocument
        {
            FormatId = FormatId,
            SourcePath = sourcePath,
            RomBytes = rom,
            CheatBlockOffset = start,
        };

        int p = start;
        int lastGood = start;
        int safety = 0;

        while (p < rom.Length && safety++ < 200000)
        {
            while (p < rom.Length && (rom[p] == 0x00 || rom[p] == 0xFF))
                p++;
            if (p >= rom.Length)
                break;

            int gameStart = p;
            if (!TryReadAsciiZ(rom, ref p, 96, out string title, out byte[] rawTitle))
                break;

            if (p >= rom.Length)
                break;

            int cheatCount = rom[p++];
            if (cheatCount < 0 || cheatCount > 0x7F)
                break;

            var game = new GameEntry { Title = title };
            AddOrUpdateGameTitleRaw(game, rawTitle);

            bool ok = true;
            for (int i = 0; i < cheatCount; i++)
            {
                int cheatStart = p;
                if (!TryParseCheat(rom, ref p, out var cheat))
                {
                    ok = false;
                    break;
                }
                AddOrUpdateCheatRaw(cheat, rom.Skip(cheatStart).Take(p - cheatStart).ToArray());
                game.Cheats.Add(cheat);
            }

            if (!ok)
            {
                if (game.Cheats.Count > 0)
                    doc.Games.Add(game);
                break;
            }

            doc.Games.Add(game);
            lastGood = p;
            if (p <= gameStart)
                break;
        }

        int visibleEnd = Math.Max(lastGood, start);

        // For Equalizer, the editable cheat space is the first large FF-padding run
        // after the parsed cheat database. There may be a few 00 bytes before that run,
        // and firmware/data may follow after the FF block.
        int ffRunStart = FindFirstPaddingRunStart(rom, visibleEnd, 0x20);
        if (ffRunStart >= 0)
            visibleEnd = ffRunStart;
        else
            while (visibleEnd < rom.Length && rom[visibleEnd] == 0x00)
                visibleEnd++;

        doc.CheatBlockEndOffset = visibleEnd;

        int padLen = GetImmediatePaddingLength(rom, visibleEnd);
        int tailStart = Math.Min(rom.Length, visibleEnd + padLen);
        AddOrUpdateTrailingTailRaw(doc, tailStart < rom.Length ? rom.Skip(tailStart).ToArray() : Array.Empty<byte>());
        return doc;
    }

    public byte[] Build(RomDocument doc)
    {
        byte[] rom = (byte[])doc.RomBytes.Clone();
        int start = doc.CheatBlockOffset;
        int end = Math.Clamp(doc.CheatBlockEndOffset, 0, rom.Length);
        int paddingLength = GetImmediatePaddingLength(rom, end);
        int capacityEnd = end + paddingLength;
        int capacity = Math.Max(0, capacityEnd - start);

        var buf = new List<byte>(Math.Max(capacity, 4096));

        var mergedGames = MergeGamesForBuild(doc.Games);

        foreach (var game in mergedGames)
        {
            if (TryGetRawGameTitle(game, out var rawTitle) && string.Equals(game.Title, Encoding.ASCII.GetString(rawTitle), StringComparison.Ordinal))
                buf.AddRange(rawTitle);
            else
                buf.AddRange(Encoding.ASCII.GetBytes(game.Title));
            buf.Add(0x00);

            var mergedCheats = MergeCheatsForBuild(game.Cheats);

            if (mergedCheats.Count > 0x7F)
                throw new InvalidOperationException($"Game '{game.Title}' has too many cheats ({mergedCheats.Count}).");

            buf.Add((byte)mergedCheats.Count);

            foreach (var cheat in mergedCheats)
            {
                if (TryGetRawCheatRecord(cheat, out var rawRecord) && RawCheatRecordMatches(cheat, rawRecord))
                {
                    buf.AddRange(rawRecord);
                    continue;
                }

                byte[] encodedName = EncodeCheatName(cheat.Name ?? string.Empty);
                buf.AddRange(encodedName);
                buf.Add(0x00);

                int lineCount = cheat.Lines.Count;
                if (lineCount == 0)
                {
                    buf.Add(0x00);
                    continue;
                }
                if (lineCount > 0x7F)
                    throw new InvalidOperationException($"Cheat '{cheat.Name}' has invalid line count {lineCount}.");

                buf.Add((byte)(0x80 | lineCount));
                foreach (var line in cheat.Lines)
                {
                    buf.Add((byte)((line.Address >> 24) & 0xFF));
                    buf.Add((byte)((line.Address >> 16) & 0xFF));
                    buf.Add((byte)((line.Address >> 8) & 0xFF));
                    buf.Add((byte)(line.Address & 0xFF));
                    buf.Add((byte)((line.Value >> 8) & 0xFF));
                    buf.Add((byte)(line.Value & 0xFF));
                }
            }
        }

        if (capacity > 0 && buf.Count > capacity)
            throw new InvalidOperationException($"Equalizer cheat database grew too large ({buf.Count} bytes) for available space ({capacity} bytes).");

        if (capacityEnd <= start)
            capacityEnd = Math.Min(rom.Length, start + buf.Count);

        for (int i = start; i < capacityEnd && i < rom.Length; i++)
            rom[i] = 0xFF;
        for (int i = 0; i < buf.Count && (start + i) < rom.Length; i++)
            rom[start + i] = buf[i];

        if (TryGetTrailingTailRaw(doc, out var tail) && tail.Length > 0)
        {
            int tailStart = Math.Min(rom.Length, capacityEnd);
            for (int i = 0; i < tail.Length && (tailStart + i) < rom.Length; i++)
                rom[tailStart + i] = tail[i];
        }

        return rom;
    }

    private static bool TryFindDatabaseStart(byte[] rom, out int start)
    {
        start = 0;

        if (ManualCheatBlockStart.HasValue)
        {
            int manual = ManualCheatBlockStart.Value;
            if (manual >= 0 && manual < rom.Length)
            {
                start = manual;
                return true;
            }
        }

        const int knownStart = 0x2C004;
        if (knownStart < rom.Length && LooksLikeDatabaseAt(rom, knownStart))
        {
            start = knownStart;
            return true;
        }

        byte[] title = Encoding.ASCII.GetBytes("3d Lemmings\0");
        for (int i = 0; i <= rom.Length - title.Length - 1; i++)
        {
            bool match = true;
            for (int j = 0; j < title.Length; j++)
            {
                if (rom[i + j] != title[j]) { match = false; break; }
            }
            if (match && LooksLikeDatabaseAt(rom, i))
            {
                start = i;
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeDatabaseAt(byte[] rom, int start)
    {
        if (rom == null || start < 0 || start >= rom.Length - 64)
            return false;

        int p = start;
        if (!TryReadAsciiZ(rom, ref p, 96, out string title1, out _))
            return false;
        if (!string.Equals(title1, "3d Lemmings", StringComparison.Ordinal) &&
            !string.Equals(title1, "Air Combat", StringComparison.Ordinal))
            return false;

        if (p >= rom.Length)
            return false;
        int cheatCount = rom[p++];
        if (cheatCount <= 0 || cheatCount > 0x7F)
            return false;

        if (!TryParseCheat(rom, ref p, out var cheat1))
            return false;
        if (cheat1.Lines.Count <= 0)
            return false;

        return true;
    }

    private static bool TryParseCheat(byte[] rom, ref int p, out CheatEntry cheat)
    {
        cheat = new CheatEntry();
        if (!TryReadCheatNameBytes(rom, ref p, out var rawName))
            return false;
        if (p >= rom.Length)
            return false;

        int flagsCount = rom[p++];

        cheat.NameRaw = rawName;
        cheat.Name = DecodeCheatName(rawName);

        // Equalizer also contains comment/warning entries with no code lines.
        // Treat a zero flags/count byte as a valid $NoCode cheat instead of a parse failure.
       if (flagsCount == 0x00 || flagsCount == 0x80)
            return true;

        int lineCount = flagsCount & 0x7F;
        if (lineCount <= 0 || lineCount > 0x7F)
            return false;
        if (p + (lineCount * 6) > rom.Length)
            return false;

        for (int i = 0; i < lineCount; i++)
        {
            uint addr = ByteHelpers.ReadU32BE(rom, p);
            ushort val = ByteHelpers.ReadU16BE(rom, p + 4);
            cheat.Lines.Add(new CodeLine { Address = addr, Value = val });
            p += 6;
        }

        return true;
    }

    private static bool TryReadAsciiZ(byte[] rom, ref int p, int maxLen, out string text, out byte[] raw)
    {
        text = string.Empty;
        raw = Array.Empty<byte>();
        if (p < 0 || p >= rom.Length)
            return false;

        int start = p;
        int len = 0;
        while (p < rom.Length && len < maxLen)
        {
            byte b = rom[p++];
            if (b == 0x00)
            {
                if (len == 0)
                    return false;
                raw = rom.Skip(start).Take(len).ToArray();
                text = Encoding.ASCII.GetString(raw);
                return IsLikelyTitle(text);
            }
            if (b < 0x20 || b > 0x7E)
                return false;
            len++;
        }
        return false;
    }

    private static bool IsLikelyTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 96)
            return false;
        return text.All(c => c >= 0x20 && c <= 0x7E);
    }

    private static bool TryReadCheatNameBytes(byte[] rom, ref int p, out byte[] raw)
    {
        raw = Array.Empty<byte>();
        if (p < 0 || p >= rom.Length)
            return false;

        int start = p;
        int len = 0;
        while (p < rom.Length && len < 160)
        {
            byte b = rom[p++];
            if (b == 0x00)
            {
                if (len == 0)
                    return false;
                raw = rom.Skip(start).Take(len).ToArray();
                return true;
            }

            bool ok = (b >= 0x20 && b <= 0x7E) || b >= 0xF0 || (b >= 0x80 && b <= 0x9F);
            if (!ok)
                return false;
            len++;
        }
        return false;
    }

    private static string DecodeCheatName(byte[] raw)
    {
        if (raw == null || raw.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(raw.Length + 16);
        foreach (byte b in raw)
        {
            if (s_symbolTokenToChar.TryGetValue(b, out var symbol))
            {
                sb.Append(symbol);
            }
            else if (s_tokenToWord.TryGetValue(b, out var word))
            {
                AppendWord(sb, word);
            }
            else if (b >= 0x20 && b <= 0x7E)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append($"<${b:X2}>");
            }
        }

        return NormalizeSpaces(sb.ToString());
    }

    private static byte[] EncodeCheatName(string text)
    {
        text ??= string.Empty;
        var ordered = s_wordToToken.OrderByDescending(t => t.Phrase.Length).ToArray();
        var bytes = new List<byte>(text.Length + 8);
        int i = 0;

        while (i < text.Length)
        {
            bool matched = false;
            foreach (var t in ordered)
            {
                if (i + t.Phrase.Length > text.Length)
                    continue;
                if (!text.AsSpan(i, t.Phrase.Length).Equals(t.Phrase.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsBoundary(text, i - 1) || !IsBoundary(text, i + t.Phrase.Length))
                    continue;

                bytes.Add(t.Token);
                i += t.Phrase.Length;
                if (i < text.Length && text[i] == ' ')
                    i++;
                matched = true;
                break;
            }
            if (matched)
                continue;

            if (s_symbolCharToToken.TryGetValue(text[i], out var symbolToken))
            {
                bytes.Add(symbolToken);
                i++;
                continue;
            }

            bytes.Add((byte)text[i]);
            i++;
        }

        return bytes.ToArray();
    }

    private static bool IsBoundary(string value, int idx)
    {
        if (idx < 0 || idx >= value.Length)
            return true;
        char c = value[idx];
        return char.IsWhiteSpace(c) || c == '+' || c == '-' || c == '(' || c == ')' || c == ':' || c == '/';
    }

    private static void AppendWord(StringBuilder sb, string word)
    {
        if (sb.Length > 0)
        {
            char last = sb[sb.Length - 1];
            if (!char.IsWhiteSpace(last) && last != '(' && last != ':' && last != '-')
                sb.Append(' ');
        }
        sb.Append(word);
        if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
            sb.Append(' ');
    }

    private static string NormalizeSpaces(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<GameEntry> MergeGamesForBuild(IEnumerable<GameEntry> games)
    {
        var existing = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Title) && TryGetRawGameTitle(g, out _))
            .ToList();

        var added = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Title) && !TryGetRawGameTitle(g, out _))
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new List<GameEntry>(existing.Count + added.Count);
        int ai = 0;
        foreach (var ex in existing)
        {
            while (ai < added.Count && string.Compare(added[ai].Title, ex.Title, StringComparison.OrdinalIgnoreCase) < 0)
            {
                merged.Add(added[ai]);
                ai++;
            }
            merged.Add(ex);
        }
        while (ai < added.Count)
        {
            merged.Add(added[ai]);
            ai++;
        }
        return merged;
    }

    private static List<CheatEntry> MergeCheatsForBuild(IList<CheatEntry> cheats)
    {
        var existing = cheats
            .Where(c => TryGetRawCheatRecord(c, out _))
            .ToList();

        var added = cheats
            .Where(c => !TryGetRawCheatRecord(c, out _))
            .OrderBy(c => c.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new List<CheatEntry>(existing.Count + added.Count);
        int ai = 0;
        foreach (var ex in existing)
        {
            string exName = ex.Name ?? string.Empty;
            while (ai < added.Count && string.Compare(added[ai].Name ?? string.Empty, exName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                merged.Add(added[ai]);
                ai++;
            }
            merged.Add(ex);
        }
        while (ai < added.Count)
        {
            merged.Add(added[ai]);
            ai++;
        }
        return merged;
    }

    private static int FindFirstPaddingRunStart(byte[] rom, int start, int minRunLength)
    {
        int p = Math.Clamp(start, 0, rom.Length);
        int runStart = -1;
        int runLen = 0;

        while (p < rom.Length)
        {
            byte b = rom[p];
            if (b == 0xFF)
            {
                if (runStart < 0)
                    runStart = p;
                runLen++;
                if (runLen >= minRunLength)
                    return runStart;
            }
            else if (b == 0x00 && runStart < 0)
            {
                // allow a small gap of zero bytes before the real FF padding starts
            }
            else
            {
                runStart = -1;
                runLen = 0;
            }
            p++;
        }

        return -1;
    }

    private static int GetImmediatePaddingLength(byte[] rom, int start)
    {
        int p = Math.Clamp(start, 0, rom.Length);
        while (p < rom.Length && rom[p] == 0xFF)
            p++;
        return p - start;
    }

    private static void AddOrUpdateCheatRaw(CheatEntry key, byte[] value)
    {
        s_rawCheatRecords.Remove(key);
        s_rawCheatRecords.Add(key, value);
    }

    private static void AddOrUpdateGameTitleRaw(GameEntry key, byte[] value)
    {
        s_rawGameTitles.Remove(key);
        s_rawGameTitles.Add(key, value);
    }

    private static void AddOrUpdateTrailingTailRaw(RomDocument doc, byte[] value)
    {
        s_rawTrailingTail.Remove(doc);
        s_rawTrailingTail.Add(doc, value);
    }

    private static bool TryGetTrailingTailRaw(RomDocument doc, out byte[] tail)
    {
        tail = Array.Empty<byte>();
        if (!s_rawTrailingTail.TryGetValue(doc, out var found) || found == null)
            return false;
        tail = found;
        return true;
    }

    private static bool TryGetRawGameTitle(GameEntry game, out byte[] rawTitle)
    {
        rawTitle = Array.Empty<byte>();
        if (!s_rawGameTitles.TryGetValue(game, out var found) || found == null)
            return false;
        rawTitle = found;
        return true;
    }

    private static bool TryGetRawCheatRecord(CheatEntry cheat, out byte[] rawRecord)
    {
        rawRecord = Array.Empty<byte>();
        if (!s_rawCheatRecords.TryGetValue(cheat, out var found) || found == null)
            return false;
        rawRecord = found;
        return true;
    }

    private static bool RawCheatRecordMatches(CheatEntry cheat, byte[] rawRecord)
    {
        if (rawRecord == null || rawRecord.Length == 0)
            return false;
        int p = 0;
        if (!TryReadCheatNameBytes(rawRecord, ref p, out var rawName))
            return false;
        if (p >= rawRecord.Length)
            return false;
        int flagsCount = rawRecord[p++];
        if (!string.Equals(DecodeCheatName(rawName), cheat.Name ?? string.Empty, StringComparison.Ordinal))
            return false;

        if (flagsCount == 0x00 || flagsCount == 0x80)
            return cheat.Lines.Count == 0 && p == rawRecord.Length;

        int lineCount = flagsCount & 0x7F;
        if (lineCount <= 0 || p + lineCount * 6 != rawRecord.Length)
            return false;
        if (cheat.Lines.Count != lineCount)
            return false;
        for (int i = 0; i < lineCount; i++)
        {
            uint addr = ByteHelpers.ReadU32BE(rawRecord, p);
            ushort val = ByteHelpers.ReadU16BE(rawRecord, p + 4);
            if (cheat.Lines[i].Address != addr || cheat.Lines[i].Value != val)
                return false;
            p += 6;
        }
        return true;
    }
}
