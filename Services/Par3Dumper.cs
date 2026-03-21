using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XplorerCheatEditorWinForms.Services
{
    public static class Par3Dumper
    {
        // GameShark Pro v3 / PAR3 512KB dump:
        // bank select via /poke8 0x1F060030:
        // 0x01 => Part1 (first 256KB), 0x03 => Part2 (second 256KB)
        public static async Task<NopsResult> DumpPar3_512KBAsync(string nopsExe, string comPort, string outFile, IWin32Window owner, Action<string>? onLine = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nopsExe) || !File.Exists(nopsExe))
                return NopsResult.Fail($"NOPS.exe not found: {nopsExe}");

            string tempDir = Path.GetTempPath();
            string part1 = Path.Combine(tempDir, $"par3_part1_{Guid.NewGuid():N}.rom");
            string part2 = Path.Combine(tempDir, $"par3_part2_{Guid.NewGuid():N}.rom");

            try
            {
                // Part1
                var r1 = await RunAsync(nopsExe, $"/poke8 0x1F060030 0x03 {comPort}", onLine, ct);
                if (!r1.Success) return r1;

                var d1 = await RunAsync(nopsExe, $"/dump 0x1F000000 0x40000 \"{part1}\" {comPort}", onLine, ct);
                if (!d1.Success) return d1;

                // Part2
                var r2 = await RunAsync(nopsExe, $"/poke8 0x1F060030 0x01 {comPort}", onLine, ct);
                if (!r2.Success) return r2;

                var d2 = await RunAsync(nopsExe, $"/dump 0x1F000000 0x40000 \"{part2}\" {comPort}", onLine, ct);
                if (!d2.Success) return d2;

                // Merge -> outFile
                using (var outFs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using var p1 = new FileStream(part1, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await p1.CopyToAsync(outFs);

                    using var p2 = new FileStream(part2, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await p2.CopyToAsync(outFs);
                }

                return NopsResult.Ok();
            }
            catch (Exception ex)
            {
                return NopsResult.Fail(ex.Message);
            }
            finally
            {
                TryDelete(part1);
                TryDelete(part2);
            }
        }

        private static async Task<NopsResult> RunAsync(string exe, string args, Action<string>? onLine, CancellationToken ct)
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

            try
            {
                using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                string? lastErr = null;

                p.OutputDataReceived += (_, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lastErr = e.Data; onLine?.Invoke(e.Data); } };

                if (!p.Start())
                    return NopsResult.Fail("Failed to start NOPS.exe");

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                try
                {
                    await p.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!p.HasExited) p.Kill(true); } catch { }
                    return NopsResult.Fail("Cancelled.");
                }

                if (p.ExitCode != 0)
                {
                    var msg = string.IsNullOrWhiteSpace(lastErr) ? $"NOPS failed (exit {p.ExitCode})." : lastErr.Trim();
                    return NopsResult.Fail(msg);
                }

                return NopsResult.Ok();
            }
            catch (Exception ex)
            {
                return NopsResult.Fail(ex.Message);
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

        public readonly struct NopsResult
        {
            public bool Success { get; }
            public string? Error { get; }

            private NopsResult(bool success, string? error)
            {
                Success = success;
                Error = error;
            }

            public static NopsResult Ok() => new NopsResult(true, null);
            public static NopsResult Fail(string error) => new NopsResult(false, error);
        }
    }
}
