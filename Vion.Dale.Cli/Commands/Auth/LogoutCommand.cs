using System.CommandLine;
using System.Threading.Tasks;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Auth
{
    public static class LogoutCommand
    {
        public static Command Create()
        {
            var command = new Command("logout", "Clear stored credentials");

            command.SetAction((parseResult, cancellationToken) =>
                              {
                                  // Clearing nothing is still success, but saying "cleared" over an empty store
                                  // makes the log prove nothing.
                                  var cleared = TokenStore.DeleteCredentials();

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { cleared });
                                  }
                                  else if (cleared)
                                  {
                                      DaleConsole.Success("Logged out", "(credentials cleared)");
                                  }
                                  else
                                  {
                                      DaleConsole.Info("No stored credentials to clear.");
                                  }

                                  return Task.FromResult(0);
                              });

            return command;
        }
    }
}