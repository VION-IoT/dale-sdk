using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;
using Vion.Dale.Sdk.Http.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     Reads one holding register from 127.0.0.1 every virtual second once <see cref="Port" /> is set, through the SDK's
    ///     Modbus TCP client — the shape of a device block polling a simulator over loopback.
    /// </summary>
    [LogicBlock(Name = "Modbus poller")]
    public class ModbusPollerBlock : LogicBlockBase
    {
        private readonly ILogicBlockModbusTcpClient _client;

        private int _port;

        [ServiceProperty(Title = "Port")]
        public int Port
        {
            get => _port;

            set
            {
                _port = value;
                if (value == 0)
                {
                    return;
                }

                _client.IpAddress = IPAddress.Loopback.ToString();
                _client.Port = value;
                _client.IsEnabled = true;
            }
        }

        /// <summary>The last register value read; zero until a read has completed.</summary>
        [ServiceProperty(Title = "Register")]
        public int Register { get; private set; }

        public ModbusPollerBlock(ILogicBlockModbusTcpClientFactory clientFactory, ILogger logger) : base(logger)
        {
            _client = clientFactory.Create();
        }

        [Timer(1)]
        public void OnPoll()
        {
            if (_port == 0)
            {
                return;
            }

            _client.ReadHoldingRegistersAsShort(1, 0, 1, this, (values, _) => Register = values[0]);
        }

        protected override void Ready()
        {
        }

        protected override void Stopping()
        {
            _client.Dispose();
        }
    }

    /// <summary>
    ///     Fetches a JSON integer from <see cref="Url" /> the moment the URL is written, through the SDK's HTTP client — so
    ///     the request is issued by a control write, outside any advance.
    /// </summary>
    [LogicBlock(Name = "HTTP fetcher")]
    public class HttpFetcherBlock : LogicBlockBase
    {
        private readonly ILogicBlockHttpClient _client;

        private string _url = string.Empty;

        [ServiceProperty(Title = "Url")]
        public string Url
        {
            get => _url;

            set
            {
                _url = value;
                if (value.Length > 0)
                {
                    _client.GetJson<int>(this, value, fetched => Fetched = fetched);
                }
            }
        }

        /// <summary>The last value fetched; zero until a fetch has completed.</summary>
        [ServiceProperty(Title = "Fetched")]
        public int Fetched { get; private set; }

        public HttpFetcherBlock(ILogicBlockHttpClient client, ILogger logger) : base(logger)
        {
            _client = client;
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     Serves <c>GET /value</c> on 127.0.0.1 once <see cref="Port" /> is set, and counts the requests its server has
    ///     recorded every virtual second — a simulator's view of the requests it answered.
    /// </summary>
    [LogicBlock(Name = "HTTP serving")]
    public sealed class HttpServingBlock : LogicBlockBase
    {
        private readonly ILogicBlockHttpServer _server;

        private int _port;

        [ServiceProperty(Title = "Port")]
        public int Port
        {
            get => _port;

            set
            {
                _port = value;
                if (value == 0)
                {
                    return;
                }

                _server.Port = value;
                _server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/value", HttpServerResponse.Json("42")));
                _server.IsEnabled = true;
            }
        }

        /// <summary>How many requests the server has recorded, as of the last virtual second.</summary>
        [ServiceProperty(Title = "Recorded")]
        public int Recorded { get; private set; }

        public HttpServingBlock(ILogicBlockHttpServerFactory serverFactory, ILogger logger) : base(logger)
        {
            _server = serverFactory.Create();
        }

        [Timer(1)]
        public void OnCount()
        {
            Recorded += _server.Sync(snapshot => snapshot.TakeReceivedRequests().Count);
        }

        protected override void Ready()
        {
        }

        protected override void Stopping()
        {
            _server.Dispose();
        }
    }
}
