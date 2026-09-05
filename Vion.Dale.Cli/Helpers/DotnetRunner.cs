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
        ///     Run a dotnet command, inheriting stdio in table mode and relaying the child's standard output
        ///     to standard error in JSON mode (see <see cref="ComposeStartInfo" />).
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
            var psi = ComposeStartInfo(command, extraArgs, workingDirectory);

            DaleConsole.Verbose($"Running: dotnet {string.Join(" ", psi.ArgumentList)}");

            using var process = Process.Start(psi);
            if (process == null)
            {
                return 1;
            }

            // Drained concurrently with the wait: a child that fills the pipe buffer would otherwise block
            // forever on a write nobody is reading.
            var relay = psi.RedirectStandardOutput ? RelayToStandardErrorAsync(process.StandardOutput) : Task.CompletedTask;

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

                // The kill closes the child's pipe, so this completes; without it the last lines the child
                // wrote are dropped on the `dale dev` cancellation path rather than reaching standard error.
                await relay;
                throw;
            }

            await relay;
            return process.ExitCode;
        }

        /// <summary>
        ///     Run a dotnet command and capture both its streams, returning the exit code and its standard
        ///     output. Nothing the child writes reaches the console, so a caller that shows a progress
        ///     display is not interleaved with it — and a caller that fails must print what it captured,
        ///     or the diagnosis is lost.
        /// </summary>
        public static async Task<(int ExitCode, string Output)> RunCaptureAsync(string command, IEnumerable<string>? extraArgs = null, string? workingDirectory = null)
        {
            var psi = ComposeStartInfo(command, extraArgs, workingDirectory);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

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

        /// <summary>
        ///     The argument list handed to <c>dotnet</c>: the verb, then the caller's arguments in order.
        ///     Extracted so the composition can be proven without spawning a process — every argument is
        ///     passed as its own list entry (never a joined string), which is what lets a path with spaces
        ///     and a <c>--filter</c> expression survive.
        /// </summary>
        internal static List<string> ComposeArguments(string command, IEnumerable<string>? extraArgs)
        {
            var args = new List<string> { command };
            if (extraArgs != null)
            {
                args.AddRange(extraArgs);
            }

            return args;
        }

        /// <summary>
        ///     How <c>dotnet</c> is started: the verb and arguments, the working directory, and — in JSON
        ///     mode — a redirected standard output. In JSON mode the tool's own standard output carries the
        ///     result document and nothing else, so a child's output is captured and relayed to standard
        ///     error rather than inherited; every one of this class's callers is a child process writing to
        ///     the same console (`dotnet build`, `test`, `run`, `new`, `pack`, `publish`), and MSBuild's
        ///     restore banner in front of a JSON document is what makes that document unparseable.
        ///     Standard error is never redirected: it is already the stream a diagnostic belongs on.
        /// </summary>
        internal static ProcessStartInfo ComposeStartInfo(string command, IEnumerable<string>? extraArgs, string? workingDirectory)
        {
            var psi = new ProcessStartInfo("dotnet")
                      {
                          WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                          UseShellExecute = false,
                          RedirectStandardOutput = DaleConsole.JsonMode,
                      };

            foreach (var arg in ComposeArguments(command, extraArgs))
            {
                psi.ArgumentList.Add(arg);
            }

            return psi;
        }

        /// <summary>
        ///     Relays a captured child's standard output to this process's standard error, line by line, so
        ///     JSON mode's single stream carries the tool's document and nothing else.
        /// </summary>
        internal static async Task RelayToStandardErrorAsync(StreamReader standardOutput)
        {
            while (await standardOutput.ReadLineAsync() is { } line)
            {
                await Console.Error.WriteLineAsync(line);
            }
        }
    }
}