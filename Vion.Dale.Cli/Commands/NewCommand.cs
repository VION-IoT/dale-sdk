using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;
using Spectre.Console;

namespace Vion.Dale.Cli.Commands
{
    public static class NewCommand
    {
        private const string TemplateName = "vion-iot-library";

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
                                                                                                                       "Invalid name. Use letters, digits, dots, hyphens, or underscores (no spaces)."));
                                      }
                                      else
                                      {
                                          DaleConsole.Error("Project name is required. Pass it as an argument or remove --no-interactive.");
                                          return 1;
                                      }
                                  }

                                  if (!IsValidProjectName(name))
                                  {
                                      DaleConsole.Error($"Invalid project name '{name}'. Use letters, digits, dots, hyphens, or underscores (no spaces).");
                                      return 1;
                                  }

                                  var packageId = name;
                                  var author = "MyCompany";
                                  var firstLogicBlock = "HelloWorld";
                                  var includeExamples = true;

                                  if (interactive)
                                  {
                                      packageId = AnsiConsole.Prompt(new TextPrompt<string>($"  Package ID [[[grey]{Markup.Escape(name)}[/]]]:").AllowEmpty()).DefaultIfEmpty(name);
                                      author = AnsiConsole.Prompt(new TextPrompt<string>("  Author [[[grey]MyCompany[/]]]:").AllowEmpty()).DefaultIfEmpty("MyCompany");
                                      firstLogicBlock = AnsiConsole.Prompt(new TextPrompt<string>("  First LogicBlock name [[[grey]HelloWorld[/]]]:").AllowEmpty())
                                                                   .DefaultIfEmpty("HelloWorld");
                                      includeExamples = AnsiConsole.Confirm("  Include example LogicBlocks? (HelloWorld, SmartLedController)");
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
                                  if (bundledTemplatePath != null)
                                  {
                                      await DaleConsole.WithSpinner("Installing template",
                                                                    async () =>
                                                                    {
                                                                        await DotnetRunner.RunCaptureAsync("new", new[] { "install", bundledTemplatePath, "--force" });
                                                                    });
                                  }
                                  else
                                  {
                                      // Fallback: install from NuGet feed (for standalone template usage)
                                      await DaleConsole.WithSpinner("Checking template",
                                                                    async () => { await DotnetRunner.RunCaptureAsync("new", new[] { "install", "Vion.Library.Template" }); });
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

                                  // Remove examples if not wanted (before generating custom block)
                                  var libDir = Path.Combine(targetDir, name);
                                  if (!includeExamples)
                                  {
                                      RemoveExamples(libDir, targetDir, name);
                                  }

                                  // Generate the first LogicBlock if it doesn't already exist
                                  if (!string.IsNullOrWhiteSpace(firstLogicBlock))
                                  {
                                      var blockFile = Path.Combine(libDir, $"{firstLogicBlock}.cs");
                                      if (!File.Exists(blockFile))
                                      {
                                          var ns = name;
                                          var content = GenerateLogicBlock(firstLogicBlock, ns);
                                          File.WriteAllText(blockFile, content);
                                      }

                                      // Ensure DI registration exists
                                      var diFile = Path.Combine(libDir, "DependencyInjection.cs");
                                      if (File.Exists(diFile))
                                      {
                                          RegisterInDi(diFile, firstLogicBlock);
                                      }

                                      // Ensure DevHost Program.cs includes the first LogicBlock
                                      var devHostProgram = Path.Combine(targetDir, $"{name}.DevHost", "Program.cs");
                                      if (File.Exists(devHostProgram))
                                      {
                                          RegisterInDevHost(devHostProgram, firstLogicBlock);
                                      }
                                  }

                                  // --- Restore ---

                                  await DaleConsole.WithSpinner("Restoring dependencies",
                                                                async () => { await DotnetRunner.RunCaptureAsync("restore", workingDirectory: targetDir); });

                                  // --- Output ---

                                  if (DaleConsole.JsonMode)
                                  {
                                      var logicBlocks = includeExamples ? new[] { firstLogicBlock, "HelloWorld", "SmartLedController" }.Distinct().ToArray() :
                                                            new[] { firstLogicBlock };

                                      DaleConsole.WriteJsonResult(new
                                                                  {
                                                                      project = name,
                                                                      directory = targetDir,
                                                                      packageId,
                                                                      author,
                                                                      logicBlocks,
                                                                  });
                                      return 0;
                                  }

                                  DaleConsole.Blank();
                                  DaleConsole.Success("Created", name);
                                  DaleConsole.Blank();
                                  DaleConsole.Info($"  {name}/{name}.csproj              (logic block library)");
                                  DaleConsole.Info($"  {name}/{name}.DevHost.csproj       (local dev host with web UI)");
                                  DaleConsole.Info($"  {name}/{name}.Test.csproj          (tests)");
                                  DaleConsole.Blank();
                                  DaleConsole.Info("Next steps:");
                                  DaleConsole.Info($"  cd {name}");
                                  DaleConsole.Info("  dale build");
                                  DaleConsole.Info("  dale test");
                                  DaleConsole.Info("  dale dev                                (web UI at localhost:5000)");

                                  return 0;
                              });

            return command;
        }

        /// <summary>
        ///     Find the bundled template shipped with the CLI tool.
        /// </summary>
        private static bool IsValidProjectName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // Allow: letters, digits, dots, hyphens, underscores. No spaces or special chars.
            return System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z][\w.\-]*$");
        }

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

        private static void RemoveExamples(string libDir, string targetDir, string projectName)
        {
            // Remove example source files
            var helloWorld = Path.Combine(libDir, "HelloWorld.cs");
            var smartLed = Path.Combine(libDir, "SmartLedController.cs");
            if (File.Exists(helloWorld))
            {
                File.Delete(helloWorld);
            }

            if (File.Exists(smartLed))
            {
                File.Delete(smartLed);
            }

            // Remove example test files
            var testDir = Path.Combine(targetDir, $"{projectName}.Test");
            var helloWorldTest = Path.Combine(testDir, "HelloWorldShould.cs");
            var smartLedTest = Path.Combine(testDir, "SmartLedControllerShould.cs");
            if (File.Exists(helloWorldTest))
            {
                File.Delete(helloWorldTest);
            }

            if (File.Exists(smartLedTest))
            {
                File.Delete(smartLedTest);
            }

            // Clean DI registrations for removed classes
            var diFile = Path.Combine(libDir, "DependencyInjection.cs");
            if (File.Exists(diFile))
            {
                var lines = File.ReadAllLines(diFile).ToList();
                lines.RemoveAll(l =>
                                {
                                    var trimmed = l.TrimStart();
                                    return trimmed.Contains("AddTransient<HelloWorld>") || trimmed.Contains("AddTransient<SmartLedController>");
                                });
                File.WriteAllLines(diFile, lines);
            }

            // Clean DevHost Program.cs references to removed classes
            var devHostDir = Path.Combine(targetDir, $"{projectName}.DevHost");
            var devHostProgram = Path.Combine(devHostDir, "Program.cs");
            if (File.Exists(devHostProgram))
            {
                var lines = File.ReadAllLines(devHostProgram).ToList();
                lines.RemoveAll(l =>
                                {
                                    var trimmed = l.TrimStart();
                                    return trimmed.Contains("AddLogicBlock<HelloWorld>") || trimmed.Contains("AddLogicBlock<SmartLedController>");
                                });
                File.WriteAllLines(devHostProgram, lines);
            }
        }

        private static string GenerateLogicBlock(string name, string ns)
        {
            return $@"using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace {ns}
{{
    public class {name} : LogicBlockBase
    {{
        private readonly ILogger _logger;

        public {name}(ILogger logger) : base(logger)
        {{
            _logger = logger;
        }}

        protected override void Ready()
        {{
            _logger.LogInformation(""{{Name}} is ready"", nameof({name}));
        }}
    }}
}}
";
        }

        private static void RegisterInDi(string diFilePath, string className)
        {
            var content = File.ReadAllText(diFilePath);
            var registration = $"services.AddTransient<{className}>();";

            if (content.Contains(registration))
            {
                return;
            }

            var lines = File.ReadAllLines(diFilePath);
            var insertIndex = -1;
            var indent = "            ";

            // Try to insert after the last services.Add line
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("services.Add"))
                {
                    insertIndex = i + 1;
                    indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                }
            }

            // Fallback: insert before the first closing brace after "ConfigureServices"
            if (insertIndex < 0)
            {
                var inMethod = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("void ConfigureServices"))
                    {
                        inMethod = true;
                    }

                    if (inMethod && lines[i].TrimStart().StartsWith("{"))
                    {
                        // Insert after the opening brace of the method body
                        insertIndex = i + 1;
                        indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length) + "    ";
                        break;
                    }
                }
            }

            if (insertIndex >= 0)
            {
                var newLines = new string[lines.Length + 1];
                Array.Copy(lines, 0, newLines, 0, insertIndex);
                newLines[insertIndex] = indent + registration;
                Array.Copy(lines, insertIndex, newLines, insertIndex + 1, lines.Length - insertIndex);
                File.WriteAllLines(diFilePath, newLines);
            }
        }

        private static void RegisterInDevHost(string programCsPath, string className)
        {
            var content = File.ReadAllText(programCsPath);
            var registration = $"AddLogicBlock<{className}>()";

            if (content.Contains(registration))
            {
                return;
            }

            var lines = File.ReadAllLines(programCsPath).ToList();

            // Find the .Build() line in the DevConfigurationBuilder chain and insert before it
            for (var i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.Contains(".Build()"))
                {
                    var indent = lines[i].Substring(0, lines[i].Length - trimmed.Length);
                    lines.Insert(i, $"{indent}.AddLogicBlock<{className}>()");
                    File.WriteAllLines(programCsPath, lines);
                    return;
                }
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