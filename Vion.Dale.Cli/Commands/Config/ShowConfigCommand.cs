using System.CommandLine;
using System.Threading.Tasks;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Config
{
    public static class ShowConfigCommand
    {
        public static Command Create()
        {
            var command = new Command("show", "Show current configuration");

            command.SetAction((parseResult, cancellationToken) =>
                              {
                                  var config = TokenStore.LoadConfig();
                                  var credentials = TokenStore.LoadCredentials();
                                  var loggedIn = credentials != null && !credentials.IsExpired;

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJson(System.Text.Json.JsonSerializer.Serialize(
                                                                                                      new
                                                                                                      {
                                                                                                          environment = config.Environment,
                                                                                                          authBaseUrl = config.AuthBaseUrl,
                                                                                                          apiBaseUrl = config.ApiBaseUrl,
                                                                                                          integratorId = config.IntegratorId,
                                                                                                          integratorName = config.IntegratorName,
                                                                                                          loggedIn,
                                                                                                      },
                                                                                                      Infrastructure.JsonDefaults.Options));
                                  }
                                  else
                                  {
                                      DaleConsole.KeyValue("Environment:", config.Environment);
                                      DaleConsole.KeyValue("Auth URL:   ", string.IsNullOrEmpty(config.AuthBaseUrl) ? "(not set)" : config.AuthBaseUrl);
                                      DaleConsole.KeyValue("API URL:    ", string.IsNullOrEmpty(config.ApiBaseUrl) ? "(not set)" : config.ApiBaseUrl);
                                      DaleConsole.KeyValue("Integrator: ", config.IntegratorName != null ? $"{config.IntegratorName} ({config.IntegratorId})" : "(not set)");
                                      DaleConsole.KeyValue("Logged in:  ", loggedIn ? "yes" : "no");
                                  }

                                  return Task.FromResult(0);
                              });

            return command;
        }
    }
}