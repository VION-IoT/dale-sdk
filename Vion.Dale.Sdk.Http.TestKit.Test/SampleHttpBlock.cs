using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.TestKit.Test
{
    /// <summary>
    ///     A block with a configuration path over HTTP, in the shape the first consumer's relayed meters take: it fetches a
    ///     device description with a GET and a timeout, and records what each callback delivered, in the order the callbacks
    ///     ran on its actor.
    /// </summary>
    public sealed class SampleHttpBlock : LogicBlockBase
    {
        private readonly ILogicBlockHttpClient _client;

        /// <summary>Every callback that ran, oldest first: the URL it answered and what it delivered.</summary>
        public List<(string Url, object Outcome)> Settled { get; } = new();

        /// <summary>The receipt each of those callbacks was handed, in the same order.</summary>
        public List<HttpReceipt> Receipts { get; } = new();

        public SampleHttpBlock(ILogicBlockHttpClient client, ILogger logger) : base(logger)
        {
            _client = client;
        }

        /// <summary>Issues a GET for a device description.</summary>
        public void FetchDescription(string url, TimeSpan? timeout = null)
        {
            _client.GetJson<DeviceDescription>(this, url, (description, receipt) => Settle(url, description, receipt), (exception, receipt) => Settle(url, exception, receipt), timeout: timeout);
        }

        /// <summary>Posts a status report whose response carries no body.</summary>
        public void ReportStatus(string url, DeviceDescription status)
        {
            _client.PostJson(this, url, status, receipt => Settle(url, "posted", receipt), (exception, receipt) => Settle(url, exception, receipt));
        }

        /// <summary>Sends a raw request and records the response it is handed.</summary>
        public void SendRaw(HttpRequestMessage request)
        {
            var url = request.RequestUri!.ToString();
            _client.SendRequest(this, request, (response, receipt) => Settle(url, response, receipt), (exception, receipt) => Settle(url, exception, receipt));
        }

        protected override void Ready()
        {
        }

        private void Settle(string url, object outcome, HttpReceipt receipt)
        {
            Settled.Add((url, outcome));
            Receipts.Add(receipt);
        }
    }

    /// <summary>The document the sample block fetches.</summary>
    public sealed class DeviceDescription
    {
        public string Serial { get; set; } = string.Empty;

        public int Value { get; set; }
    }
}