using System.Linq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Core.Test
{
    /// <summary>
    ///     The Dale plugin loader gives every plugin its own copy of an assembly unless the assembly is marked
    ///     <c>[DaleSharedAssembly]</c>. Types declared here travel in Modbus RTU's cross-plugin actor messages and
    ///     across the <c>IModbusRtu</c> surface a logic block calls, so an unmarked copy per plugin gives those types
    ///     one identity per plugin and breaks message routing — the failure the attribute exists to prevent, and one
    ///     that only shows up on a gateway running more than one Modbus plugin.
    /// </summary>
    [TestClass]
    public class SharedAssemblyMarkerShould
    {
        [TestMethod]
        public void MarkTheModbusCoreAssemblyAsSharedAcrossPlugins()
        {
            // Arrange
            var assembly = typeof(ModbusReceipt).Assembly;

            // Act
            var isMarked = assembly.GetCustomAttributes(typeof(DaleSharedAssemblyAttribute), false).Any();

            // Assert
            Assert.IsTrue(isMarked, $"{assembly.GetName().Name} declares types carried in Modbus RTU actor messages and must be shared across plugins.");
        }
    }
}