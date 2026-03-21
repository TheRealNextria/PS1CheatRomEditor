using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XplorerCheatEditorWinForms.Services
{
    internal static class NopsCartridgeService
    {
        internal sealed class NopsOpResult
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public int ExitCode { get; set; }
            public string? Output { get; set; }
        }

        public static async Task<NopsOpResult> DumpAsync(string nopsExe, string comPort, int sizeBytes, string outFile, IWin32Window owner, Action<string>? onLine = null, CancellationToken ct = default)
        {
            if (!File.Exists(nopsExe))
                return new NopsOpResult { Success = false, Error = $"NOPS.exe not found: {nopsExe}" };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outFile) ?? ".");
            }
            catch { }

            string lenHex = "0x" + sizeBytes.ToString("X");
            string args = $"/dump 0x1F000000 {lenHex} \"{outFile}\" {comPort}";
            return await RunAsync(nopsExe, args, onLine, ct).ConfigureAwait(false);
        }

        
public static async Task<NopsOpResult> DumpGsV2_256KBAsync(string nopsExe, string comPort, string outFile, IWin32Window owner, Action<string>? onLine = null, CancellationToken ct = default)
{
    if (!File.Exists(nopsExe))
        return new NopsOpResult { Success = false, Error = $"NOPS.exe not found: {nopsExe}" };

    string tempDir = Path.GetTempPath();
    string part1 = Path.Combine(tempDir, $"par2_part1_{Guid.NewGuid():N}.rom");
    string part2 = Path.Combine(tempDir, $"par2_part2_{Guid.NewGuid():N}.rom");

    try
    {
        // Part1: 0x1F000000 length 0x20000
        var d1 = await RunAsync(nopsExe, $"/dump 0x1F000000 0x20000 \"{part1}\" {comPort}", onLine, ct).ConfigureAwait(false);
        if (!d1.Success) return d1;

        // Part2: 0x1F040000 length 0x20000
        var d2 = await RunAsync(nopsExe, $"/dump 0x1F040000 0x20000 \"{part2}\" {comPort}", onLine, ct).ConfigureAwait(false);
        if (!d2.Success) return d2;

        // Merge -> outFile
        try { Directory.CreateDirectory(Path.GetDirectoryName(outFile) ?? "."); } catch { }

        using (var outFs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var p1 = new FileStream(part1, FileMode.Open, FileAccess.Read, FileShare.Read);
            await p1.CopyToAsync(outFs, ct).ConfigureAwait(false);

            using var p2 = new FileStream(part2, FileMode.Open, FileAccess.Read, FileShare.Read);
            await p2.CopyToAsync(outFs, ct).ConfigureAwait(false);
        }

        return new NopsOpResult { Success = true, ExitCode = 0 };
    }
    catch (OperationCanceledException)
    {
        return new NopsOpResult { Success = false, ExitCode = -1, Error = "Cancelled." };
    }
    catch (Exception ex)
    {
        return new NopsOpResult { Success = false, Error = ex.Message };
    }
    finally
    {
        TryDelete(part1);
        TryDelete(part2);
    }
}

private static void TryDelete(string path)
{
    try
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            File.Delete(path);
    }
    catch { }
}

public static async Task<NopsOpResult> FlashRomAsync(string nopsExe, string comPort, string romFile, IWin32Window owner, Action<string>? onLine = null, CancellationToken ct = default)
        {
            if (!File.Exists(nopsExe))
                return new NopsOpResult { Success = false, Error = $"NOPS.exe not found: {nopsExe}" };
            if (!File.Exists(romFile))
                return new NopsOpResult { Success = false, Error = $"ROM file not found: {romFile}" };

            string args = $"/rom \"{romFile}\" {comPort}";
            return await RunAsync(nopsExe, args, onLine, ct).ConfigureAwait(false);
        }

        private static async Task<NopsOpResult> RunAsync(string exe, string args, Action<string>? onLine, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            var sb = new StringBuilder();
            var esb = new StringBuilder();

            try
            {
                using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

                p.OutputDataReceived += (_, e) => { if (e.Data != null) { sb.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { esb.AppendLine(e.Data); onLine?.Invoke(e.Data); } };

                if (!p.Start())
                    return new NopsOpResult { Success = false, Error = "Failed to start NOPS." };

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                try
                {
                    await p.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!p.HasExited) p.Kill(true); } catch { }
                    return new NopsOpResult { Success = false, ExitCode = -1, Error = "Cancelled." };
                }

                var output = sb.ToString().Trim();
                var err = esb.ToString().Trim();

                if (p.ExitCode != 0)
                {
                    var msg = string.IsNullOrWhiteSpace(err) ? output : err;
                    if (string.IsNullOrWhiteSpace(msg))
                        msg = $"NOPS failed (exit code {p.ExitCode}).";
                    return new NopsOpResult { Success = false, ExitCode = p.ExitCode, Error = msg, Output = output };
                }

                return new NopsOpResult { Success = true, ExitCode = p.ExitCode, Output = output };
            }
            catch (Exception ex)
            {
                return new NopsOpResult { Success = false, Error = ex.Message };
            }
        }
    }
}