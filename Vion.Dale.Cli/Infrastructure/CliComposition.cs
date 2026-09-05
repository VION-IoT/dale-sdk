using System;
using System.Net.Http;
using Vion.Dale.Cli.Auth;

namespace Vion.Dale.Cli.Infrastructure
{
    /// <summary>
    ///     Where the tool chooses the outside world it talks to. The CLI composes no container: its
    ///     transports and its credential-store root are process-wide, and this is the one place that sets
    ///     them. Production goes through the same three seams a test overrides, so neither is a private
    ///     path the other cannot take — a test overrides a value production also sets, rather than filling
    ///     a hole production leaves empty.
    /// </summary>
    internal static class CliComposition
    {
        /// <summary>
        ///     The real transport for both HTTP clients and the real user profile for the credential store.
        ///     Called from <see cref="Program.Main" /> — the tool's own entry point, which a test driving a
        ///     command through <c>BuildRootCommand().Parse(…).InvokeAsync()</c> does not run.
        /// </summary>
        internal static void UseProductionDependencies()
        {
            DaleHttpClient.UseTransport(new HttpClientHandler());
            AuthService.UseTransport(new HttpClientHandler());
            TokenStore.UseRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
    }
}