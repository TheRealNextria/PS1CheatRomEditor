using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XplorerCheatEditorWinForms.Services;

public static class NopsDetectService
{
    private const int PSX_ROM_BASE = unchecked((int)0x1F000000);

    public sealed class DetectResult
    {
        public bool Success { get; init; }
        public string? RomName { get; init; }
        public int RomSizeKB { get; init; }
        public string? MatchedVersionString { get; init; }
        public string? Error { get; init; }
    }

    public static async Task<DetectResult> DetectXplorerAsync(string nopsExePath, string romsJsonPath, string comPort, IWin32Window? owner = null, CancellationToken ct = default)
    {
        if (!File.Exists(nopsExePath))
            return new DetectResult { Success = false, Error = $"NOPS.exe not found at:\n{nopsExePath}" };

        if (!File.Exists(romsJsonPath))
            return new DetectResult { Success = false, Error = $"roms.json not found at:\n{romsJsonPath}" };

        var entries = LoadRoms(romsJsonPath)
            .Where(r => r.Name.Contains("Xploder", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("Xplorer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
            return new DetectResult { Success = false, Error = "No Xplorer/Xploder entries found in roms.json." };

        int minOff = entries.Min(e => e.VersionOffset);
        int maxOff = entries.Max(e => e.VersionOffset);

        int startOff = Math.Max(0, minOff - 0x80);
        int endOff = maxOff + 0x200;
        int len = Math.Max(0x100, endOff - startOff);

        uint startAddr = (uint)(PSX_ROM_BASE + startOff);

        string tmp = Path.Combine(Path.GetTempPath(), $"nops_detect_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.bin");

        try
        {
            var args = $"/dump 0x{startAddr:X8} 0x{len:X} \"{tmp}\" {comPort}";
            var (exit, stdout, stderr) = await RunProcessAsync(nopsExePath, args, ct);

            if (exit != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length == 0)
            {
                var msg = $"NOPS failed.\n\nArgs: {args}\n\nExit: {exit}\n\n{stderr}\n{stdout}".Trim();
                return new DetectResult { Success = false, Error = msg };
            }

            var bytes = await File.ReadAllBytesAsync(tmp, ct);
            var ascii = Encoding.ASCII.GetString(bytes);

            RomEntry? best = null;
            string? bestNeedle = null;

            foreach (var e in entries.OrderByDescending(x => x.VersionString.Length))
            {
                if (string.IsNullOrWhiteSpace(e.VersionString))
                    continue;

                if (ascii.Contains(e.VersionString, StringComparison.Ordinal))
                {
                    best = e;
                    bestNeedle = e.VersionString;
                    break;
                }
            }

            if (best == null)
                return new DetectResult { Success = false, Error = "No version string matched in the dumped window." };

            int sizeKB = best.RomSizeKB > 0 ? best.RomSizeKB : (best.RomSizeBytes / 1024);

            return new DetectResult
            {
                Success = true,
                RomName = best.Name,
                RomSizeKB = sizeKB,
                MatchedVersionString = bestNeedle
            };
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        await p.WaitForExitAsync(ct);
        return (p.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class RomEntry
    {
        public string Name { get; set; } = "";
        public string VersionString { get; set; } = "";
        public int VersionOffset { get; set; }
        public int RomSizeBytes { get; set; }
        public int RomSizeKB { get; set; }
    }

    private static IEnumerable<RomEntry> LoadRoms(string path)
    {
        using var fs = File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);

        if (!doc.RootElement.TryGetProperty("roms", out var roms) || roms.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var r in roms.EnumerateArray())
        {
            string name = r.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            string verStr = r.TryGetProperty("versionString", out var vs) ? (vs.GetString() ?? "") : "";

            int verOff = r.TryGetProperty("versionOffset", out var vo) ? ParseHexOrDec(vo) : 0;
            int sizeBytes = r.TryGetProperty("romSizeBytes", out var rsb) ? ParseHexOrDec(rsb) : 0;
            int sizeKB = r.TryGetProperty("romSizeKB", out var rsk) ? (rsk.ValueKind == JsonValueKind.Number ? rsk.GetInt32() : ParseHexOrDec(rsk) / 1024) : 0;

            yield return new RomEntry
            {
                Name = name,
                VersionString = verStr,
                VersionOffset = verOff,
                RomSizeBytes = sizeBytes,
                RomSizeKB = sizeKB
            };
        }
    }

    private static int ParseHexOrDec(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number)
            return el.GetInt32();

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = (el.GetString() ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(s, 16);

            // Offsets are stored as hex strings (often without 0x)
            return Convert.ToInt32(s, 16);
        }

        return 0;
    }
}
