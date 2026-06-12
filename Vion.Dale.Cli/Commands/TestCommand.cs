using System.CommandLine;
using System.Linq;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Commands
{
    public static class TestCommand
    {
        public static Command Create()
        {
            // Unmatched tokens are collected (not parse errors) so consumers can pass dotnet test
            // options straight through, e.g. `dale test --filter "kind!=headless-integration"`.
            var command = new Command("test", "Run tests (unrecognized options are forwarded to dotnet test, e.g. --filter)")
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

                                  return await DotnetRunner.RunAsync("test", args);
                              });

            return command;
        }
    }
}