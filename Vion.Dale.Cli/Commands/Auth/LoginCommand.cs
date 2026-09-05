using System;
using System.CommandLine;
using System.Linq;
using Spectre.Console;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Auth
{
    public static class LoginCommand
    {
        public static Command Create()
        {
            var command = new Command("login", "Authenticate with Vion Cloud");
            var environmentOption = new Option<string>("--environment", "-e")
                                    {
                                        Description = "Target environment (production, test)",
                                        DefaultValueFactory = _ => TokenStore.LoadConfig().Environment ?? "production",
                                    };
            command.Options.Add(environmentOption);

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var environment = parseResult.GetValue(environmentOption)!;

                                  // Resolve URLs for the environment
                                  var authBaseUrl = TokenStore.ResolveAuthBaseUrl(environment);
                                  var apiBaseUrl = TokenStore.ResolveApiBaseUrl(environment);

                                  if (authBaseUrl == null || apiBaseUrl == null)
                                  {
                                      // Custom environment — check if already configured
                                      var existing = TokenStore.LoadConfig();
                                      if (existing.Environment == environment && !string.IsNullOrEmpty(existing.AuthBaseUrl) && !string.IsNullOrEmpty(existing.ApiBaseUrl))
                                      {
                                          authBaseUrl = existing.AuthBaseUrl;
                                          apiBaseUrl = existing.ApiBaseUrl;
                                      }
                                      else
                                      {
                                          DaleConsole
                                              .Error($"Unknown environment: {environment}. Use production, test, or configure a custom environment with `dale config set-environment`.");
                                          return 1;
                                      }
                                  }

                                  // 1. Authenticate via browser
                                  StoredCredentials? credentials = null;
                                  try
                                  {
                                      await DaleConsole.WithSpinner("Waiting for browser authentication",
                                                                    async () => { credentials = await AuthService.AcquireInteractiveAsync(authBaseUrl); });
                                  }
                                  catch (DaleAuthException ex)
                                  {
                                      DaleConsole.Error(ex.Message);
                                      return 1;
                                  }
                                  catch (Exception ex)
                                  {
                                      DaleConsole.Error($"Authentication failed: {ex.Message}");
                                      return 1;
                                  }

                                  if (credentials == null)
                                  {
                                      DaleConsole.Error("Authentication failed.");
                                      return 1;
                                  }

                                  credentials.Environment = environment;
                                  TokenStore.SaveCredentials(credentials);

                                  // 2. Fetch user info from /me
                                  MeResponse? me = null;
                                  try
                                  {
                                      await DaleConsole.WithSpinner("Fetching user info",
                                                                    async () => { me = await MeClient.GetMeAsync(apiBaseUrl, credentials.AccessToken, cancellationToken); });
                                  }
                                  catch (DaleAuthException ex)
                                  {
                                      DaleConsole.Error(ex.Message);

                                      // Auth succeeded but /me failed. Save the URLs — they are what the next
                                      // command needs — and keep whatever integrator was already stored: this
                                      // login replaced no selection, so it must not discard one. Exit non-zero
                                      // so a script that branches on the code does not go on to an upload
                                      // whose integrator cannot resolve.
                                      var partialConfig = TokenStore.LoadConfig();
                                      partialConfig.Environment = environment;
                                      partialConfig.AuthBaseUrl = authBaseUrl;
                                      partialConfig.ApiBaseUrl = apiBaseUrl;
                                      TokenStore.SaveConfig(partialConfig);

                                      DaleConsole.Warning($"Logged in (environment: {environment}), but the integrator could not be fetched.");
                                      DaleConsole.Info("Use `dale config set-integrator` to select one.");
                                      return 1;
                                  }

                                  var email = me!.User.Email ?? "unknown";
                                  DaleConsole.Success("Logged in", $"as {email} (environment: {environment})");

                                  // 3. Select integrator
                                  var integrators = me.IntegratorMemberships;
                                  Guid? selectedIntegratorId = null;
                                  string? selectedIntegratorName = null;

                                  if (integrators.Count == 0)
                                  {
                                      DaleConsole.Info("No integrator memberships found.");
                                  }
                                  else if (integrators.Count == 1)
                                  {
                                      // Auto-select if only one
                                      selectedIntegratorId = integrators[0].IntegratorId;
                                      selectedIntegratorName = integrators[0].IntegratorName;
                                      DaleConsole.Success("Active integrator", $"{selectedIntegratorName} ({integrators[0].IntegratorSlug})");
                                  }
                                  else if (DaleConsole.JsonMode)
                                  {
                                      // A prompt in JSON mode has no one to answer it — the mode exists so an
                                      // agent can drive the tool.
                                      DaleConsole.Error("Several integrator memberships found; select one with `dale config set-integrator --integrator-id <id>`:\n" +
                                                        string.Join("\n", integrators.Select(i => $"  {i.IntegratorName} ({i.IntegratorSlug}): {i.IntegratorId}")));
                                      return 1;
                                  }
                                  else
                                  {
                                      // Prompt user to select
                                      DaleConsole.Blank();
                                      var choices = integrators.Select(i => $"{i.IntegratorName} ({i.IntegratorSlug})").ToList();
                                      var selected = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("  Select integrator:").AddChoices(choices));

                                      var selectedIndex = choices.IndexOf(selected);
                                      selectedIntegratorId = integrators[selectedIndex].IntegratorId;
                                      selectedIntegratorName = integrators[selectedIndex].IntegratorName;
                                      DaleConsole.Success("Active integrator", $"{selectedIntegratorName}");
                                  }

                                  // 4. Save config — merged, so an integrator this login did not replace
                                  // survives. Only a selection actually made overwrites the stored one.
                                  var config = TokenStore.LoadConfig();
                                  config.Environment = environment;
                                  config.AuthBaseUrl = authBaseUrl;
                                  config.ApiBaseUrl = apiBaseUrl;
                                  if (selectedIntegratorId != null)
                                  {
                                      config.IntegratorId = selectedIntegratorId;
                                      config.IntegratorName = selectedIntegratorName;
                                  }

                                  TokenStore.SaveConfig(config);

                                  return 0;
                              });

            return command;
        }
    }
}