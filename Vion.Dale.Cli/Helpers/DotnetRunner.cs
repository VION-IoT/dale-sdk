using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Helpers
{
    public static class DotnetRunner
    {
        /// <summary>
        ///     Run a dotnet command with inherited stdio (output streams directly to console).
        ///     Returns the process exit code. When <paramref name="cancellationToken" /> is cancelled, the
        ///     spawned process — and its whole tree (a <c>dotnet run</c> spawns the built app as a child) —
        ///     is killed, and <see cref="OperationCanceledException" /> is thrown so the caller can
        ///     distinguish a cancellation from a normal exit.
        /// </summary>
        public static async Task<int> RunAsync(string command,
                                               IEnumerable<string>? extraArgs = null,
                                               string? workingDirectory = null,
                                               CancellationToken cancellationToken = default)
        {
            var args = new List<string> { command };
            if (extraArgs != null)
            {
                args.AddRange(extraArgs);
            }

            var psi = new ProcessStartInfo("dotnet")
                      {
                          WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                          UseShellExecute = false,
                      };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            DaleConsole.Verbose($"Running: dotnet {string.Join(" ", args)}");

            using var process = Process.Start(psi);
            if (process == null)
            {
                return 1;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // Best-effort kill — the process may have exited between the check and the kill.
                }

                throw;
            }

            return process.ExitCode;
        }

        /// <summary>
        ///     Run a dotnet command and capture stdout. Returns (exitCode, stdout).
        ///     Stderr is still inherited (shown to user).
        /// </summary>
        public static async Task<(int ExitCode, string Output)> RunCaptureAsync(string command, IEnumerable<string>? extraArgs = null, string? workingDirectory = null)
        {
            var args = new List<string> { command };
            if (extraArgs != null)
            {
                args.AddRange(extraArgs);
            }

            var psi = new ProcessStartInfo("dotnet")
                      {
                          WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                          UseShellExecute = false,
                          RedirectStandardOutput = true,
                          RedirectStandardError = true,
                      };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (1, string.Empty);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync();
            return (process.ExitCode, outputTask.Result);
        }
    }
}