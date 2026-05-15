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
            // Resolve auth base URL: explicit environment > stored config > default production
            var effectiveEnvironment = environment ?? TokenStore.LoadConfig().Environment ?? "production";
            var authBaseUrl = TokenStore.ResolveAuthBaseUrl(effectiveEnvironment);

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