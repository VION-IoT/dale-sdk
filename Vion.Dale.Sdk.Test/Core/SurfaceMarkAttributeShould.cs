using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Where a surface mark may be written, and how far it reaches. Neither is decoration: <c>DALE014</c>
    ///     asks every public type in a declared published namespace for one of these two marks, so the
    ///     attribute has to accept every kind the diagnostic judges and has to mean the same thing to every
    ///     reader of it.
    ///     <para>
    ///         A kind the diagnostic judges but the attribute refuses is a diagnostic an author cannot
    ///         satisfy — the shape a public <c>delegate</c> had, where following <c>DALE014</c> produced
    ///         <c>CS0592</c>. And an inherited mark is one three readers disagree about: the analyzer
    ///         (<c>ISymbol.GetAttributes</c>) and the manifest generator (a source scan) both see only what
    ///         is declared, while reflection's default sees a base type's mark on every subclass — so
    ///         <c>ModbusRtu</c>, plumbing deriving from the published <c>LogicBlockContractBase</c>, read as
    ///         published to every package-surface test in this repository and as unmarked to the build.
    ///     </para>
    /// </summary>
    [TestClass]
    public class SurfaceMarkAttributeShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.7")]
        [DataRow(typeof(PublicApiAttribute))]
        [DataRow(typeof(InternalApiAttribute))]
        public void BeDeclarableOnEveryKindDale014Judges(Type attribute)
        {
            // Arrange
            // The rows are the two marks, which must agree: DALE014 accepts either, so a kind one of them
            // refuses is a kind the diagnostic cannot be answered for at all.
            const AttributeTargets judged = AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct | AttributeTargets.Delegate;

            // Act
            var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>();

            // Assert
            Assert.IsNotNull(usage);
            Assert.AreEqual(judged, usage.ValidOn, $"{attribute.Name} refuses a declaration kind DALE014 asks for a mark on.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.8")]
        [DataRow(typeof(PublicApiAttribute))]
        [DataRow(typeof(InternalApiAttribute))]
        public void NotReachSubclassesOfWhatCarriesIt(Type attribute)
        {
            // Arrange / Act
            var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>();

            // Assert
            Assert.IsNotNull(usage);
            Assert.IsFalse(usage.Inherited,
                           $"{attribute.Name} must not reach a subclass: the analyzer and the manifest generator read declared attributes only, " +
                           "so an inherited mark makes reflection the one reader that disagrees.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.8")]
        public void LeaveSubclassOfPublishedTypeUnmarked()
        {
            // Arrange - the shape measured in the tree: Vion.Dale.Sdk.Modbus.Rtu.ModbusRtu derives from the
            // published LogicBlockContractBase and is plumbing. The base and the derived type here stand in
            // for that pair, so the claim is proven without this project depending on the Modbus packages.

            // Act
            var onBase = typeof(PublishedBase).GetCustomAttribute<PublicApiAttribute>();
            var onDerived = typeof(PlumbingDerivedFromPublishedBase).GetCustomAttribute<PublicApiAttribute>();

            // Assert - the pair disagrees, which is the whole point: reflection's default would return the
            // same non-null attribute for both and no test reading it could tell them apart.
            Assert.IsNotNull(onBase, "The base carries the mark it declares.");
            Assert.IsNull(onDerived, "A subclass declares no mark of its own and must not inherit one.");
        }

        // DALE013 reads the compiler's documentation XML, and this project sets no
        // GenerateDocumentationFile - so GetDocumentationCommentXml() is empty for every type in it and
        // the diagnostic fires on any [PublicApi] here however documented. Measured: 1 occurrence, and 0
        // under -p:GenerateDocumentationFile=true. The summary below is real; turning the doc file on for
        // a test project of this size is a change to what the whole project warns about, so the entry is
        // in the finding ledger instead.
#pragma warning disable DALE013 // the project generates no doc XML, so DALE013 cannot see this summary
        /// <summary>A published type, standing in for <c>LogicBlockContractBase</c>.</summary>
        [PublicApi]
        private class PublishedBase
        {
        }
#pragma warning restore DALE013

        /// <summary>Plumbing deriving from it, standing in for <c>ModbusRtu</c>.</summary>
        private sealed class PlumbingDerivedFromPublishedBase : PublishedBase
        {
        }
    }
}