using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XplorerCheatEditorWinForms.Services;

public sealed class RomJsonDatabase
{
    [JsonPropertyName("roms")]
    public List<RomJsonEntry> Roms { get; set; } = new();
}

public sealed class RomJsonEntry
{
    public string Name { get; set; } = "";
    public string VersionOffset { get; set; } = "";
    public string VersionString { get; set; } = "";
    public string CheatOffset { get; set; } = "";
    public string RomSize { get; set; } = "";
    public bool Compressed { get; set; }
}

public static class RomJsonMatcher
{
    private static RomJsonDatabase? _cache;

    private static RomJsonDatabase? Load()
    {
        if (_cache != null) return _cache;

        var combined = new RomJsonDatabase();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        void LoadOne(string fileName)
        {
            string jsonPath = Path.Combine(AppContext.BaseDirectory, "Tools", fileName);
            if (!File.Exists(jsonPath))
                return;

            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var db = JsonSerializer.Deserialize<RomJsonDatabase>(json, opts);
            if (db?.Roms != null && db.Roms.Count > 0)
                combined.Roms.AddRange(db.Roms);
        }

		LoadOne("roms.json");
		LoadOne("GSroms.json");
		LoadOne("Gsroms.json");
		LoadOne("Equalizer.json");

        _cache = combined.Roms.Count > 0 ? combined : null;
        return _cache;
    }

    public static RomJsonEntry? TryMatch(byte[] romBytes, string? romPath = null)
    {
        var db = Load();
        if (db?.Roms == null || db.Roms.Count == 0)
            return null;

        var byVersion = TryMatchByVersion(db, romBytes);
        if (byVersion != null)
            return byVersion;

        if (!string.IsNullOrEmpty(romPath))
        {
            string fileName = Path.GetFileNameWithoutExtension(romPath);
            var byName = TryMatchByFileName(db, fileName);
            if (byName != null)
                return byName;
        }

        return null;
    }

    private static RomJsonEntry? TryMatchByVersion(RomJsonDatabase db, byte[] romBytes)
    {
        foreach (var entry in db.Roms.OrderByDescending(e => e.VersionString?.Length ?? 0))
        {
            if (string.IsNullOrWhiteSpace(entry.VersionOffset) || string.IsNullOrEmpty(entry.VersionString))
                continue;

            if (!TryParseHexInt(entry.VersionOffset, out int offset))
                continue;

            if (!string.IsNullOrWhiteSpace(entry.RomSize))
            {
                if (!TryParseHexInt(entry.RomSize, out int expectedSize))
                    continue;

                if (romBytes.Length != expectedSize)
                    continue;
            }

            if (offset < 0 || offset + entry.VersionString.Length > romBytes.Length)
                continue;

            string actual = Encoding.ASCII.GetString(romBytes, offset, entry.VersionString.Length);
            if (actual == entry.VersionString)
                return entry;
        }

        return null;
    }

    private static RomJsonEntry? TryMatchByFileName(RomJsonDatabase db, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string normFile = NormalizeName(fileName);
        foreach (var entry in db.Roms)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            string normName = NormalizeName(entry.Name);
            if (normName.Length == 0) continue;
            if (normFile.Contains(normName))
                return entry;
        }

        return null;
    }

    private static bool TryParseHexInt(string? text, out int value)
    {
        value = 0;
        string s = text?.Trim() ?? "";
        if (s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            s = s.Substring(2);

        return int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string NormalizeName(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static bool TryGetCheatOffset(RomJsonEntry entry, out int offset)
    {
        return TryParseHexInt(entry.CheatOffset, out offset);
    }

    public static bool IsLikelyGameShark(RomJsonEntry entry)
    {
        if (entry == null) return false;

        var name = entry.Name ?? "";
        var version = entry.VersionString ?? "";

        return name.Contains("gameshark", System.StringComparison.OrdinalIgnoreCase)
            || version.Contains("gameshark", System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLikelyEqualizer(RomJsonEntry entry)
    {
        if (entry == null) return false;

        var name = entry.Name ?? "";
        var version = entry.VersionString ?? "";

        return name.Contains("equalizer", System.StringComparison.OrdinalIgnoreCase)
            || version.Contains("equalizer", System.StringComparison.OrdinalIgnoreCase);
    }
}
