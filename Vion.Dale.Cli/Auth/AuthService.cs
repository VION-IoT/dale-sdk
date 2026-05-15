using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.Cli.Auth
{
    public static class AuthService
    {
        private const string CliClientId = "dale-cli";

        private static readonly TimeSpan AuthTimeout = TimeSpan.FromMinutes(5);

        private static readonly HttpClient Http = new();

        /// <summary>
        ///     Interactive browser-based login using Authorization Code + PKCE.
        /// </summary>
        public static async Task<StoredCredentials> AcquireInteractiveAsync(string authBaseUrl, CancellationToken cancellationToken = default)
        {
            // Generate PKCE challenge + CSRF state
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);
            var state = GenerateState();

            // Start local HTTP listener for callback (retry on port conflict)
            var (listener, port) = StartCallbackListener();
            var redirectUri = $"http://localhost:{port}/callback";

            // Build authorization URL
            var authUrl = $"{authBaseUrl}/protocol/openid-connect/auth" + $"?client_id={CliClientId}" + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                          $"&response_type=code" + $"&scope=openid%20user_impersonation" + $"&code_challenge={codeChallenge}" + $"&code_challenge_method=S256" + $"&state={state}";

            // Open browser
            try
            {
                OpenBrowser(authUrl);
            }
            catch
            {
                // If browser can't open, print URL for manual copy-paste
                Console.Error.WriteLine();
                Console.Error.WriteLine("  Open this URL in your browser:");
                Console.Error.WriteLine($"  {authUrl}");
                Console.Error.WriteLine();
            }

            // Wait for callback with timeout
            string authCode;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(AuthTimeout);

                var contextTask = listener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token));
                if (completedTask != contextTask)
                {
                    throw new DaleAuthException("Authentication timed out. Please try again.");
                }

                var context = await contextTask;

                // Verify CSRF state
                var returnedState = context.Request.QueryString["state"];
                if (returnedState != state)
                {
                    throw new DaleAuthException("Authentication failed: state parameter mismatch (possible CSRF attack).");
                }

                // Check for OAuth error response (e.g. user cancelled, access denied)
                var errorParam = context.Request.QueryString["error"];
                if (errorParam != null)
                {
                    var errorDesc = context.Request.QueryString["error_description"] ?? errorParam;
                    throw new DaleAuthException($"Authentication failed: {Uri.UnescapeDataString(errorDesc)}");
                }

                authCode = context.Request.QueryString["code"] ?? throw new DaleAuthException("No authorization code received.");

                // Send success response to browser
                var response = context.Response;
                var responseBytes = Encoding.UTF8.GetBytes("<html><body><h2>Authentication successful!</h2><p>You can close this window.</p></body></html>");
                response.ContentLength64 = responseBytes.Length;
                await response.OutputStream.WriteAsync(responseBytes, cancellationToken);
                response.Close();
            }
            finally
            {
                listener.Stop();
            }

            // Exchange code for tokens
            var tokenUrl = $"{authBaseUrl}/protocol/openid-connect/token";
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
                                                         {
                                                             ["grant_type"] = "authorization_code",
                                                             ["client_id"] = CliClientId,
                                                             ["code"] = authCode,
                                                             ["redirect_uri"] = redirectUri,
                                                             ["code_verifier"] = codeVerifier,
                                                         });

            var tokenResponse = await Http.PostAsync(tokenUrl, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new DaleAuthException($"Token exchange failed: {tokenResponse.StatusCode} - {errorBody}");
            }

            var json = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            return ParseTokenResponse(json);
        }

        /// <summary>
        ///     Non-interactive client credentials flow for CI/agents.
        /// </summary>
        public static async Task<StoredCredentials> AcquireClientCredentialsAsync(string authBaseUrl, string clientId, string clientSecret)
        {
            var tokenUrl = $"{authBaseUrl}/protocol/openid-connect/token";
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
                                                    {
                                                        ["grant_type"] = "client_credentials",
                                                        ["client_id"] = clientId,
                                                        ["client_secret"] = clientSecret,
                                                    });

            var response = await Http.PostAsync(tokenUrl, request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new DaleAuthException($"Client credentials auth failed: {response.StatusCode} - {errorBody}");
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseTokenResponse(json);
        }

        /// <summary>
        ///     Refresh an expired token using the refresh token.
        /// </summary>
        public static async Task<StoredCredentials> RefreshAsync(string authBaseUrl, string refreshToken)
        {
            var tokenUrl = $"{authBaseUrl}/protocol/openid-connect/token";
            var request = new FormUrlEncodedContent(new Dictionary<string, string>
                                                    {
                                                        ["grant_type"] = "refresh_token",
                                                        ["client_id"] = CliClientId,
                                                        ["refresh_token"] = refreshToken,
                                                    });

            var response = await Http.PostAsync(tokenUrl, request);
            if (!response.IsSuccessStatusCode)
            {
                throw new DaleAuthException("Token refresh failed. Please run `dale login` again.");
            }

            var json = await response.Content.ReadAsStringAsync();
            return ParseTokenResponse(json);
        }

        private static StoredCredentials ParseTokenResponse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var accessToken = root.GetProperty("access_token").GetString() ?? throw new DaleAuthException("No access_token in response.");
            var expiresIn = root.GetProperty("expires_in").GetInt32();

            string? refreshToken = null;
            if (root.TryGetProperty("refresh_token", out var refreshProp))
            {
                refreshToken = refreshProp.GetString();
            }

            return new StoredCredentials
                   {
                       AccessToken = accessToken,
                       RefreshToken = refreshToken,
                       ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
                   };
        }

        private static string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(bytes);
        }

        private static string GenerateState()
        {
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static (HttpListener Listener, int Port) StartCallbackListener()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var port = FindAvailablePort();
                var httpListener = new HttpListener();
                httpListener.Prefixes.Add($"http://localhost:{port}/");
                try
                {
                    httpListener.Start();
                    return (httpListener, port);
                }
                catch (HttpListenerException)
                {
                    // Port was taken between discovery and bind — retry
                }
            }

            throw new DaleAuthException("Could not find a free port for the OAuth callback.");
        }

        private static int FindAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void OpenBrowser(string url)
        {
            var psi = new ProcessStartInfo
                      {
                          FileName = url,
                          UseShellExecute = true,
                      };
            Process.Start(psi);
        }
    }
}