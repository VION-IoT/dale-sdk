using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     Represents an analog output that can be used to communicate with hardware.
    /// </summary>
    public partial class AnalogOutput : LogicBlockContractBase, IAnalogOutput
    {
        private readonly ILogger<AnalogOutput> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(AnalogOutputHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogOutput" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this analog output.</param>
        /// <param name="actorContext">The actor context used for communication with the HAL handler.</param>
        /// <param name="logger">The logger instance.</param>
        public AnalogOutput(string identifier, IActorContext actorContext, ILogger<AnalogOutput> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Occurs when the analog output state changes.
        /// </summary>
        public event EventHandler<double>? OutputChanged;

        /// <inheritdoc />
        public void Set(double value)
        {
            LogSendingAnalogOutputSetRequest(LogicBlockContractId, value);
            SendToContractHandler(new ContractMessage<SetAnalogOutput>(LogicBlockContractId, new SetAnalogOutput(value)));
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<AnalogOutputChanged> m:
                    LogAnalogOutputChangedReceived(LogicBlockContractId, m.Data.Value);
                    OutputChanged?.Invoke(this, m.Data.Value);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Analog output changed received (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogAnalogOutputChangedReceived(LogicBlockContractId logicBlockContractId, double value);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending analog output set request (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogSendingAnalogOutputSetRequest(LogicBlockContractId logicBlockContractId, double value);
    }
}