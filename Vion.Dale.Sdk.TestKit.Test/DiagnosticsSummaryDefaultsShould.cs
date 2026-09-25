using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;
using Vion.Dale.Sdk.Http.Server;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     Each diagnostics summary the SDK ships declares its own slow default, so a block publishing one whole
    ///     with no interval of its own does not publish it at the SDK's 250 ms. What that default does to a member is
    ///     <see cref="TypeDefaultEmissionShould" />'s; this pins that the shipped types carry it.
    /// </summary>
    [TestClass]
    public class DiagnosticsSummaryDefaultsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.8")]
        [DataRow(typeof(ModbusLinkSummary))]
        [DataRow(typeof(ModbusTcpConnectionSummary))]
        [DataRow(typeof(HttpClientSummary))]
        [DataRow(typeof(HttpServerSummary))]
        public void DeclareThirtySecondDefaultInterval(Type summary)
        {
            // Arrange / Act
            var declared = summary.GetCustomAttribute<DefaultMinIntervalAttribute>();

            // Assert
            Assert.IsNotNull(declared);
            Assert.AreEqual("30s", declared.MinInterval);
        }
    }
}
