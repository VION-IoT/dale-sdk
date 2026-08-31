using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     The provider side of a digital output, bound by a simulator standing in for the hardware.
    /// </summary>
    [InternalApi]
    public partial class DigitalOutputProvider : LogicBlockContractBase, IDigitalOutputProvider
    {
        private readonly ILogger<DigitalOutputProvider> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(DigitalOutputProviderHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalOutputProvider" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this digital output provider.</param>
        /// <param name="actorContext">The actor context used for communication with the handler.</param>
        /// <param name="logger"></param>
        public DigitalOutputProvider(string identifier, IActorContext actorContext, ILogger<DigitalOutputProvider> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Occurs when a digital output commands a value.
        /// </summary>
        public event EventHandler<bool>? SetReceived;

        /// <inheritdoc />
        public void Confirm(bool value)
        {
            LogSendingDigitalOutputConfirmation(LogicBlockContractId, value);
            SendToContractHandler(new ContractMessage<DigitalOutputChanged>(LogicBlockContractId, new DigitalOutputChanged(value)));
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<SetDigitalOutput> m:
                    LogSetDigitalOutputReceived(LogicBlockContractId, m.Data.Value);
                    SetReceived?.Invoke(this, m.Data.Value);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Set digital output received (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSetDigitalOutputReceived(LogicBlockContractId logicBlockContractId, bool value);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending digital output confirmation (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSendingDigitalOutputConfirmation(LogicBlockContractId logicBlockContractId, bool value);
    }
}