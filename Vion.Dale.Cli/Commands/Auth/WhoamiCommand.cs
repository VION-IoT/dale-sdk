using System;
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

                                  if (!string.IsNullOrEmpty(apiBaseUrl))
                                  {
                                      try
                                      {
                                          MeResponse? me = null;
                                          await DaleConsole.WithSpinner("Fetching user info",
                                                                        async () => { me = await MeClient.GetMeAsync(apiBaseUrl, credentials.AccessToken, cancellationToken); });

                                          DaleConsole.KeyValue("Email:", me!.User.Email ?? "unknown");

                                          if (me.IntegratorMemberships.Count > 0)
                                          {
                                              var names = string.Join(", ", me.IntegratorMemberships.ConvertAll(i => $"{i.IntegratorName} ({i.IntegratorSlug})"));
                                              DaleConsole.KeyValue("Integrators:", names);
                                          }
                                      }
                                      catch
                                      {
                                          DaleConsole.KeyValue("Email:", "(could not fetch — token may be expired)");
                                      }
                                  }

                                  var remaining = credentials.ExpiresAt - DateTime.UtcNow;
                                  var tokenStatus = remaining.TotalMinutes > 0 ? $"valid (expires in {(int)remaining.TotalHours}h {remaining.Minutes}m)" : "expired";
                                  DaleConsole.KeyValue("Token:", tokenStatus);

                                  return 0;
                              });

            return command;
        }
    }
}