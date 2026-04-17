using System;
using System.Threading.Tasks;
using Vion.Dale.Cli.Auth;

namespace Vion.Dale.Cli.Infrastructure
{
    /// <summary>
    ///     Resolves and holds the common context needed by cloud-facing commands:
    ///     environment, API/auth URLs, integrator, and access token.
    /// </summary>
    public class CommandContext
    {
        public DaleConfig Config { get; private set; } = new();

        public string Environment { get; private set; } = "production";

        public string ApiBaseUrl { get; private set; } = string.Empty;

        public string AuthBaseUrl { get; private set; } = string.Empty;

        public Guid IntegratorId { get; private set; }

        public string AccessToken { get; private set; } = string.Empty;

        /// <summary>
        ///     Resolves the full cloud context. Throws DaleAuthException on failure.
        /// </summary>
        public static async Task<CommandContext> ResolveAsync(string? environmentFlag = null,
                                                              Guid? integratorIdFlag = null,
                                                              string? clientId = null,
                                                              string? clientSecret = null,
                                                              bool requireIntegrator = true)
        {
            var config = TokenStore.LoadConfig();

            // 1. Environment
            var environment = environmentFlag ?? config.Environment ?? "production";

            // 2. URLs
            var apiBaseUrl = TokenStore.ResolveApiBaseUrl(environment);
            var authBaseUrl = TokenStore.ResolveAuthBaseUrl(environment);

            // Fall back to config for custom environments
            if (string.IsNullOrEmpty(apiBaseUrl))
            {
                apiBaseUrl = config.ApiBaseUrl;
            }

            if (string.IsNullOrEmpty(authBaseUrl))
            {
                authBaseUrl = config.AuthBaseUrl;
            }

            if (string.IsNullOrEmpty(apiBaseUrl))
            {
                throw new DaleAuthException($"Cannot resolve API URL for environment '{environment}'. Run `dale login` or `dale config set-environment` first.");
            }

            if (string.IsNullOrEmpty(authBaseUrl))
            {
                throw new DaleAuthException($"Cannot resolve auth URL for environment '{environment}'. Run `dale login` or `dale config set-environment` first.");
            }

            // 3. Access token (needed before /me call)
            var accessToken = await TokenProvider.GetAccessTokenAsync(clientId, clientSecret, environmentFlag);

            // 4. Integrator (flag > env var > config > auto-resolve via /me)
            Guid integratorId = default;
            if (requireIntegrator)
            {
                integratorId = integratorIdFlag ?? ParseGuidEnvVar("DALE_INTEGRATOR_ID") ?? config.IntegratorId ?? Guid.Empty;

                if (integratorId == Guid.Empty)
                {
                    integratorId = await AutoResolveIntegratorAsync(apiBaseUrl, accessToken);
                }
            }

            return new CommandContext
                   {
                       Config = config,
                       Environment = environment,
                       ApiBaseUrl = apiBaseUrl,
                       AuthBaseUrl = authBaseUrl,
                       IntegratorId = integratorId,
                       AccessToken = accessToken,
                   };
        }

        /// <summary>
        ///     Lightweight resolve for commands that only need config + URLs (no auth/integrator).
        /// </summary>
        public static CommandContext ResolveLocal(string? environmentFlag = null)
        {
            var config = TokenStore.LoadConfig();
            var environment = environmentFlag ?? config.Environment ?? "production";

            return new CommandContext
                   {
                       Config = config,
                       Environment = environment,
                       ApiBaseUrl = TokenStore.ResolveApiBaseUrl(environment) ?? config.ApiBaseUrl ?? string.Empty,
                       AuthBaseUrl = TokenStore.ResolveAuthBaseUrl(environment) ?? config.AuthBaseUrl ?? string.Empty,
                   };
        }

        private static async Task<Guid> AutoResolveIntegratorAsync(string apiBaseUrl, string accessToken)
        {
            MeResponse me;
            try
            {
                me = await MeClient.GetMeAsync(apiBaseUrl, accessToken);
            }
            catch (Exception ex)
            {
                throw new DaleAuthException($"Failed to auto-resolve integrator via /me: {ex.Message}");
            }

            if (me.IntegratorMemberships.Count == 0)
            {
                throw new DaleAuthException("No integrator memberships found. Contact your administrator.");
            }

            if (me.IntegratorMemberships.Count == 1)
            {
                return me.IntegratorMemberships[0].IntegratorId;
            }

            throw new DaleAuthException(
                "Multiple integrators found. Use --integrator-id or run `dale login` to select one:\n" +
                string.Join("\n", me.IntegratorMemberships.ConvertAll(m => $"  {m.IntegratorName} ({m.IntegratorSlug}): {m.IntegratorId}")));
        }

        private static Guid? ParseGuidEnvVar(string name)
        {
            var value = System.Environment.GetEnvironmentVariable(name);
            if (Guid.TryParse(value, out var guid))
            {
                return guid;
            }

            return null;
        }
    }
}