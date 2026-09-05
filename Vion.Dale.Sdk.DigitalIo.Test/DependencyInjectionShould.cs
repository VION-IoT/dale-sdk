using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.Sdk.DigitalIo.Test
{
    /// <summary>
    ///     What the package asks a host's container for. The runtime discovers this by reflection at plugin
    ///     load and resolves a handler per actor, so both the set of types and their lifetime are the contract.
    /// </summary>
    [TestClass]
    public class DependencyInjectionShould
    {
        private readonly ServiceCollection _services = [];

        private readonly DependencyInjection _sut = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-009.1")]
        public void RegisterHandlersOfHardwareFacesOnly()
        {
            // Arrange / Act
            _sut.ConfigureServices(_services);

            // Assert — the provider faces' handlers are deliberately absent: a production host's scan skips
            // them by their marking and the development host stands in for them, so nothing resolves one.
            CollectionAssert.AreEquivalent(new[] { typeof(DigitalInputHandler), typeof(DigitalOutputHandler) }, _services.Select(descriptor => descriptor.ServiceType).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-009.1")]
        public void RegisterEachHandlerPerActor()
        {
            // Arrange / Act
            _sut.ConfigureServices(_services);

            // Assert — a handler owns per-contract state for as long as its actor lives; a shared instance
            // would carry one actor's state into another's.
            Assert.IsTrue(_services.All(descriptor => descriptor.Lifetime == ServiceLifetime.Transient));
        }
    }
}