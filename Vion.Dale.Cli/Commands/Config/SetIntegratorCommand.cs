using System.CommandLine;
using System.Linq;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Output;
using Spectre.Console;

namespace Vion.Dale.Cli.Commands.Config
{
    public static class SetIntegratorCommand
    {
        public static Command Create()
        {
            var command = new Command("set-integrator", "Select active integrator");

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  // Resolve cloud context (no integrator required — we're selecting one)
                                  CommandContext ctx;
                                  try
                                  {
                                      ctx = await CommandContext.ResolveAsync(requireIntegrator: false);
                                  }
                                  catch (DaleAuthException ex)
                                  {
                                      DaleConsole.Error(ex.Message);
                                      return 1;
                                  }

                                  // Fetch integrators from /me
                                  MeResponse? me = null;
                                  try
                                  {
                                      await DaleConsole.WithSpinner("Fetching integrators",
                                                                    async () => { me = await MeClient.GetMeAsync(ctx.ApiBaseUrl, ctx.AccessToken, cancellationToken); });
                                  }
                                  catch (DaleAuthException ex)
                                  {
                                      DaleConsole.Error(ex.Message);
                                      return 1;
                                  }

                                  var integrators = me!.IntegratorMemberships;
                                  if (integrators.Count == 0)
                                  {
                                      DaleConsole.Error("No integrator memberships found for this user.");
                                      return 1;
                                  }

                                  // Prompt selection
                                  var choices = integrators.Select(i => $"{i.IntegratorName} ({i.IntegratorSlug})").ToList();
                                  var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("  Select integrator:").AddChoices(choices));

                                  var selectedIndex = choices.IndexOf(selected);
                                  var config = ctx.Config;
                                  config.IntegratorId = integrators[selectedIndex].IntegratorId;
                                  config.IntegratorName = integrators[selectedIndex].IntegratorName;
                                  TokenStore.SaveConfig(config);

                                  DaleConsole.Success("Active integrator", $"{config.IntegratorName}");
                                  return 0;
                              });

            return command;
        }
    }
}