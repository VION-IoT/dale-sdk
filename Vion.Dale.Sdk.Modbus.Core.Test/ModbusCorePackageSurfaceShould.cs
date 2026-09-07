using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Core.Test
{
    /// <summary>
    ///     What the package ships, read off the assembly rather than off a list: which of its public types are
    ///     published surface, and which are plumbing that is public only because another assembly of this build
    ///     reaches it. Rosters drift; these derive both sides and compare them, so a type added without a mark
    ///     fails here rather than at a consumer.
    ///     <para>
    ///         The analyzer that enforces the same rule at compile time is proven in
    ///         <c>Vion.Dale.Sdk.Generators.Test.AnalyzerWiringShould</c>, which builds this project with a
    ///         deliberately unmarked type linked in and requires the diagnostic. Until that reference landed
    ///         this package declared no published namespace at all, so nothing ever asked it for a mark.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ModbusCorePackageSurfaceShould
    {
        private static readonly Assembly Package = typeof(ModbusReceipt).Assembly;

        [TestMethod]
        [TestProperty("spec", "AC-MODB-019.1")]
        public void ClassifyEveryPublicTypeAsSurfaceOrPlumbing()
        {
            // Arrange
            // Nested types are included rather than filtered out: DALE014 judges a public type nested in a
            // public one exactly as it judges a top-level one, so a test that skipped them would pass on a
            // declaration the build still reports.
            var exported = Package.GetExportedTypes();

            // Act
            var unmarked = exported.Where(type => type.GetCustomAttribute<PublicApiAttribute>() == null && type.GetCustomAttribute<InternalApiAttribute>() == null)
                                   .Select(type => type.FullName!)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.IsNotEmpty(exported, $"{Package.GetName().Name} exports no public type at all, so an empty unmarked list proves nothing.");
            Assert.IsEmpty(unmarked,
                           $"Every public type this package ships is published surface or is marked internal plumbing; {exported.Length} classified, " +
                           $"{unmarked.Count} unmarked: {string.Join(", ", unmarked)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-019.1")]
        public void PublishOnlyWhatBlockAuthorNames()
        {
            // Arrange
            // The marked set is what reaches the API manifest and the generated reference, so it is the set a
            // block author is told to use: the registration they call, the converter they inject, the client
            // surface and its receipt, the server's accessors and extents, the protocol and timeout limits, and
            // every exception their error callback may receive by name. The seams beneath - the validator, the
            // bit-converter proxy, the concrete converter and the concrete accessors - are reached only through
            // the interfaces above them.

            // Act
            var published = Package.GetExportedTypes()
                                   .Where(type => type.GetCustomAttribute<PublicApiAttribute>() != null)
                                   .Select(type => type.Name)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.AreEqual("ByteOrder,IModbusBitAccessor,IModbusClient,IModbusDataConverter,IModbusRegisterAccessor,IModbusServerSnapshot," +
                            "InvalidBitQuantityException,InvalidCountException,InvalidServerAddressException,InvalidUnitIdentifierException," +
                            "ModbusException,ModbusExceptionCode,ModbusLinkState,ModbusLinkSummary,ModbusOutcome,ModbusProtocolLimits,ModbusReceipt," +
                            "ModbusResponseAlignmentException,ModbusServerArea,ModbusServerAreaExtents,ModbusTimeoutLimits,OperationTimeoutException," +
                            "RequestExpiredException,ServiceCollectionExtensions,TextEncoding,UnsupportedByteOrderException," +
                            "UnsupportedTextEncodingException,UnsupportedWordOrder32Exception,UnsupportedWordOrder64Exception,WordOrder32,WordOrder64",
                            string.Join(",", published));
        }
    }
}