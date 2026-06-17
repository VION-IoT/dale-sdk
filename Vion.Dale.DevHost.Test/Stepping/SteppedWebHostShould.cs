using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     Part 2 of the RFC 0008 stepped-host enabler: <c>WithWebUi()</c> boots a stepped host when
    ///     stepping is requested (<c>dale dev --stepped</c> → <c>DALE_DEVHOST_STEPPED</c>), so server-side
    ///     scenario runs over HTTP (the Player + <c>dale scenario run</c>) are deterministic — without any
    ///     <c>Program.cs</c> edit. The host is built (not started), so no web server binds; <c>IsStepped</c>
    ///     is observable on the control immediately.
    /// </summary>
    [TestClass]
    public class SteppedWebHostShould
    {
        [TestMethod]
        public async Task BeStepped_WhenWebUiRequestsStepping()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(stepped: true).Build();

            Assert.IsTrue(host.Control.IsStepped, "WithWebUi(stepped: true) must boot a stepped host (controllable clock).");
        }

        [TestMethod]
        public async Task UseTheRealClock_WhenSteppingNotRequested()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithWebUi(stepped: false).Build();

            Assert.IsFalse(host.Control.IsStepped, "WithWebUi(stepped: false) must use the real clock.");
        }
    }
}