using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     A test-owned loopback peer — a raw socket, so nothing about it is counted by the host — that holds its answer to
    ///     the first request until the test releases it or a fallback elapses, and answers every later request at once. The
    ///     hold is what makes the window between a request leaving the actor system and its answer landing deterministic,
    ///     rather than a sub-millisecond race a fast machine always wins.
    /// </summary>
    internal sealed class HeldAnswerPeer : IAsyncDisposable
    {
        private readonly Task _acceptLoop;

        private readonly Func<Stream, CancellationToken, Task<bool>> _answerOne;

        private readonly TimeSpan _fallback;

        private readonly TcpListener _listener;

        private readonly TaskCompletionSource<bool> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _requestArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly CancellationTokenSource _stopping = new();

        private int _held;

        private HeldAnswerPeer(Func<Stream, Func<Task>, CancellationToken, Task<bool>> answerOne, TimeSpan fallback)
        {
            _fallback = fallback;
            _answerOne = (stream, token) => answerOne(stream, HoldFirstAsync, token);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = AcceptAsync();
        }

        public int Port { get; }

        /// <summary>Completes when the first request has reached the peer and is being held.</summary>
        public Task RequestArrived
        {
            get => _requestArrived.Task;
        }

        public async ValueTask DisposeAsync()
        {
            _released.TrySetResult(true);
            _stopping.Cancel();
            _listener.Stop();
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Stopping the listener ends the loop by failing its accept.
            }

            _stopping.Dispose();
        }

        /// <summary>A Modbus TCP server answering every holding-register read with <paramref name="value" />.</summary>
        public static HeldAnswerPeer Modbus(short value, TimeSpan fallback)
        {
            return new HeldAnswerPeer((stream, hold, token) => AnswerModbusReadAsync(stream, value, hold, token), fallback);
        }

        /// <summary>An HTTP server answering every request with the JSON body <paramref name="json" />, one request per connection.</summary>
        public static HeldAnswerPeer Http(string json, TimeSpan fallback)
        {
            return new HeldAnswerPeer((stream, hold, token) => AnswerHttpAsync(stream, json, hold, token), fallback);
        }

        public void Release()
        {
            _released.TrySetResult(true);
        }

        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
                _ = ServeAsync(client);
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    while (await _answerOne(stream, _stopping.Token).ConfigureAwait(false))
                    {
                    }
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // The client hung up or the peer is stopping.
                }
            }
        }

        private async Task HoldFirstAsync()
        {
            if (Interlocked.Exchange(ref _held, 1) == 1)
            {
                return;
            }

            _requestArrived.TrySetResult(true);
            await Task.WhenAny(_released.Task, Task.Delay(_fallback, _stopping.Token)).ConfigureAwait(false);
        }

        private static async Task<bool> AnswerModbusReadAsync(Stream stream, short value, Func<Task> hold, CancellationToken token)
        {
            var header = new byte[7];
            if (!await ReadExactlyOrEndAsync(stream, header, token).ConfigureAwait(false))
            {
                return false;
            }

            var pdu = new byte[((header[4] << 8) | header[5]) - 1];
            if (!await ReadExactlyOrEndAsync(stream, pdu, token).ConfigureAwait(false))
            {
                return false;
            }

            await hold().ConfigureAwait(false);
            var quantity = (pdu[3] << 8) | pdu[4];
            var response = new byte[9 + 2 * quantity];
            response[0] = header[0];
            response[1] = header[1];
            response[4] = (byte)((3 + 2 * quantity) >> 8);
            response[5] = (byte)(3 + 2 * quantity);
            response[6] = header[6];
            response[7] = pdu[0];
            response[8] = (byte)(2 * quantity);
            for (var register = 0; register < quantity; register++)
            {
                response[9 + 2 * register] = (byte)(value >> 8);
                response[10 + 2 * register] = (byte)value;
            }

            await stream.WriteAsync(response, token).ConfigureAwait(false);

            return true;
        }

        private static async Task<bool> AnswerHttpAsync(Stream stream, string json, Func<Task> hold, CancellationToken token)
        {
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one, token).ConfigureAwait(false) == 0)
                {
                    return false;
                }

                head.Append((char)one[0]);
            }

            await hold().ConfigureAwait(false);
            var body = Encoding.UTF8.GetBytes(json);
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, token).ConfigureAwait(false);
            await stream.WriteAsync(body, token).ConfigureAwait(false);

            return false;
        }

        private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            var filled = 0;
            while (filled < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(filled), token).ConfigureAwait(false);
                if (read == 0)
                {
                    return false;
                }

                filled += read;
            }

            return true;
        }
    }
}
