using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Mocking
{
    public class MockPersistentDataHandler : IActorReceiver
    {
        private readonly ILogger<MockPersistentDataHandler> _logger;

        public MockPersistentDataHandler(ILogger<MockPersistentDataHandler> logger)
        {
            _logger = logger;
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            switch (message)
            {
                case PersistentDataSnapshotChanged:
                    // do nothing
                    break;

                default:
                    _logger.LogDebug("Received message: {MessageType}", message.GetType().Name);
                    break;
            }

            return Task.CompletedTask;
        }
    }
}
