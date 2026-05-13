using System.Linq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Examples.PingPong.Contracts;
using Vion.Examples.PingPong.ServiceInterfaces;

namespace Vion.Examples.PingPong.LogicBlocks
{
    [LogicBlock(Name = "Ping", Icon = "ping-pong-line")]
    public class Ping : LogicBlockBase, IPing, IPingService
    {
        private readonly ILogger _logger;

        private int _count;

        private int _lastLoggedCount;

        private bool _pause;

        public IDigitalInput DigitalInput { get; set; } = null!;

        /// <inheritdoc />
        public Ping(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, Contracts.PingPong.PongResponse response)
        {
            if (Pause)
            {
                _logger.LogInformation("Paused, not sending ping request");
                return;
            }

            SendPingRequest();
        }

        /// <inheritdoc />
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Secondary)]
        public int PingsPerSecond { get; private set; } // from IPingService

        /// <inheritdoc />
        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Configuration)]
        public bool Pause // from IPingService
        {
            get => _pause;

            set
            {
                if (_pause != value) // on change
                {
                    _pause = value;
                    _logger.LogInformation("Pause set to {value}", value);
                    if (!value)
                    {
                        _logger.LogInformation("Resuming, sending ping request");
                        SendPingRequest();
                    }
                    else
                    {
                        _logger.LogInformation("Pausing, not sending ping request");
                    }
                }
            }
        }

        [Timer(1)]
        public void LogCount()
        {
            PingsPerSecond = _count - _lastLoggedCount;
            _logger.LogInformation("{PingsPerSecond} messages pinged back since last log", PingsPerSecond);
            _lastLoggedCount = _count;
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            DigitalInput.InputChanged += (_, value) => { _logger.LogInformation("DI changed to {value}", value); };
        }

        /// <inheritdoc />
        protected override void Starting()
        {
            _logger.LogInformation("Starting. sending initial ping request");
            SendPingRequest();
        }

        private void SendPingRequest()
        {
            var linkedPongs = this.GetLinkedPongs();
            switch (linkedPongs.Count)
            {
                case 0:
                    _logger.LogWarning("No linked function IDs found for Ping, cannot send ping request");
                    return;
                case > 1:
                    _logger.LogWarning("Multiple linked function IDs found for Ping, cannot determine which to use");
                    return;
                default:
                    this.SendRequest(linkedPongs.Single(), new Contracts.PingPong.PingRequest());
                    _count++;
                    break;
            }
        }
    }
}