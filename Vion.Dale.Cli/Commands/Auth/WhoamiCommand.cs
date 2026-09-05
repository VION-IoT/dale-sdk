using System;
using System.Collections.Generic;
using System.CommandLine;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Auth
{
    public static class WhoamiCommand
    {
        public static Command Create()
        {
            var command = new Command("whoami", "Show current authenticated identity");

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var config = TokenStore.LoadConfig();
                                  var credentials = TokenStore.LoadCredentials();

                                  if (credentials == null || credentials.IsExpired)
                                  {
                                      DaleConsole.Error("Not logged in. Run `dale login`.");
                                      return 1;
                                  }

                                  // Call /me for identity info
                                  var apiBaseUrl = TokenStore.ResolveApiBaseUrl(config.Environment);
                                  if (string.IsNullOrEmpty(apiBaseUrl))
                                  {
                                      apiBaseUrl = config.ApiBaseUrl;
                                  }

                                  string? email = null;
                                  var integrators = new List<string>();
                                  if (!string.IsNullOrEmpty(apiBaseUrl))
                                  {
                                      try
                                      {
                                          MeResponse? me = null;
                                          await DaleConsole.WithSpinner("Fetching user info",
                                                                        async () => { me = await MeClient.GetMeAsync(apiBaseUrl, credentials.AccessToken, cancellationToken); });

                                          email = me!.User.Email;
                                          integrators = me.IntegratorMemberships.ConvertAll(i => $"{i.IntegratorName} ({i.IntegratorSlug})");
                                      }
                                      catch (DaleAuthException)
                                      {
                                          // The identity is what /me knows; the token's own lifetime below is
                                          // still worth reporting when it cannot be reached.
                                      }
                                  }

                                  // The guard above already refused anything inside the expiry buffer, so what
                                  // is left to report is how long the token has.
                                  var remaining = credentials.ExpiresAt - DateTime.UtcNow;

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new
                                                                  {
                                                                      email,
                                                                      integrators,
                                                                      environment = config.Environment,
                                                                      expiresAt = credentials.ExpiresAt,
                                                                  });
                                      return 0;
                                  }

                                  DaleConsole.KeyValue("Email:", email ?? "(could not fetch)");
                                  if (integrators.Count > 0)
                                  {
                                      DaleConsole.KeyValue("Integrators:", string.Join(", ", integrators));
                                  }

                                  DaleConsole.KeyValue("Token:", $"valid (expires in {(int)remaining.TotalHours}h {remaining.Minutes}m)");
                                  return 0;
                              });

            return command;
        }
    }
}