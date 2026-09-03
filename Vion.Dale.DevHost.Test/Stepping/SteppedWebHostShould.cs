using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     A web host boots stepped when stepping is requested (<c>dale dev --stepped</c>), so a scenario run
    ///     over HTTP is deterministic without a <c>Program.cs</c> edit. The host is built and not started, so
    ///     nothing binds a port; the clock mode is observable on the control immediately.
    /// </summary>
    [TestClass]
    public class SteppedWebHostShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.1")]
        [DataRow(true, DisplayName = "stepping requested")]
        [DataRow(false, DisplayName = "stepping not requested")]
        public async Task ReportClockModeWebUiRequested(bool stepped)
        {
            // Arrange
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();

            // Act
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(stepped: stepped).Build();

            // Assert
            Assert.AreEqual(stepped, host.Control.IsStepped);
        }
    }
}