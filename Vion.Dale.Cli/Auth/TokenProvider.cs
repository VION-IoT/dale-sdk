using System;
using System.Threading.Tasks;

namespace Vion.Dale.Cli.Auth
{
    public static class TokenProvider
    {
        /// <summary>
        ///     Resolve an access token using the priority chain:
        ///     1. Explicit flags (--client-id, --client-secret)
        ///     2. Environment variables (DALE_CLIENT_ID, DALE_CLIENT_SECRET)
        ///     3. Stored token from dale login
        ///     4. Error
        /// </summary>
        public static async Task<string> GetAccessTokenAsync(string? flagClientId = null, string? flagClientSecret = null, string? environment = null)
        {
            // Resolve auth base URL: explicit environment > stored config > default production. A custom
            // environment resolves to no named URL, so the stored configuration's is the answer — the same
            // fallback CommandContext.ResolveAsync applies, and without it a client-credentials or refresh
            // exchange against a custom environment posts to a relative URL.
            var config = TokenStore.LoadConfig();
            var effectiveEnvironment = environment ?? config.Environment ?? "production";
            var authBaseUrl = TokenStore.ResolveAuthBaseUrl(effectiveEnvironment);
            if (string.IsNullOrEmpty(authBaseUrl))
            {
                authBaseUrl = config.AuthBaseUrl;
            }

            if (string.IsNullOrEmpty(authBaseUrl))
            {
                throw new DaleAuthException($"Cannot resolve auth URL for environment '{effectiveEnvironment}'. Run `dale login` or `dale config set-environment` first.");
            }

            // 1. Explicit flags
            if (!string.IsNullOrEmpty(flagClientId) && !string.IsNullOrEmpty(flagClientSecret))
            {
                var result = await AuthService.AcquireClientCredentialsAsync(authBaseUrl, flagClientId, flagClientSecret);
                return result.AccessToken;
            }

            // 2. Environment variables
            var envClientId = Environment.GetEnvironmentVariable("DALE_CLIENT_ID");
            var envClientSecret = Environment.GetEnvironmentVariable("DALE_CLIENT_SECRET");
            if (!string.IsNullOrEmpty(envClientId) && !string.IsNullOrEmpty(envClientSecret))
            {
                var result = await AuthService.AcquireClientCredentialsAsync(authBaseUrl, envClientId, envClientSecret);
                return result.AccessToken;
            }

            // 3. Stored token
            var stored = TokenStore.LoadCredentials();
            if (stored != null && !string.IsNullOrEmpty(stored.Environment) &&
                !string.Equals(stored.Environment, effectiveEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                // A token minted for one environment is refused by the other with a 401, which the tool
                // reports as "Session expired. Run `dale login` again." — the one instruction that cannot
                // fix it. Say what is actually wrong.
                throw new DaleAuthException($"The stored login is for environment '{stored.Environment}', not '{effectiveEnvironment}'. " +
                                            $"Run `dale login -e {effectiveEnvironment}`, or pass --client-id and --client-secret.");
            }

            if (stored == null)
            {
                throw new DaleAuthException("Not logged in. Run `dale login`, set DALE_CLIENT_ID + DALE_CLIENT_SECRET, " + "or pass --client-id and --client-secret.");
            }

            // Refresh if expired
            if (stored.IsExpired && stored.RefreshToken != null)
            {
                try
                {
                    stored = await AuthService.RefreshAsync(authBaseUrl, stored.RefreshToken);
                    TokenStore.SaveCredentials(stored);
                }
                catch (DaleAuthException)
                {
                    throw new DaleAuthException("Stored token expired and refresh failed. Please run `dale login` again.");
                }
            }

            if (stored.IsExpired)
            {
                throw new DaleAuthException("Stored token expired with no refresh token. Please run `dale login` again.");
            }

            return stored.AccessToken;
        }
    }
}