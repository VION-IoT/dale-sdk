using System;
using System.CommandLine;
using System.Linq;
using Spectre.Console;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Config
{
    public static class SetIntegratorCommand
    {
        public static Command Create()
        {
            var command = new Command("set-integrator", "Select active integrator");

            var integratorIdOption = new Option<Guid?>("--integrator-id") { Description = "Select this integrator without prompting" };
            command.Options.Add(integratorIdOption);

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

                                  // One membership needs no question — `dale login` auto-selects it from the
                                  // same answer, and this is the command meant for repair, so it must run
                                  // unattended too.
                                  int selectedIndex;
                                  var named = parseResult.GetValue(integratorIdOption);
                                  if (named != null)
                                  {
                                      selectedIndex = integrators.FindIndex(i => i.IntegratorId == named);
                                      if (selectedIndex < 0)
                                      {
                                          DaleConsole.Error($"'{named}' is not one of this account's integrator memberships.");
                                          return 1;
                                      }
                                  }
                                  else if (integrators.Count == 1)
                                  {
                                      selectedIndex = 0;
                                  }
                                  else if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.Error("Several integrator memberships found; name one with `--integrator-id <id>`:\n" +
                                                        string.Join("\n", integrators.Select(i => $"  {i.IntegratorName} ({i.IntegratorSlug}): {i.IntegratorId}")));
                                      return 1;
                                  }
                                  else
                                  {
                                      var choices = integrators.Select(i => $"{i.IntegratorName} ({i.IntegratorSlug})").ToList();
                                      var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("  Select integrator:").AddChoices(choices));
                                      selectedIndex = choices.IndexOf(selected);
                                  }

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