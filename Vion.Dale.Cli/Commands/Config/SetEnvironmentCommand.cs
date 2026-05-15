using System.CommandLine;
using System.Threading.Tasks;
using Spectre.Console;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Config
{
    public static class SetEnvironmentCommand
    {
        public static Command Create()
        {
            var command = new Command("set-environment", "Configure target environment");

            var nameArgument = new Argument<string>("name") { Description = "Environment name (production, test, or custom name)" };
            var authUrlOption = new Option<string?>("--auth-url") { Description = "Custom auth base URL (required for custom environments)" };
            var apiUrlOption = new Option<string?>("--api-url") { Description = "Custom API base URL (required for custom environments)" };
            var forceOption = new Option<bool>("--force", "-f") { Description = "Skip confirmation prompt" };

            command.Arguments.Add(nameArgument);
            command.Options.Add(authUrlOption);
            command.Options.Add(apiUrlOption);
            command.Options.Add(forceOption);

            command.SetAction((parseResult, cancellationToken) =>
                              {
                                  var name = parseResult.GetValue(nameArgument)!;
                                  var authUrl = parseResult.GetValue(authUrlOption);
                                  var apiUrl = parseResult.GetValue(apiUrlOption);

                                  string resolvedAuthUrl;
                                  string resolvedApiUrl;

                                  if (TokenStore.IsKnownEnvironment(name))
                                  {
                                      resolvedAuthUrl = TokenStore.ResolveAuthBaseUrl(name);
                                      resolvedApiUrl = TokenStore.ResolveApiBaseUrl(name);
                                  }
                                  else
                                  {
                                      // Custom environment — require both URLs
                                      if (string.IsNullOrEmpty(authUrl) || string.IsNullOrEmpty(apiUrl))
                                      {
                                          DaleConsole.Error("Custom environments require --auth-url and --api-url.");
                                          DaleConsole
                                              .Info("Example: dale config set-environment myenv --auth-url https://auth.example.com/realms/vion --api-url https://api.example.com");
                                          return Task.FromResult(1);
                                      }

                                      resolvedAuthUrl = authUrl;
                                      resolvedApiUrl = apiUrl;
                                  }

                                  var config = TokenStore.LoadConfig();

                                  var force = parseResult.GetValue(forceOption);
                                  if (!force && config.IntegratorId != null)
                                  {
                                      DaleConsole.Warning($"This will clear the active integrator ({config.IntegratorName ?? config.IntegratorId.ToString()}).");
                                      var confirm = AnsiConsole.Confirm("  Continue?", false);
                                      if (!confirm)
                                      {
                                          DaleConsole.Info("Cancelled.");
                                          return Task.FromResult(0);
                                      }
                                  }

                                  config.Environment = name;
                                  config.AuthBaseUrl = resolvedAuthUrl;
                                  config.ApiBaseUrl = resolvedApiUrl;

                                  // Clear integrator when switching environment — it belongs to the previous context
                                  config.IntegratorId = null;
                                  config.IntegratorName = null;
                                  TokenStore.SaveConfig(config);

                                  DaleConsole.Success("Environment", $"{name}");
                                  DaleConsole.Info($"Auth URL: {resolvedAuthUrl}");
                                  DaleConsole.Info($"API URL:  {resolvedApiUrl}");
                                  DaleConsole.Info("Integrator cleared. Run `dale login` or `dale config set-integrator` to select one.");
                                  return Task.FromResult(0);
                              });

            return command;
        }
    }
}
