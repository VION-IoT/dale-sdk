using System.Collections.Generic;
using System.CommandLine;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Add
{
    public static class AddServicePropertyCommand
    {
        public static Command Create()
        {
            var command = new Command("serviceproperty", "Add a [ServiceProperty] to a LogicBlock");

            var nameArg = new Argument<string>("name") { Description = "Property name" };
            command.Arguments.Add(nameArg);

            var typeOption = new Option<string>("--type", "-t") { Description = "C# type of the property (e.g. double, string, bool)", Required = true };
            var toOption = new Option<string?>("--to") { Description = "Target LogicBlock class name (auto-detected if only one exists)" };
            var setterOption = new Option<string>("--setter") { Description = "Setter visibility", DefaultValueFactory = _ => "private" };
            setterOption.AcceptOnlyFromAmong("public", "private");
            var defaultNameOption = new Option<string?>("--default-name") { Description = "DefaultName parameter for [ServiceProperty]" };
            var persistentOption = new Option<bool>("--persistent") { Description = "Add [Persistent] attribute" };

            command.Options.Add(typeOption);
            command.Options.Add(toOption);
            command.Options.Add(setterOption);
            command.Options.Add(defaultNameOption);
            command.Options.Add(persistentOption);

            command.SetAction(parseResult =>
                              {
                                  var name = parseResult.GetValue(nameArg);
                                  var type = parseResult.GetValue(typeOption);
                                  var to = parseResult.GetValue(toOption);
                                  var setter = parseResult.GetValue(setterOption);
                                  var defaultName = parseResult.GetValue(defaultNameOption);
                                  var persistent = parseResult.GetValue(persistentOption);
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  var target = CommandHelpers.RequireTarget(project, to);
                                  if (target == null)
                                  {
                                      return 1;
                                  }

                                  // Check for existing property with same name
                                  var sourceContent = System.IO.File.ReadAllText(target.FilePath);
                                  if (System.Text.RegularExpressions.Regex.IsMatch(sourceContent, $@"\b{System.Text.RegularExpressions.Regex.Escape(name!)}\s*{{"))
                                  {
                                      DaleConsole.Error($"Property '{name}' already exists in {target.ClassName}.");
                                      return 1;
                                  }

                                  // Build the snippet
                                  var snippet = BuildPropertySnippet(name!, type!, setter!, defaultName, persistent);

                                  // Insert into class
                                  if (!SourceInserter.InsertIntoClass(target.FilePath, target.ClassName, snippet))
                                  {
                                      DaleConsole.Error($"Failed to insert property into {target.ClassName}.");
                                      return 1;
                                  }

                                  // Ensure using statements
                                  SourceInserter.EnsureUsing(target.FilePath, "Vion.Dale.Sdk.Core");

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { file = target.FilePath, property = name, type, logicBlock = target.ClassName });
                                  }
                                  else
                                  {
                                      var setterDesc = setter == "public" ? "public" : "private";
                                      DaleConsole.Success("Added", $"[ServiceProperty] {type} {name} ({setterDesc} set) to {target.ClassName}");
                                  }

                                  return 0;
                              });

            return command;
        }

        internal static string BuildPropertySnippet(string name, string type, string setter, string? defaultName, bool persistent)
        {
            var lines = new List<string>();

            // [ServiceProperty] attribute — DefaultName is a constructor parameter, not a named argument
            var displayName = defaultName ?? name;
            lines.Add($"[ServiceProperty(\"{displayName}\")]");

            // [Persistent] attribute if requested
            if (persistent)
            {
                lines.Add("[Persistent]");
            }

            // Property declaration
            var setterSuffix = setter == "public" ? "set;" : "private set;";
            lines.Add($"public {type} {name} {{ get; {setterSuffix} }}");

            return string.Join("\n", lines);
        }
    }
}