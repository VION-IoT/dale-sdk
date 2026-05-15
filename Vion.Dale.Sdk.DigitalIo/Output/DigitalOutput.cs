using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using System;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     Represents a digital output that can be used to communicate with hardware.
    /// </summary>
    public partial class DigitalOutput : LogicBlockContractBase, IDigitalOutput
    {
        private readonly ILogger<DigitalOutput> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(DigitalOutputHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalOutput" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this digital output.</param>
        /// <param name="actorContext">The actor context used for communication with the HAL handler.</param>
        /// <param name="logger"></param>
        public DigitalOutput(string identifier, IActorContext actorContext, ILogger<DigitalOutput> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Occurs when the digital output state changes.
        /// </summary>
        public event EventHandler<bool>? OutputChanged;

        /// <inheritdoc />
        public void Set(bool value)
        {
            LogSendingDigitalOutputSetRequest(LogicBlockContractId, value);
            SendToContractHandler(new ContractMessage<SetDigitalOutput>(LogicBlockContractId, new SetDigitalOutput(value)));
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<DigitalOutputChanged> m:
                    LogDigitalOutputChangedReceived(LogicBlockContractId, m.Data.Value);
                    OutputChanged?.Invoke(this, m.Data.Value);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Digital output changed received (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogDigitalOutputChangedReceived(LogicBlockContractId logicBlockContractId, bool value);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending digital output set request (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSendingDigitalOutputSetRequest(LogicBlockContractId logicBlockContractId, bool value);
    }
}