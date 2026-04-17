using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Microsoft.Extensions.Logging;
using Vion.Examples.PingPong.Contracts;
using Vion.Examples.PingPong.ServiceInterfaces;

namespace Vion.Examples.PingPong.LogicBlocks
{
    [LogicBlockInfo("Pong", "ping-pong-fill")]
    public class Pong : LogicBlockBase, IPong, IPongService
    {
        private readonly ILogger _logger;

        private int _count;

        private bool _lastDoState;

        private int _lastLoggedCount;

        public IDigitalOutput DigitalOutput { get; set; } = null!;

        /// <inheritdoc />
        public Pong(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Contracts.PingPong.PongResponse HandleRequest(Contracts.PingPong.PingRequest request)
        {
            _count++;
            return new Contracts.PingPong.PongResponse();
        }

        /// <inheritdoc />
        [Category(PropertyCategory.Metric)]
        [Importance(Importance.Secondary)]
        public int PongsPerSecond { get; private set; } // from IPongService

        [Timer(1)]
        public void LogCount()
        {
            PongsPerSecond = _count - _lastLoggedCount;
            _logger.LogDebug("{PongsPerSecond} messages pinged back since last log", PongsPerSecond);
            _lastLoggedCount = _count;
        }

        [Timer(5)]
        public void ToggleDo()
        {
            _logger.LogInformation("toggling de from {DoBefore} to {DoAfter}", _lastDoState, !_lastDoState);
            DigitalOutput.Set(!_lastDoState);
            _lastDoState = !_lastDoState;
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            DigitalOutput.OutputChanged += (_, value) => { _logger.LogInformation("DO changed to {value}", value); };
        }
    }
}