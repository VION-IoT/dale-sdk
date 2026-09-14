using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;

namespace Vion.Examples.Http.LogicBlocks
{
    /// <summary>
    ///     An interactive HTTP client for working out what a device's REST face actually answers: set the method, URL,
    ///     headers and body, send one request, and read back the status, the latency, the response headers and a preview of
    ///     the body.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One request at a time. Sending while a request is still in flight is refused rather than queued, so the
    ///         response on screen always belongs to the request described beside it.
    ///     </para>
    ///     <para>
    ///         Only a 2xx response reaches this block with its headers and body. The SDK judges the status before handing a
    ///         response over and reports anything else as a failure, so for a 4xx or 5xx the block shows the status code and
    ///         nothing of what the server sent with it.
    ///     </para>
    /// </remarks>
    [LogicBlock(Name = "HTTP Debug Client",
                Icon = "search-line",
                Groups = new[]
                         {
                             PropertyGroup.Status,
                             RequestGroup,
                             ResponseGroup,
                             PropertyGroup.Diagnostics,
                         })]
    public class HttpDebugClient : LogicBlockBase
    {
        private const string RequestGroup = "request";

        private const string ResponseGroup = "response";

        /// <summary>The body preview is published as a service property, so it is capped well below what a body can be.</summary>
        private const int MaxBodyPreviewBytes = 8192;

        private readonly ILogicBlockHttpClient _httpClient;

        private readonly ILogger _logger;

        private readonly TimeProvider _timeProvider;

        private bool _inFlight;

        // ── Status ────────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Outcome", Description = "How the last request ended.")]
        [Presentation(DisplayName = "Outcome", Group = PropertyGroup.Status, StatusIndicator = true, Importance = Importance.Primary)]
        public RequestOutcome Outcome { get; private set; } = RequestOutcome.Idle;

        // ── Request ───────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Method")]
        [Presentation(DisplayName = "Method", Group = RequestGroup, Order = 10)]
        public RequestMethod Method { get; set; } = RequestMethod.Get;

        [ServiceProperty(Title = "URL", StringFormat = StringFormats.Uri, Description = "An absolute http:// or https:// URL, query string included.")]
        [Presentation(Group = RequestGroup, Order = 20)]
        public string Url { get; set; } = "http://127.0.0.1:18080/api/status";

        [ServiceProperty(Title = "Headers",
                         Description =
                             "One header per line, as 'Name: value'. Content-Type and the other headers that describe a body are sent with the body, so they need one. Stored in the block's configuration and visible to anyone who can see it — do not paste a production credential.")]
        [Presentation(Group = RequestGroup, Order = 30, UiHint = UiHints.Multiline)]
        public string RequestHeaders { get; set; } = "Accept: application/json";

        [ServiceProperty(Title = "Body", Description = "Sent as UTF-8 exactly as typed, on any method. Leave empty to send no body.")]
        [Presentation(Group = RequestGroup, Order = 40, UiHint = UiHints.Multiline)]
        public string RequestBody { get; set; } = string.Empty;

        [ServiceProperty(Title = "Timeout",
                         Description =
                             "How long to wait for the response headers, and again for the body preview. The SDK's own client gives up after 30 seconds, so a longer value does not wait longer.")]
        [Presentation(Group = RequestGroup, Order = 50)]
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        [ServiceProperty(Title = "Send now", Description = "Sends one request built from the fields above.")]
        [Presentation(Group = RequestGroup, Order = 60, UiHint = UiHints.Trigger)]
        public bool SendOnce
        {
            get => false;

            set
            {
                if (value)
                {
                    Send();
                }
            }
        }

        // ── Response ──────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Status code", Description = "The status the server answered with — also for a 4xx or 5xx. Empty when no answer arrived.")]
        [Presentation(Group = ResponseGroup, Order = 10, Importance = Importance.Primary)]
        public int? StatusCode { get; private set; }

        [ServiceProperty(Title = "Reason phrase", Description = "The text the server put after the status code. Servers may leave it empty.")]
        [Presentation(Group = ResponseGroup, Order = 20)]
        public string ReasonPhrase { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Latency",
                         Unit = "ms",
                         Description =
                             "From sending to the response headers — or to the failure — measured on the block's clock when the answer reached the block, so a busy block adds its own wait.")]
        [Presentation(Group = ResponseGroup, Order = 30, Decimals = 1, Importance = Importance.Secondary)]
        public double? LatencyMs { get; private set; }

        [ServiceProperty(Title = "Content type")]
        [Presentation(Group = ResponseGroup, Order = 40)]
        public string ResponseContentType { get; private set; } = string.Empty;

        // A header table is state, not a time series, for the same reason the Modbus client's register table is: a
        // measuring point would push the whole array into the charting pipeline on every response.
        [ServiceProperty(Title = "Response headers", Description = "Every header of a 2xx response, the body's own headers included.")]
        [Presentation(DisplayName = "Response headers", Group = ResponseGroup, Order = 50)]
        public ImmutableArray<HeaderRow> ResponseHeaders { get; private set; } = ImmutableArray<HeaderRow>.Empty;

        [ServiceProperty(Title = "Body preview", Description = "The start of a 2xx response's body, decoded as UTF-8.")]
        [Presentation(Group = ResponseGroup, Order = 60, UiHint = UiHints.Multiline)]
        public string ResponseBodyPreview { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Body truncated", Description = "The body was longer than the preview holds; the rest was not read.")]
        [Presentation(Group = ResponseGroup, Order = 70)]
        public bool ResponseBodyTruncated { get; private set; }

        // ── Diagnostics ───────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Last request", Description = "What was sent last: method, URL, header count and body size.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 10)]
        public string LastRequest { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Last sent at")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 20, Format = Formats.Relative)]
        public DateTime? LastSentAt { get; private set; }

        [ServiceProperty(Title = "Last error")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 30)]
        public string LastError { get; private set; } = string.Empty;

        public HttpDebugClient(ILogicBlockHttpClient httpClient, TimeProvider timeProvider, ILogger logger) : base(logger)
        {
            _httpClient = httpClient;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        protected override void Ready()
        {
        }

        // ── Sending ───────────────────────────────────────────────────────────────

        private void Send()
        {
            if (_inFlight)
            {
                // The outcome is left alone: it still describes the request that is in flight.
                LastError = "A request is already in flight. Wait for its answer or its timeout before sending again.";

                return;
            }

            HttpRequestMessage request;
            try
            {
                request = BuildRequest();
            }
            catch (FormatException ex)
            {
                // Nothing was sent, so the previous response is cleared rather than left beside a request it does not
                // belong to.
                ClearResponse();
                Outcome = RequestOutcome.Invalid;
                LastError = ex.Message;

                return;
            }

            ClearResponse();
            LastRequest = Describe(request);
            LastSentAt = _timeProvider.GetUtcNow().UtcDateTime;
            LastError = string.Empty;
            Outcome = RequestOutcome.InFlight;
            _inFlight = true;

            var timeout = Timeout;
            var sentAt = _timeProvider.GetTimestamp();

            try
            {
                _httpClient.SendRequest(this, request, response => OnResponse(request, response, sentAt, timeout), exception => OnFailure(request, exception, sentAt), timeout);
            }
            catch (ArgumentException ex)
            {
                // The SDK refuses a timeout its cancellation source cannot take before anything is sent.
                request.Dispose();
                _inFlight = false;
                Outcome = RequestOutcome.Invalid;
                LastError = ex.Message;
            }
        }

        /// <summary>
        ///     Builds the request exactly as typed. Every refusal is a <see cref="FormatException" /> raised before anything
        ///     is sent, so a half-typed URL or header surfaces as <see cref="RequestOutcome.Invalid" /> and never reaches the
        ///     network.
        /// </summary>
        private HttpRequestMessage BuildRequest()
        {
            var url = Url.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new FormatException($"'{Url}' is not an absolute http:// or https:// URL.");
            }

            if (Timeout <= TimeSpan.Zero)
            {
                throw new FormatException("The timeout must be longer than zero.");
            }

            var request = new HttpRequestMessage(ToHttpMethod(Method), uri);
            if (RequestBody.Length > 0)
            {
                request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(RequestBody));
            }

            try
            {
                foreach (var (name, value) in ParseHeaders(RequestHeaders))
                {
                    AddHeader(request, name, value);
                }
            }
            catch
            {
                request.Dispose();

                throw;
            }

            return request;
        }

        /// <summary>
        ///     Puts a header where the platform will send it. A request carries two header collections — the request's own
        ///     and its body's — and each refuses the other's names, so a <c>Content-Type</c> added to the request is
        ///     silently dropped. Trying the request first and the body second sends every header that has a place, and a
        ///     body header typed without a body is refused by name instead of vanishing.
        /// </summary>
        private static void AddHeader(HttpRequestMessage request, string name, string value)
        {
            if (request.Headers.TryAddWithoutValidation(name, value))
            {
                return;
            }

            if (request.Content != null && request.Content.Headers.TryAddWithoutValidation(name, value))
            {
                return;
            }

            if (request.Content == null && new ByteArrayContent(Array.Empty<byte>()).Headers.TryAddWithoutValidation(name, value))
            {
                throw new FormatException($"'{name}' describes a body, and this request has none. Enter a body to send it.");
            }

            throw new FormatException($"'{name}' cannot be sent as a request header.");
        }

        private static IEnumerable<(string Name, string Value)> ParseHeaders(string headers)
        {
            foreach (var rawLine in headers.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    throw new FormatException($"The header line '{line}' is not in the form 'Name: value'.");
                }

                yield return (line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim());
            }
        }

        // ── Receiving ─────────────────────────────────────────────────────────────

        /// <summary>
        ///     Takes a 2xx response as soon as its headers are in. The body may still be arriving, so it is read off the actor
        ///     and handed back once the preview is full or the body ends; the response is this block's to dispose, and the
        ///     body read disposes it.
        /// </summary>
        private void OnResponse(HttpRequestMessage request, HttpResponseMessage response, long sentAt, TimeSpan timeout)
        {
            LatencyMs = _timeProvider.GetElapsedTime(sentAt).TotalMilliseconds;
            StatusCode = (int)response.StatusCode;
            ReasonPhrase = response.ReasonPhrase ?? string.Empty;
            ResponseContentType = response.Content?.Headers.ContentType?.ToString() ?? string.Empty;
            ResponseHeaders = response.Headers
                                      .Concat(response.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                                      .Select(header => new HeaderRow(header.Key, string.Join(", ", header.Value)))
                                      .ToImmutableArray();

            _ = ReadBodyPreviewAsync(request, response, timeout);
        }

        private async Task ReadBodyPreviewAsync(HttpRequestMessage request, HttpResponseMessage response, TimeSpan timeout)
        {
            var preview = string.Empty;
            var truncated = false;
            string? error = null;

            try
            {
                if (response.Content != null)
                {
                    // Bounded on the block's clock, because the per-request timeout ends at the headers and a server that
                    // stalls mid-body would otherwise hold this request in flight for good.
                    using var cancellation = _timeProvider.CreateCancellationTokenSource(timeout);
                    using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                    // One byte beyond the preview tells a body that exactly fills it from one that is longer.
                    var buffer = new byte[MaxBodyPreviewBytes + 1];
                    var filled = 0;
                    int read;
                    while (filled < buffer.Length && (read = await stream.ReadAsync(buffer, filled, buffer.Length - filled, cancellation.Token).ConfigureAwait(false)) > 0)
                    {
                        filled += read;
                    }

                    truncated = filled > MaxBodyPreviewBytes;
                    preview = Encoding.UTF8.GetString(buffer, 0, Math.Min(filled, MaxBodyPreviewBytes));
                }
            }
            catch (OperationCanceledException)
            {
                error = $"The body did not finish within the timeout of {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.";
            }
            catch (Exception ex)
            {
                // Caught whole: an exception escaping here ends a task nobody observes, and the block would read
                // In flight until it restarts.
                error = $"The body could not be read: {ex.Message}";
            }
            finally
            {
                response.Dispose();
                request.Dispose();
            }

            try
            {
                InvokeSynchronized(() => CompleteResponse(preview, truncated, error));
            }
            catch (Exception ex)
            {
                // The block stopped while the body was arriving; nothing is left to show the preview on.
                _logger.LogDebug(ex, "HTTP debug client could not hand the body preview back to the block");
            }
        }

        private void CompleteResponse(string preview, bool truncated, string? error)
        {
            _inFlight = false;
            ResponseBodyPreview = preview;
            ResponseBodyTruncated = truncated;

            if (error == null)
            {
                Outcome = RequestOutcome.Succeeded;
            }
            else
            {
                // The status and headers did arrive, so they stay; only the body is missing.
                Outcome = RequestOutcome.Failed;
                LastError = error;
            }
        }

        private void OnFailure(HttpRequestMessage request, Exception exception, long sentAt)
        {
            request.Dispose();
            _inFlight = false;
            LatencyMs = _timeProvider.GetElapsedTime(sentAt).TotalMilliseconds;

            var status = AnsweredStatus(exception);
            if (status != null)
            {
                StatusCode = (int)status.Value;
                Outcome = RequestOutcome.HttpError;
                LastError = $"The server answered {(int)status.Value}. Only a 2xx response reaches this block with its headers and body.";
            }
            else
            {
                Outcome = exception is TimeoutException ? RequestOutcome.TimedOut : RequestOutcome.Failed;
                LastError = DescribeFailure(exception);
            }

            _logger.LogDebug(exception, "HTTP debug client request failed");
        }

        /// <summary>
        ///     The status of a response the SDK refused for being outside 2xx, or <c>null</c> when no response arrived.
        /// </summary>
        /// <remarks>
        ///     Both cases arrive as <see cref="HttpRequestException" />: the platform wraps a refused connection in one too.
        ///     What tells them apart is the exception's <c>StatusCode</c>, set only for a response. This block targets
        ///     <c>netstandard2.1</c>, which does not declare that property, while every runtime it loads into sets it — so it
        ///     is read by name.
        /// </remarks>
        private static HttpStatusCode? AnsweredStatus(Exception exception)
        {
            return exception is HttpRequestException ? exception.GetType().GetProperty("StatusCode")?.GetValue(exception) as HttpStatusCode? : null;
        }

        /// <summary>
        ///     The message a transport failure carries is often only "An error occurred while sending the request."; the
        ///     cause — a refused connection, an unknown host — is in the inner exceptions, so their messages are appended.
        /// </summary>
        private static string DescribeFailure(Exception exception)
        {
            var messages = new List<string>();
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (!messages.Contains(current.Message))
                {
                    messages.Add(current.Message);
                }
            }

            return string.Join(" → ", messages);
        }

        private void ClearResponse()
        {
            StatusCode = null;
            ReasonPhrase = string.Empty;
            LatencyMs = null;
            ResponseContentType = string.Empty;
            ResponseHeaders = ImmutableArray<HeaderRow>.Empty;
            ResponseBodyPreview = string.Empty;
            ResponseBodyTruncated = false;
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        private string Describe(HttpRequestMessage request)
        {
            var headerCount = ParseHeaders(RequestHeaders).Count();

            return $"{request.Method} {request.RequestUri} — {headerCount} header(s), {Encoding.UTF8.GetByteCount(RequestBody)} byte body";
        }

        private static HttpMethod ToHttpMethod(RequestMethod method)
        {
            return new HttpMethod(method.ToString().ToUpperInvariant());
        }
    }

    /// <summary>
    ///     One HTTP header, its values joined by <c>", "</c> when it carried several.
    /// </summary>
    public readonly record struct HeaderRow([StructField(Title = "Name")] string Name, [StructField(Title = "Value")] string Value);
}