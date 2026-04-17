using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     Represents an analog input that can be used to communicate with hardware.
    /// </summary>
    public partial class AnalogInput : LogicBlockContractBase, IAnalogInput
    {
        private readonly ILogger<AnalogInput> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(AnalogInputHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogInput" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this analog input.</param>
        /// <param name="actorContext">The actor context used for communication with the HAL handler.</param>
        /// <param name="logger">The logger instance.</param>
        public AnalogInput(string identifier, IActorContext actorContext, ILogger<AnalogInput> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Occurs when the analog input state changes.
        /// </summary>
        public event EventHandler<double>? InputChanged;

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<AnalogInputChanged> m:
                    LogAnalogInputChangedReceived(LogicBlockContractId, m.Data.Value);
                    InputChanged?.Invoke(this, m.Data.Value);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Analog input changed received (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogAnalogInputChangedReceived(LogicBlockContractId logicBlockContractId, double value);
    }
}