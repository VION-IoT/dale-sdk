using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     The provider side of an analog input, bound by a simulator standing in for the hardware.
    /// </summary>
    [InternalApi]
    public partial class AnalogInputProvider : LogicBlockContractBase, IAnalogInputProvider
    {
        private readonly ILogger<AnalogInputProvider> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(AnalogInputProviderHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogInputProvider" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this analog input provider.</param>
        /// <param name="actorContext">The actor context used for communication with the handler.</param>
        /// <param name="logger"></param>
        public AnalogInputProvider(string identifier, IActorContext actorContext, ILogger<AnalogInputProvider> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void Drive(double value)
        {
            LogSendingAnalogInputChange(LogicBlockContractId, value);
            SendToContractHandler(new ContractMessage<AnalogInputChanged>(LogicBlockContractId, new AnalogInputChanged(value)));
        }

        /// <summary>
        ///     Analog input providers are write-only — no contract messages arrive from the consumer.
        /// </summary>
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending analog input change (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSendingAnalogInputChange(LogicBlockContractId logicBlockContractId, double value);
    }
}