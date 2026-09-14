using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     Hosts an HTTP server for a logic block: clients elsewhere on the network request paths, and the server answers
    ///     each from the responses the block has published.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Plain HTTP only.</b> The server speaks unencrypted HTTP/1.1 and authenticates nobody: anything that can
    ///         reach the port can read every response and send any request. Bind a trusted interface, or serve nothing a
    ///         network peer must not see.
    ///     </para>
    ///     <para>
    ///         The server is configured via properties and gated by <see cref="IsEnabled" />: configure while disabled,
    ///         then enable. It listens on loopback (<c>127.0.0.1</c>) on port 8080 unless told otherwise, so nothing off the
    ///         machine reaches it until the block sets <see cref="ListenAddress" /> to an interface, or to <c>0.0.0.0</c>
    ///         for all of them.
    ///     </para>
    ///     <para>
    ///         The block publishes responses inside <see cref="Sync(Action{IHttpServerSnapshot})" />, keyed by method and
    ///         path, and takes the requests the server has answered there too. Requests are answered on background threads
    ///         from what the block last published; no event or callback is ever delivered to the block from those threads,
    ///         so a block reacts to a request on its own cadence. A path with no response answers 404, and a path published
    ///         only under other methods answers 405.
    ///     </para>
    ///     <para>
    ///         Each connection carries one request and is closed after its response. A request body needs a
    ///         <c>Content-Length</c>; a chunked body, an oversized request, a malformed one or a client that does not finish
    ///         its request in time is refused by the server itself and never reaches the block.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface ILogicBlockHttpServer : IDisposable
    {
        /// <summary>
        ///     Gets or sets whether the server is enabled. Setting <c>true</c> binds the listener and throws, leaving the server
        ///     disabled, when the port cannot be bound. Must not be set from inside a <c>Sync</c> callback.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        ///     Gets or sets the local IP address the server listens on. Default is <c>"127.0.0.1"</c> (loopback);
        ///     <c>"0.0.0.0"</c> listens on all interfaces. Changeable only while disabled.
        /// </summary>
        string? ListenAddress { get; set; }

        /// <summary>
        ///     Gets or sets the local port the server listens on, from 1 to 65535. Default is 8080; changeable only while
        ///     disabled.
        /// </summary>
        int Port { get; set; }

        /// <summary>
        ///     Gets a value indicating whether the server is currently listening for connections.
        /// </summary>
        bool IsListening { get; }

        /// <summary>
        ///     Gets when the most recent request arrived, or <c>null</c> when none has.
        /// </summary>
        DateTimeOffset? LastRequestAt { get; }

        /// <summary>
        ///     Executes <paramref name="access" /> with exclusive access to the published responses and the answered requests.
        /// </summary>
        /// <param name="access">
        ///     The callback receiving the snapshot. It runs on the caller's thread while requests wait for it to return; keep
        ///     it short, and do not use the snapshot after it returns.
        /// </param>
        void Sync(Action<IHttpServerSnapshot> access);

        /// <summary>
        ///     Executes <paramref name="access" /> with exclusive access to the published responses and the answered requests,
        ///     and returns its result.
        /// </summary>
        /// <typeparam name="T">The type of the result.</typeparam>
        /// <param name="access">The callback receiving the snapshot; see <see cref="Sync(Action{IHttpServerSnapshot})" />.</param>
        /// <returns>The value returned by <paramref name="access" />.</returns>
        T Sync<T>(Func<IHttpServerSnapshot, T> access);
    }
}