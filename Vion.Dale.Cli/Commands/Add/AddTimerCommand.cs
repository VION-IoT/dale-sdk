using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Add
{
    public static class AddTimerCommand
    {
        public static Command Create()
        {
            var command = new Command("timer", "Add a [Timer] method to a LogicBlock");

            var nameArg = new Argument<string>("name") { Description = "Timer method name" };
            command.Arguments.Add(nameArg);

            var intervalOption = new Option<double>("--interval", "-i") { Description = "Timer interval in seconds", Required = true };
            var toOption = new Option<string?>("--to") { Description = "Target LogicBlock class name (auto-detected if only one exists)" };
            command.Options.Add(intervalOption);
            command.Options.Add(toOption);

            command.SetAction(parseResult =>
                              {
                                  var name = parseResult.GetValue(nameArg);
                                  var interval = parseResult.GetValue(intervalOption);
                                  var to = parseResult.GetValue(toOption);
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  if (interval <= 0)
                                  {
                                      DaleConsole.Error("Timer interval must be greater than zero.");
                                      return 1;
                                  }

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

                                  // Check for existing method with same name
                                  var sourceContent = File.ReadAllText(target.FilePath);
                                  if (Regex.IsMatch(sourceContent, $@"\bvoid\s+{Regex.Escape(name!)}\s*\("))
                                  {
                                      DaleConsole.Error($"Method '{name}' already exists in {target.ClassName}.");
                                      return 1;
                                  }

                                  var snippet = BuildTimerSnippet(name!, interval);

                                  if (!SourceInserter.InsertIntoClass(target.FilePath, target.ClassName, snippet))
                                  {
                                      DaleConsole.Error($"Failed to insert timer into {target.ClassName}.");
                                      return 1;
                                  }

                                  SourceInserter.EnsureUsing(target.FilePath, "Vion.Dale.Sdk.Core");

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { file = target.FilePath, method = name, interval, logicBlock = target.ClassName });
                                  }
                                  else
                                  {
                                      var intervalStr = interval.ToString(CultureInfo.InvariantCulture);
                                      DaleConsole.Success("Added", $"[Timer({intervalStr})] {name} to {target.ClassName}");
                                  }

                                  return 0;
                              });

            return command;
        }

        internal static string BuildTimerSnippet(string name, double interval)
        {
            var intervalStr = interval.ToString(CultureInfo.InvariantCulture);
            return $"[Timer({intervalStr})]\nprivate void {name}()\n{{\n    // TODO: implement timer logic\n}}";
        }
    }
}
