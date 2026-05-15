using System.CommandLine;
using System.Linq;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Commands
{
    public static class TestCommand
    {
        public static Command Create()
        {
            var command = new Command("test", "Run tests");

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");
                                  var target = CommandHelpers.RequireBuildTarget(projectPath);
                                  if (target == null)
                                  {
                                      return 1;
                                  }

                                  var args = new[] { target }.Concat(parseResult.UnmatchedTokens).ToList();

                                  return await DotnetRunner.RunAsync("test", args);
                              });

            return command;
        }
    }
}