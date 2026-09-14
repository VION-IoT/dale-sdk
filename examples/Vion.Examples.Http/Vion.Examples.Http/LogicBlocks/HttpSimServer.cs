using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Examples.Http.LogicBlocks
{
    /// <summary>
    ///     A synthetic HTTP server so <see cref="HttpDebugClient" /> has something to talk to without a device on the bench.
    ///     It serves the routes configured in its slots and shows what it was asked: the latest requests, the last one in
    ///     full, and a hit count per route.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Plain HTTP, no TLS and no authentication: anything that can reach the port can read every route and send any
    ///         request. It binds <c>127.0.0.1</c>, the SDK server's own default, so nothing off the machine reaches it until
    ///         <see cref="ListenAddress" /> names an interface.
    ///     </para>
    ///     <para>
    ///         Listens on 18080 rather than the SDK default of 8080, which other software on a gateway commonly wants, so a
    ///         simulated device cannot be confused with a real one.
    ///     </para>
    ///     <para>
    ///         The server answers on its own threads from whatever the block last published and never calls the block. So an
    ///         edit to a route takes effect on the next tick, and what was asked is collected on the tick as well.
    ///     </para>
    /// </remarks>
    [LogicBlock(Name = "HTTP Sim Server",
                Icon = "server-line",
                Groups = new[]
                         {
                             PropertyGroup.Status,
                             PropertyGroup.Configuration,
                             PropertyGroup.Diagnostics,
                         })]
    public class HttpSimServer : LogicBlockBase
    {
        /// <summary>How many requests the table of recent requests keeps, newest first.</summary>
        private const int RecentRequestCount = 10;

        /// <summary>The last request's body is published as a service property, so it is capped well below what a body can be.</summary>
        private const int MaxBodyPreviewBytes = 8192;

        /// <summary>A body in the table of recent requests is a hint, not the document.</summary>
        private const int RowBodyPreviewChars = 120;

        private readonly ILogger _logger;

        private readonly ILogicBlockHttpServer _server;

        private string _listenAddress = "127.0.0.1";

        private int _listenPort = 18080;

        private bool _serverEnabled = true;

        [ServiceProperty(Title = "Server enabled", Description = "Binds the listener. Disable to free the port.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10)]
        public bool ServerEnabled
        {
            get => _serverEnabled;

            set
            {
                if (_serverEnabled == value)
                {
                    return;
                }

                _serverEnabled = value;
                ApplyServerConfiguration();
            }
        }

        [ServiceProperty(Title = "Listen address",
                         StringFormat = StringFormats.Ipv4,
                         Description =
                             "The address the listener binds. 127.0.0.1 keeps the simulated device on this machine. An interface's address opens it to that network, and 0.0.0.0 to every network the machine is on — in clear, to anyone who can reach the port.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 20)]
        public string ListenAddress
        {
            get => _listenAddress;

            set
            {
                if (_listenAddress == value)
                {
                    return;
                }

                _listenAddress = value;
                ApplyServerConfiguration();
            }
        }

        [ServiceProperty(Title = "Listen port", Minimum = 1, Maximum = 65535, Description = "TCP port the simulated server listens on.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 30)]
        public int ListenPort
        {
            get => _listenPort;

            set
            {
                if (_listenPort == value)
                {
                    return;
                }

                _listenPort = value;
                ApplyServerConfiguration();
            }
        }

        // ── Status ────────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Listening")]
        [Presentation(Group = PropertyGroup.Status, StatusIndicator = false, Importance = Importance.Primary)]
        public bool IsListening { get; private set; }

        [ServiceProperty(Title = "Requests", Description = "Requests the server has answered, over every route and none.")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public int RequestCount { get; private set; }

        [ServiceProperty(Title = "Last request")]
        [Presentation(Group = PropertyGroup.Status, Format = Formats.Relative)]
        public DateTime? LastRequestAt { get; private set; }

        [ServiceProperty(Title = "Recent requests", Description = "The latest requests answered, newest first.")]
        [Presentation(DisplayName = "Recent requests", Group = PropertyGroup.Status)]
        public ImmutableArray<ReceivedRequestRow> RecentRequests { get; private set; } = ImmutableArray<ReceivedRequestRow>.Empty;

        // ── Diagnostics ───────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Last request line", Description = "Method and target of the latest request, query string included.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 10)]
        public string LastRequestLine { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Last request headers")]
        [Presentation(DisplayName = "Last request headers", Group = PropertyGroup.Diagnostics, Order = 20)]
        public ImmutableArray<HeaderRow> LastRequestHeaders { get; private set; } = ImmutableArray<HeaderRow>.Empty;

        [ServiceProperty(Title = "Last request body", Description = "The start of the latest request's body, decoded as UTF-8.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 30, UiHint = UiHints.Multiline)]
        public string LastRequestBody { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Unmatched requests", Description = "Requests no enabled route answered, so the server sent 404 or 405.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 40)]
        public int UnmatchedRequestCount { get; private set; }

        [ServiceProperty(Title = "Dropped requests",
                         Description = "Requests answered but never shown, because more arrived between two ticks than the server keeps. Their answers were sent.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 50)]
        public int DroppedRequestCount { get; private set; }

        [ServiceProperty(Title = "Last error")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 60)]
        public string LastError { get; private set; } = string.Empty;

        // ── Route slots ───────────────────────────────────────────────────────────

        /// <summary>
        ///     How many route slots this instance exposes. Chosen when the block is configured — slots above the count do not
        ///     exist at all, rather than sitting empty in the UI.
        /// </summary>
        [ServiceProperty(Title = "Route slots", Minimum = 0, Maximum = 8, Description = "Number of editable routes this instance exposes. Set when the block is configured.")]
        [InstantiationParameter]
        [Presentation(Group = PropertyGroup.Configuration, Order = 40)]
        public int RouteSlotCount { get; init; } = 3;

        [IncludedWhen("RouteSlotCount >= 1")]
        public RouteSlot Route1 { get; } = new() { Enabled = true, Method = RequestMethod.Get, Path = "/api/status", Body = "{\"device\":\"dale-http-sim\",\"state\":\"ok\"}" };

        [IncludedWhen("RouteSlotCount >= 2")]
        public RouteSlot Route2 { get; } = new() { Enabled = true, Method = RequestMethod.Post, Path = "/api/setpoint", StatusCode = 202, Body = "{\"accepted\":true}" };

        [IncludedWhen("RouteSlotCount >= 3")]
        public RouteSlot Route3 { get; } = new() { Enabled = true, Method = RequestMethod.Get, Path = "/api/fault", StatusCode = 503, ContentType = "text/plain", Body = "maintenance" };

        [IncludedWhen("RouteSlotCount >= 4")]
        public RouteSlot Route4 { get; } = new();

        [IncludedWhen("RouteSlotCount >= 5")]
        public RouteSlot Route5 { get; } = new();

        [IncludedWhen("RouteSlotCount >= 6")]
        public RouteSlot Route6 { get; } = new();

        [IncludedWhen("RouteSlotCount >= 7")]
        public RouteSlot Route7 { get; } = new();

        [IncludedWhen("RouteSlotCount >= 8")]
        public RouteSlot Route8 { get; } = new();

        public HttpSimServer(ILogicBlockHttpServerFactory serverFactory, ILogger logger) : base(logger)
        {
            _server = serverFactory.Create();
            _logger = logger;
        }

        /// <summary>
        ///     Republishes every route and collects what was asked since the last tick, in one <c>Sync</c> so a request never
        ///     meets a table half rebuilt.
        /// </summary>
        [Timer(1)]
        public void OnTick()
        {
            Exchange();
            RefreshServerStatus();
        }

        /// <summary>
        ///     Routes are published before the listener binds, so the first request is answered from the configured table
        ///     rather than with a 404.
        /// </summary>
        protected override void Ready()
        {
            Exchange();
            ApplyServerConfiguration();
        }

        protected override void Stopping()
        {
            // A factory-created server comes from the root container, not the per-block scope that disposes an injected
            // one on teardown (docs/specs/http.md) — so releasing the port is the block's job.
            _server.Dispose();
        }

        /// <summary>
        ///     Reconfigures and rebinds the listener. The address and the port are settable only while the server is disabled,
        ///     so every change is a disable / apply / re-enable cycle.
        /// </summary>
        private void ApplyServerConfiguration()
        {
            try
            {
                _server.IsEnabled = false;
                _server.ListenAddress = _listenAddress;
                _server.Port = _listenPort;
                _server.IsEnabled = _serverEnabled;
                LastError = string.Empty;
            }
            catch (Exception ex)
            {
                // A half-typed address arrives here on every keystroke, and a port another process holds on enable — report
                // either and stay alive, so the operator can correct it from the UI.
                LastError = ex.Message;
                _logger.LogWarning(ex, "HTTP sim server could not listen on {Address}:{Port}", _listenAddress, _listenPort);
            }

            RefreshServerStatus();
        }

        private void RefreshServerStatus()
        {
            IsListening = _server.IsListening;
            LastRequestAt = _server.LastRequestAt?.UtcDateTime;
        }

        private void Exchange()
        {
            var slots = IncludedSlots().ToList();
            var published = new List<(RouteSlot Slot, string Method, string Path)>();
            IReadOnlyList<HttpServerRequest> requests = Array.Empty<HttpServerRequest>();
            var dropped = 0;

            _server.Sync(snapshot =>
                         {
                             snapshot.ClearResponses();
                             foreach (var slot in slots)
                             {
                                 if (TryPublish(snapshot, slot, published))
                                 {
                                     published.Add((slot, slot.Method.ToString().ToUpperInvariant(), slot.Path));
                                 }
                             }

                             // Read before taking: taking resets the count.
                             dropped = snapshot.DroppedRequestCount;
                             requests = snapshot.TakeReceivedRequests();
                         });

            Record(requests, dropped, published);
        }

        /// <summary>
        ///     Publishes one slot, or says on the slot why it serves nothing. An operator typing a status or a content type the
        ///     server refuses is met here, on every tick while it stays wrong, and must not take the block down.
        /// </summary>
        private static bool TryPublish(IHttpServerSnapshot snapshot, RouteSlot slot, IReadOnlyList<(RouteSlot Slot, string Method, string Path)> published)
        {
            if (!slot.Enabled)
            {
                slot.Status = "Disabled.";

                return false;
            }

            var method = slot.Method.ToString().ToUpperInvariant();
            var shadowing = published.FirstOrDefault(route => route.Method == method && route.Path == slot.Path);
            if (shadowing.Slot != null)
            {
                slot.Status = $"Not served: an earlier slot already answers {method} {slot.Path}.";

                return false;
            }

            try
            {
                var contentType = slot.ContentType.Length == 0 ? null : slot.ContentType;
                var response = new HttpServerResponse((HttpStatusCode)slot.StatusCode, contentType, Encoding.UTF8.GetBytes(slot.Body));
                snapshot.SetResponse(new HttpMethod(method), slot.Path, response);
            }
            catch (ArgumentException ex)
            {
                slot.Status = $"Not served: {ex.Message}";

                return false;
            }

            slot.Status = $"Serving {method} {slot.Path} → {slot.StatusCode}.";

            return true;
        }

        /// <summary>
        ///     Folds the requests taken this tick into the counters and tables. A request is credited to the route that
        ///     answers its method and path in the table just published — the same ordinal match the server makes — so a route
        ///     edited between answering a request and this tick is credited under its new shape.
        /// </summary>
        private void Record(IReadOnlyList<HttpServerRequest> requests, int dropped, IReadOnlyList<(RouteSlot Slot, string Method, string Path)> published)
        {
            DroppedRequestCount += dropped;
            if (requests.Count == 0)
            {
                return;
            }

            RequestCount += requests.Count;
            foreach (var request in requests)
            {
                var route = published.FirstOrDefault(candidate => candidate.Method == request.Method && candidate.Path == request.Path);
                if (route.Slot != null)
                {
                    route.Slot.HitCount++;
                    route.Slot.LastHitAt = request.ReceivedAt.UtcDateTime;
                }
                else
                {
                    UnmatchedRequestCount++;
                }
            }

            var newestFirst = requests.Reverse().Select(ToRow);
            RecentRequests = newestFirst.Concat(RecentRequests).Take(RecentRequestCount).ToImmutableArray();

            var latest = requests[requests.Count - 1];
            LastRequestLine = $"{latest.Method} {Target(latest)}";
            LastRequestHeaders = latest.Headers.Select(header => new HeaderRow(header.Key, header.Value)).ToImmutableArray();
            LastRequestBody = Decode(latest.Body, MaxBodyPreviewBytes);
        }

        private IEnumerable<RouteSlot> IncludedSlots()
        {
            // Mirrors the [IncludedWhen] gates: slots above the configured count exist as CLR objects but are not part of
            // this instance, so they must not be served.
            var all = new[] { Route1, Route2, Route3, Route4, Route5, Route6, Route7, Route8 };

            return all.Take(Math.Max(0, Math.Min(RouteSlotCount, all.Length)));
        }

        private static ReceivedRequestRow ToRow(HttpServerRequest request)
        {
            var body = Decode(request.Body, MaxBodyPreviewBytes);
            request.Headers.TryGetValue("Content-Type", out var contentType);

            return new ReceivedRequestRow(request.ReceivedAt.UtcDateTime,
                                          request.Method,
                                          Target(request),
                                          contentType ?? string.Empty,
                                          request.Body.Length,
                                          body.Length > RowBodyPreviewChars ? body.Substring(0, RowBodyPreviewChars) + "…" : body);
        }

        private static string Target(HttpServerRequest request)
        {
            return request.Query.Length == 0 ? request.Path : $"{request.Path}?{request.Query}";
        }

        private static string Decode(ReadOnlyMemory<byte> body, int maxBytes)
        {
            var bytes = body.Length > maxBytes ? body.Slice(0, maxBytes) : body;

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }

    /// <summary>
    ///     One request the simulated server answered, as the client sent it.
    /// </summary>
    public readonly record struct ReceivedRequestRow(
        [StructField(Title = "Received at")] DateTime ReceivedAt,
        [StructField(Title = "Method")] string Method,
        [StructField(Title = "Target", Description = "The path, and the query string when there was one.")]
        string Target,
        [StructField(Title = "Content type")] string ContentType,
        [StructField(Title = "Body bytes")] int BodyBytes,
        [StructField(Title = "Body", Description = "The start of the body, decoded as UTF-8.")]
        string BodyPreview);
}
