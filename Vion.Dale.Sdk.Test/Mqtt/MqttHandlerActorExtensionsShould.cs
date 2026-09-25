using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Mqtt
{
    /// <summary>
    ///     The half of the registration handshake a host relies on: where a handler's registration goes and
    ///     that it is on its way before the handler answers the host's request. A host that waits for every
    ///     answer before it next writes to its client finds every registration already queued there.
    /// </summary>
    [TestClass]
    public class MqttHandlerActorExtensionsShould
    {
        private readonly LifecycleHarness.RecordingActorContext _context = new();

        private readonly IMqttHandlerActor _sut = new SilentHandler();

        [TestMethod]
        [TestProperty("spec", "AC-HOST-001.1")]
        public void SendRegistrationToMqttClient()
        {
            // Arrange / Act
            _sut.RegisterWithMqttClient("probe", ["/state"], _context, NullLogger.Instance);

            // Assert
            var sent = _context.Sent.Single();
            Assert.AreEqual(MqttConstants.MqttClientName, sent.Target);
            Assert.IsInstanceOfType<RegisterMqttHandler>(sent.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HOST-001.1")]
        public void SendRegistrationBeforeAnswering()
        {
            // Arrange / Act
            _sut.RegisterWithMqttClient("probe", ["/state"], _context, NullLogger.Instance);

            // Assert
            CollectionAssert.AreEqual(new[] { typeof(RegisterMqttHandler), typeof(RegisterMqttHandlerResponse) }, _context.Log.Select(message => message.GetType()).ToList());
        }

        private sealed class SilentHandler : IMqttHandlerActor
        {
            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }
    }
}