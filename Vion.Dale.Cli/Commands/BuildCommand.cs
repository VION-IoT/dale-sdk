using System.CommandLine;
using System.Linq;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Commands
{
    public static class BuildCommand
    {
        public static Command Create()
        {
            // Unmatched tokens are collected (not parse errors) so consumers can pass dotnet build
            // options straight through, e.g. `dale build -c Release`.
            var command = new Command("build", "Build the project (unrecognized options are forwarded to dotnet build, e.g. -c Release)")
                          {
                              TreatUnmatchedTokensAsErrors = false,
                          };

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");
                                  var target = CommandHelpers.RequireBuildTarget(projectPath);
                                  if (target == null)
                                  {
                                      return 1;
                                  }

                                  var args = new[] { target }.Concat(parseResult.UnmatchedTokens).ToList();

                                  return await DotnetRunner.RunAsync("build", args);
                              });

            return command;
        }
    }
}