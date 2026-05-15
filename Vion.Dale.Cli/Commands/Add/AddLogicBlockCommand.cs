using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Add
{
    public static class AddLogicBlockCommand
    {
        public static Command Create()
        {
            var command = new Command("logicblock", "Add a new LogicBlock class to the project");
            var nameArg = new Argument<string>("name") { Description = "Name of the new LogicBlock class" };
            command.Arguments.Add(nameArg);

            var displayNameOption = new Option<string?>("--name") { Description = "Human-readable name for [LogicBlock(Name = ...)] (defaults to the class name)" };
            var iconOption = new Option<string?>("--icon") { Description = "Icon identifier for [LogicBlock(Icon = ...)] (Remixicon name without the \"ri-\" prefix)" };
            command.Options.Add(displayNameOption);
            command.Options.Add(iconOption);

            command.SetAction(parseResult =>
                              {
                                  var name = parseResult.GetValue(nameArg);
                                  var displayName = parseResult.GetValue(displayNameOption);
                                  var icon = parseResult.GetValue(iconOption);
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  var outputPath = Path.Combine(project.ProjectDirectory, $"{name}.cs");
                                  if (File.Exists(outputPath))
                                  {
                                      DaleConsole.Error($"File '{name}.cs' already exists in {project.ProjectName}.");
                                      return 1;
                                  }

                                  var ns = project.RootNamespace ?? project.ProjectName;
                                  var content = GenerateLogicBlock(name!, ns, displayName, icon);
                                  File.WriteAllText(outputPath, content);

                                  // Try to register in DependencyInjection.cs
                                  var diFile = Path.Combine(project.ProjectDirectory, "DependencyInjection.cs");
                                  if (File.Exists(diFile))
                                  {
                                      RegisterInDi(diFile, name!);
                                  }

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { file = outputPath, logicBlock = name, project = project.ProjectName });
                                  }
                                  else
                                  {
                                      DaleConsole.Success("Added", $"logicblock {name} to {project.ProjectName}");
                                  }

                                  return 0;
                              });

            return command;
        }

        internal static string GenerateLogicBlock(string name, string ns, string? displayName, string? icon)
        {
            var classAttribute = string.Empty;
            var args = new List<string>();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                args.Add($"Name = \"{PresentationSnippet.EscapeCsString(displayName!)}\"");
            }

            if (!string.IsNullOrWhiteSpace(icon))
            {
                args.Add($"Icon = \"{PresentationSnippet.EscapeCsString(icon!)}\"");
            }

            if (args.Count > 0)
            {
                classAttribute = $"    [LogicBlock({string.Join(", ", args)})]\n";
            }

            return $@"using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace {ns}
{{
{classAttribute}    public class {name} : LogicBlockBase
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

            // Find the last AddTransient/AddSingleton/AddScoped call and insert after it
            var lines = File.ReadAllLines(diFilePath);
            var insertIndex = -1;
            var indent = "            ";

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("services.Add"))
                {
                    insertIndex = i + 1;
                    indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                }
            }

            // Fallback: insert after the opening brace of ConfigureServices method
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
    }
}