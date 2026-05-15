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
                                  TokenStore.DeleteCredentials();
                                  DaleConsole.Success("Logged out", "(credentials cleared)");
                                  return Task.FromResult(0);
                              });

            return command;
        }
    }
}