using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The one type a consumer implements to reach this area: a <c>MinChange</c> on a value type the
    ///     SDK ships no built-in deadband for resolves an <c>IChangeThreshold&lt;T&gt;</c> declared beside
    ///     the block. Being reachable is not enough — the marker asserted here is what puts it in the
    ///     PublicApi manifest and the generated API reference, and nothing else fails a build without it:
    ///     the manifest is regenerated and auto-committed rather than verified, and <c>DALE014</c> is a
    ///     warning.
    /// </summary>
    [TestClass]
    public class IChangeThresholdShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-014.1")]
        public void CarryPublicApiMarker()
        {
            // Arrange / Act
            var marker = typeof(IChangeThreshold<>).GetCustomAttributes(typeof(PublicApiAttribute), false);

            // Assert
            Assert.HasCount(1, marker);
        }
    }
}