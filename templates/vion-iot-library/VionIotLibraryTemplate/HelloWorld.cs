using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace VionIotLibraryTemplate
{
    public class HelloWorld : LogicBlockBase
    {
        private readonly ILogger _logger;

        /// <summary>
        ///     A writable property for the greeting message
        /// </summary>
        [ServiceProperty]
        public string Greeting { get; set; } = "Hello, World!";

        /// <summary>
        ///     A measuring point for the number of times greeted
        /// </summary>
        [ServiceMeasuringPoint]
        public int TimesGreeted { get; private set; }

        /// <inheritdoc />
        public HelloWorld(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Method called by the runtime periodically based on the Timer attribute.
        /// </summary>
        [Timer(5)]
        public void Greet()
        {
            _logger.LogInformation(Greeting);
            TimesGreeted++;
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            _logger.LogInformation($"{nameof(HelloWorld)} is ready.");
        }
    }
}