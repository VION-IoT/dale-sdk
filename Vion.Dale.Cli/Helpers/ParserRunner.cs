using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Vion.Dale.Cli.Models;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Helpers
{
    public static class ParserRunner
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
                                                                    {
                                                                        PropertyNameCaseInsensitive = true,
                                                                    };

        /// <summary>
        ///     Publish the project and run Vion.Dale.LogicBlockParser on the published output.
        ///     Publishing is required (not just build) to get all transitive dependencies
        ///     into a single directory for the parser's PluginLoadContext.
        ///     Returns the parsed introspection result, or null on failure.
        /// </summary>
        public static async Task<DalePluginInfo?> RunIntrospectionAsync(DaleProject project)
        {
            CleanStaleTempDirs();

            // Use temp directory for publish output — cleaned up after introspection
            var publishDir = Path.Combine(Path.GetTempPath(), $"dale-introspection-{Environment.ProcessId}");
            Directory.CreateDirectory(publishDir);

            try
            {
                // Step 1: Publish the project to get all dependencies
                DaleConsole.Verbose($"Publishing to {publishDir}...");
                var publishExitCode = await DotnetRunner.RunAsync("publish", new[] { project.CsprojPath, "-o", publishDir }, project.ProjectDirectory);
                if (publishExitCode != 0)
                {
                    return null;
                }

                // Step 2: Locate the published DLL
                var dllPath = Path.Combine(publishDir, project.ProjectName + ".dll");
                if (!File.Exists(dllPath))
                {
                    return null;
                }

                // Step 3: Find the parser
                var parserDll = FindParserDll(project);
                if (parserDll == null)
                {
                    DaleConsole.Error("Vion.Dale.LogicBlockParser not found. Ensure Vion.Dale.Sdk NuGet package is restored.");
                    return null;
                }

                DaleConsole.Verbose($"Using parser: {parserDll}");

                // Step 4: Run parser against published DLL
                DaleConsole.Verbose($"Running parser on {dllPath}...");
                var tempJson = Path.Combine(publishDir, "introspection.json");
                var exitCode = await RunParserDll(parserDll, dllPath, tempJson);
                if (exitCode != 0)
                {
                    return null;
                }

                // Step 5: Read and parse JSON
                var json = await File.ReadAllTextAsync(tempJson);
                return JsonSerializer.Deserialize<DalePluginInfo>(json, JsonOptions);
            }
            finally
            {
                // Clean up temp publish directory
                try
                {
                    if (Directory.Exists(publishDir))
                    {
                        Directory.Delete(publishDir, true);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }

        /// <summary>
        ///     Find the parser DLL using a two-tier strategy:
        ///     1. NuGet global packages cache (for end users with Vion.Dale.Sdk PackageReference)
        ///     2. Local project walk-up (for developers working in the dale repo with ProjectReference)
        /// </summary>
        internal static string? FindParserDll(DaleProject project)
        {
            // Tier 1: NuGet package cache — parser ships inside Vion.Dale.Sdk NuGet package at tools/net10.0/
            if (project.SdkVersion != null)
            {
                var nugetPath = FindParserInNuGetCache(project.SdkVersion);
                if (nugetPath != null)
                {
                    return nugetPath;
                }
            }

            // Tier 2: Local project (dale repo development) — walk up to find built parser
            var localPath = FindParserInLocalRepo(project.ProjectDirectory);
            return localPath;
        }

        private static string? FindParserInNuGetCache(string sdkVersion)
        {
            var globalPackagesDir = GetNuGetGlobalPackagesDir();
            if (globalPackagesDir == null)
            {
                return null;
            }

            var parserDll = Path.Combine(globalPackagesDir,
                                         "vion.dale.sdk",
                                         sdkVersion,
                                         "tools",
                                         "net10.0",
                                         "Vion.Dale.LogicBlockParser.dll");
            return File.Exists(parserDll) ? parserDll : null;
        }

        private static string? FindParserInLocalRepo(string startDirectory)
        {
            // Walk up to find dale repo root containing Vion.Dale.LogicBlockParser/
            var dir = startDirectory;
            while (dir != null)
            {
                // Check for built parser DLL
                var parserDll = Path.Combine(dir,
                                             "Vion.Dale.LogicBlockParser",
                                             "bin",
                                             "Debug",
                                             "net10.0",
                                             "Vion.Dale.LogicBlockParser.dll");
                if (File.Exists(parserDll))
                {
                    return parserDll;
                }

                // Check for parser project (needs building)
                var parserCsproj = Path.Combine(dir, "Vion.Dale.LogicBlockParser", "Vion.Dale.LogicBlockParser.csproj");
                if (File.Exists(parserCsproj))
                {
                    // Build it, then return the DLL
                    var buildResult = DotnetRunner.RunCaptureAsync("build", new[] { parserCsproj }).GetAwaiter().GetResult();
                    if (buildResult.ExitCode == 0)
                    {
                        parserDll = Path.Combine(dir,
                                                 "Vion.Dale.LogicBlockParser",
                                                 "bin",
                                                 "Debug",
                                                 "net10.0",
                                                 "Vion.Dale.LogicBlockParser.dll");
                        if (File.Exists(parserDll))
                        {
                            return parserDll;
                        }
                    }

                    return null;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        private static string? GetNuGetGlobalPackagesDir()
        {
            // Standard location: ~/.nuget/packages/
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var standardPath = Path.Combine(userProfile, ".nuget", "packages");
            if (Directory.Exists(standardPath))
            {
                return standardPath;
            }

            // Fallback: ask dotnet for the path
            try
            {
                var psi = new ProcessStartInfo("dotnet")
                          {
                              UseShellExecute = false,
                              RedirectStandardOutput = true,
                              RedirectStandardError = true,
                          };
                psi.ArgumentList.Add("nuget");
                psi.ArgumentList.Add("locals");
                psi.ArgumentList.Add("global-packages");
                psi.ArgumentList.Add("--list");

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Output format: "global-packages: /path/to/packages/"
                var prefix = "global-packages:";
                var idx = output.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var path = output.Substring(idx + prefix.Length).Trim();
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Best effort
            }

            return null;
        }

        private static async Task<int> RunParserDll(string parserDllPath, string pluginDllPath, string outputJsonPath)
        {
            var psi = new ProcessStartInfo("dotnet")
                      {
                          UseShellExecute = false,
                          RedirectStandardOutput = true,
                          RedirectStandardError = true,
                      };

            psi.ArgumentList.Add(parserDllPath);
            psi.ArgumentList.Add(pluginDllPath);
            psi.ArgumentList.Add(outputJsonPath);

            using var process = Process.Start(psi);
            if (process == null)
            {
                return 1;
            }

            // Drain stdout/stderr concurrently to avoid deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync();

            var stderr = stderrTask.Result;
            var stdout = stdoutTask.Result;

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                DaleConsole.Verbose($"Parser stdout: {stdout}");
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                DaleConsole.Verbose($"Parser stderr: {stderr}");
            }

            return process.ExitCode;
        }

        private static void CleanStaleTempDirs()
        {
            try
            {
                var tempBase = Path.GetTempPath();
                foreach (var dir in Directory.GetDirectories(tempBase, "dale-introspection-*"))
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        if (info.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                        {
                            info.Delete(true);
                        }
                    }
                    catch
                    {
                        // Best effort — skip dirs in use
                    }
                }
            }
            catch
            {
                // Best effort
            }
        }
    }
}