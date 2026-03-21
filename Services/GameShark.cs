using System.Linq;

using System.Text;
using System.Runtime.CompilerServices;
using XplorerCheatEditorWinForms.Models;

namespace XplorerCheatEditorWinForms.Services;

public sealed class Gameshark : IRomFormat
{
    public string FormatId => "gameshark_3_2";

    public static int? ManualCheatBlockStart { get; set; }

    private static readonly ConditionalWeakTable<CheatEntry, byte[]> s_rawCheatRecords = new();
    private static readonly ConditionalWeakTable<GameEntry, byte[]> s_rawGameTitles = new();
    private static readonly ConditionalWeakTable<RomDocument, byte[]> s_rawTrailingTail = new();

    public bool TryProbe(byte[] rom, out string reason)
    {
        reason = "";

        if (rom == null || rom.Length < 0x1000)
        {
            reason = "ROM is too small.";
            return false;
        }

        if (TryFindDatabaseStart(rom, out int start))
        {
            reason = $"Matched GameShark 3.2-style cheat database at 0x{start:X}.";
            return true;
        }

        reason = "No GameShark 3.2-style cheat database found.";
        return false;
    }

    public RomDocument Parse(string sourcePath, byte[] rom)
    {
        if (!TryFindDatabaseStart(rom, out int start))
            throw new InvalidOperationException("Could not locate GameShark cheat database.");

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
            while (p < rom.Length && (rom[p] == 0x00 || rom[p] == 0xFF)) p++;
            if (p >= rom.Length) break;

            int gameStart = p;
            if (!TryReadAsciiZ(rom, ref p, 96, out string titleRaw))
                break;

            string title = titleRaw.Trim();
            if (!IsLikelyGameTitle(title))
                break;

            if (p >= rom.Length)
                break;

            int cheatCount = rom[p++];
			if (cheatCount > 255)
                break;

            var game = new GameEntry { Title = title };
            AddOrUpdateGameTitleRaw(game, rom.Skip(gameStart).Take((p - gameStart) - 1).ToArray());
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
                break;

            doc.Games.Add(game);
            lastGood = p;

            if (p <= gameStart)
                break;
        }

        doc.CheatBlockEndOffset = FindCheatBlockEnd(rom, start);
        if (doc.CheatBlockEndOffset > lastGood)
            AddOrUpdateTrailingTailRaw(doc, rom.Skip(lastGood).Take(doc.CheatBlockEndOffset - lastGood).ToArray());
        else
            AddOrUpdateTrailingTailRaw(doc, Array.Empty<byte>());
        return doc;
    }

    public byte[] Build(RomDocument doc)
    {
        byte[] rom = (byte[])doc.RomBytes.Clone();

        int start = doc.CheatBlockOffset;
        int paddingLength = GetImmediatePaddingLength(rom, doc.CheatBlockEndOffset);
        int capacityEnd = doc.CheatBlockEndOffset + paddingLength;
        int capacity = capacityEnd - start;
        if (capacity < 0)
            throw new InvalidOperationException("Invalid GameShark cheat block capacity.");

        var buf = new List<byte>(capacity);

        void WriteNameZ(string text)
        {
            var enc = EncodeCheatName(text ?? string.Empty);
            buf.AddRange(enc);
            buf.Add(0x00);
        }

        void WriteCheatNameZ(CheatEntry cheat, string text)
        {
            text ??= string.Empty;

            if (cheat.NameRaw != null)
            {
                string decodedRaw = DecodeCheatName(cheat.NameRaw);
                if (string.Equals(text, decodedRaw, StringComparison.Ordinal))
                {
                    buf.AddRange(cheat.NameRaw);
                    buf.Add(0x00);
                    return;
                }
            }

            WriteNameZ(text);
        }

		var existingGames = doc.Games
		    .Where(g => !string.IsNullOrWhiteSpace(g.Title) && HasOriginalGameRecord(g))
		    .ToList();

		var newGames = doc.Games
		    .Where(g => !string.IsNullOrWhiteSpace(g.Title) && !HasOriginalGameRecord(g))
		    .ToList();

		var orderedGames = new List<GameEntry>(existingGames);

		foreach (var newGame in newGames)
		{
		    int insertIndex = orderedGames.FindIndex(g =>
		        string.Compare(newGame.Title ?? string.Empty, g.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase) < 0);

		    if (insertIndex < 0)
		        orderedGames.Add(newGame);
		    else
		        orderedGames.Insert(insertIndex, newGame);
		}

        foreach (var game in orderedGames)
        {
            if (RawGameTitleMatches(game, out var rawTitle))
                buf.AddRange(rawTitle);
            else
                buf.AddRange(Encoding.ASCII.GetBytes(game.Title));
            buf.Add(0x00);

			var cheats = game.Cheats
			    .OrderBy(c => HasOriginalCheatRecord(c) ? 1 : 0)
			    .ToList();

            if (cheats.Count > 255)
                throw new InvalidOperationException($"Game '{game.Title}' has too many cheats ({cheats.Count}).");

            buf.Add((byte)cheats.Count);

            foreach (var cheat in cheats)
            {
                if (RawCheatRecordMatches(cheat, out var rawRecord))
                {
                    buf.AddRange(rawRecord);
                    continue;
                }

                string cheatName = cheat.Name ?? string.Empty;
                bool enabled = true;

                if (cheatName.EndsWith(" .off", StringComparison.Ordinal))
                {
                    enabled = false;
                    cheatName = cheatName.Substring(0, cheatName.Length - 5).TrimEnd();
                }

                if ((cheat.IsNoCodeNote || cheat.Lines.Count == 0) &&
                    s_rawCheatRecords.TryGetValue(cheat, out var rawNote))
                {
                    string decodedRawNote = DecodeNoteRecord(rawNote);
                    if (string.Equals(cheatName, decodedRawNote, StringComparison.Ordinal))
                    {
                        buf.AddRange(rawNote);
                        continue;
                    }
                }

                if (cheat.IsNoCodeNote || cheat.Lines.Count == 0)
                {
                    var noteParts = cheatName
                        .Split(new[] { " / " }, StringSplitOptions.None)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    if (noteParts.Count == 0)
                        noteParts.Add(string.Empty);

                    for (int i = 0; i < noteParts.Count; i++)
                    {
                        WriteNameZ(noteParts[i]);
                        if (i < noteParts.Count - 1)
                            buf.Add(0x00);
                    }
                    continue;
                }

                WriteCheatNameZ(cheat, cheatName);

                int lineCount = cheat.Lines.Count;
                if (lineCount <= 0 || lineCount > 0x7F)
                    throw new InvalidOperationException($"Cheat '{cheat.Name}' has invalid line count {lineCount}.");

                byte flagsCount = (byte)(enabled ? (0x80 | lineCount) : lineCount);
                buf.Add(flagsCount);

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

        if (TryGetTrailingTailRaw(doc, out var trailingTail) && trailingTail.Length > 0)
            buf.AddRange(trailingTail);

        if (buf.Count > capacity)
            throw new InvalidOperationException($"GameShark cheat database grew too large ({buf.Count} bytes) for available space ({capacity} bytes).");

        if (start < 2)
            throw new InvalidOperationException("Cheat block offset is too small to contain GameShark bank/count bytes.");

		int totalGames = orderedGames.Count;
		ushort gameCount = (ushort)totalGames;

		rom[start - 2] = (byte)(gameCount >> 8);   // high byte
		rom[start - 1] = (byte)(gameCount & 0xFF); // low byte

        for (int i = start; i < capacityEnd; i++)
            rom[i] = 0xFF;

        for (int i = 0; i < buf.Count; i++)
            rom[start + i] = buf[i];

        return rom;
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
        if (!s_rawTrailingTail.TryGetValue(doc, out var foundTail) || foundTail == null)
            return false;

        tail = foundTail;
        return true;
    }

    private static bool HasOriginalGameRecord(GameEntry game)
    {
        return game != null && s_rawGameTitles.TryGetValue(game, out var raw) && raw != null && raw.Length > 0;
    }

    private static bool HasOriginalCheatRecord(CheatEntry cheat)
    {
        return cheat != null && s_rawCheatRecords.TryGetValue(cheat, out var raw) && raw != null && raw.Length > 0;
    }

    private static bool RawGameTitleMatches(GameEntry game, out byte[] rawTitle)
    {
        rawTitle = Array.Empty<byte>();
        if (!s_rawGameTitles.TryGetValue(game, out var foundRawTitle) || foundRawTitle == null)
            return false;

        rawTitle = foundRawTitle;

        string decoded = Encoding.ASCII.GetString(rawTitle);
        return string.Equals(game.Title ?? string.Empty, decoded, StringComparison.Ordinal);
    }

    private static bool RawCheatRecordMatches(CheatEntry cheat, out byte[] rawRecord)
    {
        rawRecord = Array.Empty<byte>();
        if (!s_rawCheatRecords.TryGetValue(cheat, out var foundRawRecord) || foundRawRecord == null || foundRawRecord.Length == 0)
            return false;

        rawRecord = foundRawRecord;

        string currentName = cheat.Name ?? string.Empty;
        bool currentEnabled = true;
        if (currentName.EndsWith(" .off", StringComparison.Ordinal))
        {
            currentEnabled = false;
            currentName = currentName.Substring(0, currentName.Length - 5).TrimEnd();
        }

        int p = 0;
        if (!TryReadCheatNameBytes(rawRecord, ref p, out var rawName))
            return false;
        if (p >= rawRecord.Length)
            return false;

        if (!LooksLikeCodeRecord(rawRecord, p))
        {
            string decodedNote = DecodeNoteRecord(rawRecord);
            return (cheat.IsNoCodeNote || cheat.Lines.Count == 0)
                && string.Equals(currentName, decodedNote, StringComparison.Ordinal);
        }

        int flagsCount = rawRecord[p++];
        int lineCount = flagsCount & 0x7F;
        bool enabled = (flagsCount & 0x80) != 0;

        if (lineCount <= 0 || p + (lineCount * 6) != rawRecord.Length)
            return false;

        string decodedName = DecodeCheatName(rawName);
		if (!string.Equals(currentName.TrimEnd(), decodedName.TrimEnd(), StringComparison.Ordinal))
		return false;
        if (currentEnabled != enabled)
            return false;
        if (cheat.IsNoCodeNote || cheat.Lines.Count != lineCount)
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

    private static bool TryFindDatabaseStart(byte[] rom, out int start)
    {
        start = 0;
        if (rom == null || rom.Length < 0x1000)
            return false;

       if (ManualCheatBlockStart.HasValue)
{
    int manual = ManualCheatBlockStart.Value;
    if (manual >= 0 && manual < rom.Length)
            {
                start = manual;
                return true;
            }
        }

        // GameShark 3.2 dump we are targeting starts here.
        const int knownStart = 0x5F004;
        if (LooksLikeDatabaseAt(rom, knownStart))
        {
            start = knownStart;
            return true;
        }

        // Conservative fallback: look for the known first-title sequence anywhere in the ROM.
        // This avoids false positives on Xplorer ROMs.
        byte[] aceCombat2 = Encoding.ASCII.GetBytes("ACE COMBAT 2");
        for (int i = 0; i <= rom.Length - aceCombat2.Length - 1; i++)
        {
            bool match = true;
            for (int j = 0; j < aceCombat2.Length; j++)
            {
                if (rom[i + j] != aceCombat2[j]) { match = false; break; }
            }
            if (!match) continue;

            if (LooksLikeDatabaseAt(rom, i))
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

        if (!TryReadAsciiZ(rom, ref p, 96, out string title1))
            return false;
        if (!string.Equals(title1.Trim(), "ACE COMBAT 2", StringComparison.Ordinal))
            return false;

        if (p >= rom.Length) return false;
        int cheatCount1 = rom[p++];
        if (cheatCount1 != 3)
            return false;

        if (!TryReadCheatNameBytes(rom, ref p, out var cheat1NameRaw))
            return false;
        if (DecodeCheatName(cheat1NameRaw) != "Extra Planes")
            return false;
        if (p >= rom.Length) return false;
        int flagsCount1 = rom[p++];
        int lineCount1 = flagsCount1 & 0x7F;
        if (lineCount1 != 8)
            return false;
        if (p + lineCount1 * 6 > rom.Length)
            return false;
        p += lineCount1 * 6;

        if (!TrySkipCheat(rom, ref p)) return false; // Infinite Fuel
        if (!TrySkipCheat(rom, ref p)) return false; // Infinite Missiles

        if (!TryReadAsciiZ(rom, ref p, 96, out string title2))
            return false;
        if (!string.Equals(title2.Trim(), "ADIDAS POWER SOCCER", StringComparison.Ordinal))
            return false;

        if (p >= rom.Length) return false;
        int cheatCount2 = rom[p++];
        if (cheatCount2 != 2)
            return false;

        return TrueAfterTwoCheats(rom, p);
    }

    private static bool TrueAfterTwoCheats(byte[] rom, int p)
    {
        if (!TrySkipCheat(rom, ref p)) return false;
        if (!TrySkipCheat(rom, ref p)) return false;
        return true;
    }

    private static bool TrySkipCheat(byte[] rom, ref int p)
    {
        if (!TryReadCheatNameBytes(rom, ref p, out _))
            return false;
        if (p >= rom.Length) return false;

        // Some GameShark entries are text-only note records with no code bytes.
        if (!LooksLikeCodeRecord(rom, p))
        {
            if (p < rom.Length && rom[p] == 0x00)
                p++;

            // Allow at most one extra text-only line.
            if (p < rom.Length && !LooksLikeCodeRecord(rom, p) && !LooksLikeGameRecordAt(rom, p))
            {
                int temp = p;
                if (TryReadCheatNameBytes(rom, ref temp, out _))
                    p = temp;
            }

            return true;
        }

        int flagsCount = rom[p++];
        int lineCount = flagsCount & 0x7F;
        if (lineCount <= 0 || lineCount > 255)
            return false;

        int bytesNeeded = lineCount * 6;
        if (p + bytesNeeded > rom.Length)
            return false;
        p += bytesNeeded;
        return true;
    }

    private static bool TryParseCheat(byte[] rom, ref int p, out CheatEntry cheat)
    {
        cheat = new CheatEntry();

        if (!TryReadCheatNameBytes(rom, ref p, out var nameBytes))
            return false;
        if (p >= rom.Length)
            return false;

        // Text-only / note record with no code bytes.
        if (!LooksLikeCodeRecord(rom, p))
        {
            var rawNote = new List<byte>(nameBytes.Length + 8);
            rawNote.AddRange(nameBytes);
            rawNote.Add(0x00);

            string name = DecodeCheatName(nameBytes);

            if (p < rom.Length && rom[p] == 0x00)
            {
                rawNote.Add(0x00);
                p++;
            }

            // Allow at most one extra text-only line.
            if (p < rom.Length && !LooksLikeCodeRecord(rom, p) && !LooksLikeGameRecordAt(rom, p))
            {
                int temp = p;
                if (TryReadCheatNameBytes(rom, ref temp, out var extraBytes))
                {
                    string extra = DecodeCheatName(extraBytes);
                    if (!string.IsNullOrWhiteSpace(extra))
                        name = string.IsNullOrWhiteSpace(name) ? extra : name + " / " + extra;

                    rawNote.AddRange(extraBytes);
                    rawNote.Add(0x00);
                    p = temp;
                }
            }

            cheat.NameRaw = rawNote.ToArray();
            cheat.Name = name;
            if (!cheat.Name.EndsWith(" .off", StringComparison.Ordinal))
                cheat.Name += " .off";
            cheat.IsNoCodeNote = true;
            return true;
        }

        int flagsCount = rom[p++];
        int lineCount = flagsCount & 0x7F;
        bool enabled = (flagsCount & 0x80) != 0;

        if (lineCount <= 0 || lineCount > 255)
            return false;
        if (p + lineCount * 6 > rom.Length)
            return false;

        cheat.NameRaw = nameBytes.ToArray();
        cheat.Name = DecodeCheatName(nameBytes);
        if (!enabled)
            cheat.Name += " .off";

        for (int i = 0; i < lineCount; i++)
        {
            uint addr = ByteHelpers.ReadU32BE(rom, p);
            ushort val = ByteHelpers.ReadU16BE(rom, p + 4);
            cheat.Lines.Add(new CodeLine { Address = addr, Value = val });
            p += 6;
        }

        return true;
    }


    private static bool LooksLikeCodeRecord(byte[] rom, int p)
    {
        if (p < 0 || p >= rom.Length)
            return false;

        int flagsCount = rom[p];
        int lineCount = flagsCount & 0x7F;
        if (lineCount <= 0 || lineCount > 255)
            return false;

        int bytesNeeded = 1 + (lineCount * 6);
        return p + bytesNeeded <= rom.Length;
    }

    private static bool LooksLikeGameRecordAt(byte[] rom, int p)
    {
        if (p < 0 || p >= rom.Length)
            return false;

        int temp = p;
        if (!TryReadAsciiZ(rom, ref temp, 96, out string title))
            return false;

        title = title.Trim();
        if (!IsLikelyGameTitle(title))
            return false;

        if (temp >= rom.Length)
            return false;

        int cheatCount = rom[temp];
        return cheatCount > 0 && cheatCount <= 255;
    }

    private static bool TryReadCheatNameBytes(byte[] rom, ref int p, out byte[] nameBytes)
    {
        nameBytes = Array.Empty<byte>();
        var bytes = new List<byte>(32);
        int max = Math.Min(rom.Length, p + 96);

        while (p < max)
        {
            byte b = rom[p++];
            if (b == 0x00)
            {
                if (bytes.Count == 0) return false;
                nameBytes = bytes.ToArray();
                return true;
            }

            if (!IsAllowedCheatNameByte(b))
                return false;

            bytes.Add(b);
        }

        return false;
    }

    private static bool TryReadAsciiZ(byte[] rom, ref int p, int maxLen, out string value)
    {
        value = string.Empty;
        int start = p;
        int end = Math.Min(rom.Length, p + maxLen);
        var sb = new StringBuilder();

        while (p < end)
        {
            byte b = rom[p++];
            if (b == 0x00)
            {
                value = sb.ToString();
                return sb.Length > 0;
            }

            if (b < 0x20 || b > 0x7E)
                return false;

            sb.Append((char)b);
        }

        p = start;
        return false;
    }

    private static int GetImmediatePaddingLength(byte[] rom, int start)
    {
        if (rom == null || start < 0 || start >= rom.Length)
            return 0;

        int i = start;
        while (i < rom.Length && rom[i] == 0xFF)
            i++;

        return i - start;
    }

    private static int FindCheatBlockEnd(byte[] rom, int start)
    {
        for (int i = start; i < rom.Length; i++)
            if (ByteHelpers.LooksLikePaddingFF(rom, i, minRun: 128))
                return i;

        return rom.Length;
    }


    private static bool IsAllowedCheatNameByte(byte b)
    {
		return b != 0x00;
    
    }

    private static bool IsLikelyGameTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 96)
            return false;

        int letters = 0;
        foreach (char ch in title)
        {
            if (char.IsLetterOrDigit(ch)) letters++;
            if (char.IsControl(ch)) return false;
        }

        return letters >= 1;
    }

    private static byte[] EncodeCheatName(string text)
    {
        text ??= string.Empty;

        var tokens = new (string Phrase, byte Token)[]
        {
            ("Always", 0xFE),
            ("Infinite", 0xFF),
            ("Ammo", 0xFD),
            ("Unlimited", 0xFC),
            ("Special", 0xFB),
            ("Level", 0xFA),
            ("Energy", 0xF9),
            ("Lives", 0xF8),
            ("Have", 0xF7),
            ("Keys", 0xF6),
            ("△", 0x9C),
            ("□", 0x9E),
        };

       static bool IsBoundary(string value, int idx)
	{
    if (idx < 0 || idx >= value.Length)
        return true;

    char c = value[idx];
    return char.IsWhiteSpace(c)
        || c == '+'
        || c == '-'
        || c == '('
        || c == ')'
        || c == ':'
        || c == '/';
	}

        var orderedTokens = tokens.OrderByDescending(t => t.Phrase.Length).ToArray();
        var bytes = new List<byte>(text.Length + 8);
        int i = 0;

        while (i < text.Length)
        {
            bool matched = false;

            foreach (var t in orderedTokens)
            {
                if (i + t.Phrase.Length > text.Length)
                    continue;

                if (!text.AsSpan(i, t.Phrase.Length).Equals(t.Phrase.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    continue;

                int end = i + t.Phrase.Length;
				if (!IsBoundary(text, i - 1) || !IsBoundary(text, end))
				continue;

                bytes.Add(t.Token);
                i = end;

                if (i < text.Length && text[i] == ' ')
                    i++;

                matched = true;
                break;
            }

            if (matched)
                continue;

            bytes.Add((byte)text[i]);
            i++;
        }

        return bytes.ToArray();
    }

    private static string DecodeNoteRecord(byte[] raw)
    {
        if (raw == null || raw.Length == 0)
            return string.Empty;

        var parts = new List<string>();
        var current = new List<byte>();

        for (int i = 0; i < raw.Length; i++)
        {
            byte b = raw[i];
            if (b == 0x00)
            {
                if (current.Count > 0)
                {
                    parts.Add(DecodeCheatName(current.ToArray()));
                    current.Clear();
                }
            }
            else
            {
                current.Add(b);
            }
        }

        if (current.Count > 0)
            parts.Add(DecodeCheatName(current.ToArray()));

        return string.Join(" / ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string DecodeCheatName(byte[] raw)
    {
        if (raw == null || raw.Length == 0)
            return string.Empty;

        var sb = new StringBuilder(raw.Length + 16);
        foreach (byte b in raw)
        {
            switch (b)
            {
                case 0xFF:
                    AppendWord(sb, "Infinite");
                    break;
                case 0xFE:
                    AppendWord(sb, "Always");
                    break;
                case 0xFD:
                    AppendWord(sb, "Ammo");
                    break;
                case 0xFC:
                    AppendWord(sb, "Unlimited");
                    break;
                case 0xFB:
                    AppendWord(sb, "Special");
                    break;
                case 0xFA:
                    AppendWord(sb, "Level");
                    break;
                case 0xF9:
                    AppendWord(sb, "Energy");
                    break;
                case 0xF8:
                    AppendWord(sb, "Lives");
                    break;
                case 0xF7:
                    AppendWord(sb, "Have");
                    break;
                case 0xF6:
                    AppendWord(sb, "Keys");
                    break;
                case 0x9C:
                    AppendWord(sb, "△");
                    break;
                case 0x9E:
                    AppendWord(sb, "□");
                    break;
                default:
					if (b >= 0x20 && b <= 0x7E)
					sb.Append((char)b);
				else
					sb.Append($"[TK:{b:X2}]");
				break;
            }
        }

        return sb.ToString();
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

        // GameShark token bytes usually represent whole words, and many entries do not
        // store an explicit trailing space after the token. Add one here; NormalizeSpaces()
        // will collapse duplicates when the ROM already contains a real space byte.
        if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
            sb.Append(' ');
    }

    private static string NormalizeSpaces(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
