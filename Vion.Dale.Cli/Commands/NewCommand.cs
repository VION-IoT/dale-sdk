using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Spectre.Console;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class NewCommand
    {
        private const string TemplateName = "vion-dale-library";

        /// <summary>
        ///     Why a project name was refused — the whole rule <see cref="IsValidProjectName" /> enforces,
        ///     first character included, so a reader does not retry the same shape.
        /// </summary>
        private const string InvalidNameReason = "Start with a letter, then letters, digits, dots, hyphens or underscores (no spaces).";

        public static Command Create()
        {
            var command = new Command("new", "Create a new LogicBlock library project");
            var nameArg = new Argument<string?>("name") { Description = "Name of the new project", Arity = ArgumentArity.ZeroOrOne };
            command.Arguments.Add(nameArg);

            var noInteractiveOption = new Option<bool>("--no-interactive") { Description = "Skip interactive prompts (use defaults)" };
            command.Options.Add(noInteractiveOption);

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var name = parseResult.GetValue(nameArg);
                                  var noInteractive = parseResult.GetValue(noInteractiveOption);
                                  var interactive = !noInteractive && !DaleConsole.JsonMode;

                                  // --- Gather inputs ---

                                  if (interactive)
                                  {
                                      DaleConsole.Blank();
                                      DaleConsole.Header("Dale — Create a new LogicBlock library");
                                      DaleConsole.Blank();
                                  }

                                  if (string.IsNullOrWhiteSpace(name))
                                  {
                                      if (interactive)
                                      {
                                          name = AnsiConsole.Prompt(new TextPrompt<string>("  Project name:").Validate(n => IsValidProjectName(n),
                                                                                                                       InvalidNameReason));
                                      }
                                      else
                                      {
                                          DaleConsole.Error(noInteractive
                                                                ? "Project name is required. Pass it as an argument or remove --no-interactive."
                                                                : "Project name is required. Pass it as an argument — `--output json` cannot prompt for it.");
                                          return 1;
                                      }
                                  }

                                  if (!IsValidProjectName(name))
                                  {
                                      DaleConsole.Error($"Invalid project name '{name}'. {InvalidNameReason}");
                                      return 1;
                                  }

                                  // Package ID always equals the project name (and therefore the assembly name).
                                  // The Dale runtime identifies plugins by PackageId and loads them from files named
                                  // `{AssemblyName}.dll`, so the two must match — there's no useful divergence to prompt for.
                                  var packageId = name;
                                  var author = "MyCompany";

                                  if (interactive)
                                  {
                                      author = AnsiConsole.Prompt(new TextPrompt<string>("  Author [[[grey]MyCompany[/]]]:").AllowEmpty()).DefaultIfEmpty("MyCompany");
                                      DaleConsole.Blank();
                                  }

                                  // --- Validate ---

                                  var targetDir = Path.Combine(Directory.GetCurrentDirectory(), name);
                                  if (Directory.Exists(targetDir))
                                  {
                                      DaleConsole.Error($"Directory '{name}' already exists.");
                                      return 1;
                                  }

                                  // --- Install bundled template ---

                                  var bundledTemplatePath = FindBundledTemplate();
                                  if (bundledTemplatePath == null)
                                  {
                                      DaleConsole
                                          .Error("Bundled template not found — the CLI installation appears to be broken. Reinstall with `dotnet tool update -g Vion.Dale.Cli`.");
                                      return 1;
                                  }

                                  var installResult = default((int ExitCode, string Output));
                                  await DaleConsole.WithSpinner("Installing template",
                                                                async () =>
                                                                {
                                                                    installResult = await DotnetRunner.RunCaptureAsync("new", new[] { "install", bundledTemplatePath, "--force" });
                                                                });
                                  if (installResult.ExitCode != 0)
                                  {
                                      DaleConsole.Error(DescribeChildFailure("Installing the bundled template failed", installResult.Output ?? string.Empty));
                                      return 1;
                                  }

                                  // --- Scaffold ---

                                  DaleConsole.Info($"Creating {name}...");
                                  var scaffoldResult = await DotnetRunner.RunAsync("new", new[] { TemplateName, "-n", name });
                                  if (scaffoldResult != 0)
                                  {
                                      DaleConsole.Error("Failed to create project from template.");
                                      return 1;
                                  }

                                  // --- Post-scaffold customization ---

                                  var libCsproj = Path.Combine(targetDir, name, $"{name}.csproj");
                                  if (File.Exists(libCsproj))
                                  {
                                      UpdateCsprojMetadata(libCsproj, packageId, author);
                                  }

                                  // --- Restore ---

                                  var restoreResult = default((int ExitCode, string Output));
                                  await DaleConsole.WithSpinner("Restoring dependencies",
                                                                async () => { restoreResult = await DotnetRunner.RunCaptureAsync("restore", workingDirectory: targetDir); });
                                  if (restoreResult.ExitCode != 0)
                                  {
                                      DaleConsole.Warning(DescribeChildFailure("Restoring the new project's dependencies failed", restoreResult.Output ?? string.Empty));
                                  }

                                  // --- Output ---

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new
                                                                  {
                                                                      project = name,
                                                                      directory = targetDir,
                                                                      packageId,
                                                                      author,
                                                                      logicBlocks = ScaffoldedLogicBlocks(targetDir),
                                                                  });
                                      return 0;
                                  }

                                  DaleConsole.Blank();
                                  DaleConsole.Success("Created", name);
                                  ReportSdkVersion(libCsproj);
                                  DaleConsole.Blank();
                                  DaleConsole.Info($"  {name}/{name}.csproj              (logic block library — the Thermostat example)");
                                  DaleConsole.Info($"  {name}/{name}.DevHost.csproj       (local dev host with web UI)");
                                  DaleConsole.Info($"  {name}/{name}.Test.csproj          (tests)");
                                  DaleConsole.Info($"  {name}/scenarios/                  (scenario files — `docs/specs/scenarios.md`)");
                                  DaleConsole.Blank();
                                  DaleConsole.Info("Next steps:");
                                  DaleConsole.Info($"  cd {name}");
                                  DaleConsole.Info("  dale build");
                                  DaleConsole.Info("  dale dev                                 web UI at localhost:5000 — open the Thermostat block:");
                                  DaleConsole.Info("                                             • drag the TargetTemperature slider and watch CurrentTemperature track it");
                                  DaleConsole.Info("                                             • the State pill changes colour (Idle / Heating / Cooling)");
                                  DaleConsole.Info("  dale scenario run thermostat             drive the bundled scenario (while `dale dev` runs)");
                                  DaleConsole.Info("  dale test                                the unit test (Vion.Dale.Sdk.TestKit)");
                                  DaleConsole.Info("  dale list                                introspect your blocks (properties, metrics, contracts)");
                                  DaleConsole.Info("  dale add logicblock <Name>               scaffold another block");

                                  return 0;
                              });

            return command;
        }

        /// <summary>
        ///     The logic blocks the scaffold actually wrote, read from the tree rather than assumed — the
        ///     JSON result is a machine-readable claim about a directory this command has just created.
        /// </summary>
        private static string[] ScaffoldedLogicBlocks(string targetDir)
        {
            return Directory.Exists(targetDir)
                       ? ProjectDiscovery.FindLogicBlocks(targetDir).Select(block => block.ClassName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                       : Array.Empty<string>();
        }

        /// <summary>
        ///     Says which SDK version the scaffold references whenever it differs from this tool's own. The
        ///     pack-time rewrite aligns the two, but it is skipped for a <c>0.0.0</c> version (an untagged CI
        ///     or local build), and a consumer who installed such a build otherwise has no way to notice.
        /// </summary>
        private static void ReportSdkVersion(string libCsprojPath)
        {
            if (!File.Exists(libCsprojPath))
            {
                return;
            }

            var scaffolded = ProjectDiscovery.FindProject(libCsprojPath)?.SdkVersion;
            if (scaffolded == null || scaffolded == Program.Version())
            {
                return;
            }

            DaleConsole.Info($"  References Vion.Dale.Sdk {scaffolded} (this tool is {Program.Version()}).");
        }

        /// <summary>
        ///     A child process's failure with the lines it named, so the reader is not sent to reinstall the
        ///     tool over a feed timeout.
        /// </summary>
        private static string DescribeChildFailure(string what, string output)
        {
            var detail = output.Split('\n').Select(line => line.Trim('\r', ' ')).Where(line => line.Length > 0).TakeLast(5).ToList();
            return detail.Count > 0 ? what + ":" + Environment.NewLine + string.Join(Environment.NewLine, detail) : what + ".";
        }

        private static bool IsValidProjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // Allow: letters, digits, dots, hyphens, underscores. No spaces or special chars.
            return Regex.IsMatch(name, @"^[a-zA-Z][\w.\-]*$");
        }

        /// <summary>
        ///     Find the bundled template shipped with the CLI tool.
        /// </summary>
        private static string? FindBundledTemplate()
        {
            var baseDir = AppContext.BaseDirectory;
            var templateDir = Path.Combine(baseDir, "Templates", "vion-iot-library");
            var templateJson = Path.Combine(templateDir, ".template.config", "template.json");

            if (File.Exists(templateJson))
            {
                return templateDir;
            }

            return null;
        }

        private static void UpdateCsprojMetadata(string csprojPath, string packageId, string author)
        {
            try
            {
                var doc = XDocument.Load(csprojPath);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                var props = doc.Descendants(ns + "PropertyGroup").FirstOrDefault();
                if (props == null)
                {
                    return;
                }

                var packageIdEl = props.Element(ns + "PackageId");
                if (packageIdEl != null)
                {
                    packageIdEl.Value = packageId;
                }

                var authorsEl = props.Element(ns + "Authors");
                if (authorsEl != null)
                {
                    authorsEl.Value = author;
                }

                doc.Save(csprojPath);
            }
            catch
            {
                // Non-critical — metadata can be updated manually
            }
        }
    }

    internal static class StringExtensions
    {
        public static string DefaultIfEmpty(this string value, string defaultValue)
        {
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }
    }
}