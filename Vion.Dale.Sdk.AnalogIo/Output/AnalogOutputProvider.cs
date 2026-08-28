using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     The provider side of an analog output, bound by a simulator standing in for the hardware.
    /// </summary>
    public partial class AnalogOutputProvider : LogicBlockContractBase, IAnalogOutputProvider
    {
        private readonly ILogger<AnalogOutputProvider> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(AnalogOutputProviderHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogOutputProvider" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this analog output provider.</param>
        /// <param name="actorContext">The actor context used for communication with the handler.</param>
        /// <param name="logger"></param>
        public AnalogOutputProvider(string identifier, IActorContext actorContext, ILogger<AnalogOutputProvider> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Occurs when an analog output commands a value.
        /// </summary>
        public event EventHandler<double>? SetReceived;

        /// <inheritdoc />
        public void Confirm(double value)
        {
            LogSendingAnalogOutputConfirmation(LogicBlockContractId, value);
            SendToContractHandler(new ContractMessage<AnalogOutputChanged>(LogicBlockContractId, new AnalogOutputChanged(value)));
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<SetAnalogOutput> m:
                    LogSetAnalogOutputReceived(LogicBlockContractId, m.Data.Value);
                    SetReceived?.Invoke(this, m.Data.Value);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Set analog output received (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSetAnalogOutputReceived(LogicBlockContractId logicBlockContractId, double value);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending analog output confirmation (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSendingAnalogOutputConfirmation(LogicBlockContractId logicBlockContractId, double value);
    }
}