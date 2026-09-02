using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The one type a consumer implements to reach this area: a <c>MinChange</c> on a value type the
    ///     SDK ships no built-in deadband for resolves an <c>IChangeThreshold&lt;T&gt;</c> declared beside
    ///     the block. Being reachable is not enough — the generated API reference and the PublicApi
    ///     manifest are both driven by the marker asserted here.
    /// </summary>
    [TestClass]
    public class IChangeThresholdShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-014.1")]
        public void CarryThePublicApiMarker()
        {
            // Arrange / Act
            var marker = typeof(IChangeThreshold<>).GetCustomAttributes(typeof(PublicApiAttribute), false);

            // Assert
            Assert.HasCount(1, marker);
        }
    }
}
