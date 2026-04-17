using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Infrastructure
{
    public static class DaleHttpClient
    {
        private static readonly HttpClient Http = new()
                                                  {
                                                      Timeout = TimeSpan.FromSeconds(30),
                                                  };

        static DaleHttpClient()
        {
            var version = typeof(DaleHttpClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"Vion.Dale.Cli/{version}");
        }

        public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                string accessToken,
                                                                CancellationToken cancellationToken = default,
                                                                params HttpStatusCode[] allowedStatuses)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            DaleConsole.Verbose($"HTTP {request.Method} {request.RequestUri}");

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DaleAuthException($"Request timed out: {request.Method} {request.RequestUri}");
            }
            catch (HttpRequestException ex)
            {
                throw new DaleAuthException($"Network error: {ex.Message}. Check your connectivity and API URL.");
            }

            DaleConsole.Verbose($"HTTP {(int)response.StatusCode} {response.StatusCode}");

            if (response.IsSuccessStatusCode || Array.IndexOf(allowedStatuses, response.StatusCode) >= 0)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            DaleConsole.Verbose($"Response body: {body}");

            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new DaleAuthException("Session expired. Run `dale login` again."),
                HttpStatusCode.Forbidden => new DaleAuthException("Access denied. Check your integrator permissions."),
                HttpStatusCode.NotFound => new DaleAuthException($"Endpoint not found: {request.RequestUri}"),
                _ => new DaleAuthException($"API error {(int)response.StatusCode}: {body}"),
            };
        }

        public static async Task<HttpResponseMessage> GetAsync(string url, string accessToken, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            return await SendAsync(request, accessToken, cancellationToken);
        }

        public static async Task<HttpResponseMessage> PostAsync(string url,
                                                                HttpContent content,
                                                                string accessToken,
                                                                CancellationToken cancellationToken = default,
                                                                params HttpStatusCode[] allowedStatuses)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            return await SendAsync(request, accessToken, cancellationToken, allowedStatuses);
        }
    }
}