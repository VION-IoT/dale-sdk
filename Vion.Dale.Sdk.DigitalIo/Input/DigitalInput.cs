using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     Represents a digital input that can be used to communicate with hardware.
    /// </summary>
    public partial class DigitalInput : LogicBlockContractBase, IDigitalInput
    {
        private readonly ILogger<DigitalInput> _logger;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(DigitalInputHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalInput" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this digital input.</param>
        /// <param name="actorContext">The actor context used for communication with the HAL handler.</param>
        /// <param name="logger"></param>
        public DigitalInput(string identifier, IActorContext actorContext, ILogger<DigitalInput> logger) : base(identifier, actorContext)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Occurs when the digital input state changes.
        /// </summary>
        public event EventHandler<bool>? InputChanged;

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<DigitalInputChanged> m:
                    LogDigitalInputChangedReceived(LogicBlockContractId, m.Data.Value);
                    InputChanged?.Invoke(this, m.Data.Value);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Digital input changed received (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogDigitalInputChangedReceived(LogicBlockContractId logicBlockContractId, bool value);
    }
}