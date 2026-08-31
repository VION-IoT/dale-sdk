using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     The provider side of a digital input, bound by a simulator standing in for the hardware.
    /// </summary>
    [InternalApi]
    public partial class DigitalInputProvider : LogicBlockContractBase, IDigitalInputProvider
    {
        private readonly ILogger<DigitalInputProvider> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(DigitalInputProviderHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalInputProvider" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this digital input provider.</param>
        /// <param name="actorContext">The actor context used for communication with the handler.</param>
        /// <param name="logger"></param>
        public DigitalInputProvider(string identifier, IActorContext actorContext, ILogger<DigitalInputProvider> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void Drive(bool value)
        {
            LogSendingDigitalInputChange(LogicBlockContractId, value);
            SendToContractHandler(new ContractMessage<DigitalInputChanged>(LogicBlockContractId, new DigitalInputChanged(value)));
        }

        /// <summary>
        ///     Digital input providers are write-only — no contract messages arrive from the consumer.
        /// </summary>
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending digital input change (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSendingDigitalInputChange(LogicBlockContractId logicBlockContractId, bool value);
    }
}