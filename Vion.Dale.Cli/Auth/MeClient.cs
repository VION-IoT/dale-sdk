using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Cli.Infrastructure;

namespace Vion.Dale.Cli.Auth
{
    public static class MeClient
    {
        /// <summary>
        ///     Calls the /me endpoint and returns the integrator memberships.
        /// </summary>
        public static async Task<MeResponse> GetMeAsync(string apiBaseUrl, string accessToken, CancellationToken cancellationToken = default)
        {
            var response = await DaleHttpClient.GetAsync($"{apiBaseUrl}/me", accessToken, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<MeResponse>(json, JsonDefaults.Options) ?? throw new DaleAuthException("Failed to parse /me response.");
        }
    }

    public class MeResponse
    {
        public MeUser User { get; set; } = new();

        public List<MeIntegratorMembership> IntegratorMemberships { get; set; } = new();
    }

    public class MeUser
    {
        public string? Email { get; set; }
    }

    public class MeIntegratorMembership
    {
        public Guid IntegratorId { get; set; }

        public string IntegratorSlug { get; set; } = string.Empty;

        public string IntegratorName { get; set; } = string.Empty;
    }
}