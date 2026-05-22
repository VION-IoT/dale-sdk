using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Runtime watchdog for the Metalama [Observable]-aspect bug where setter bodies that use
    ///     the C# 13 'field' keyword are silently dropped. The companion <c>DALE029</c> analyzer
    ///     prevents the antipattern at edit time; this test exists so a future Metalama upgrade
    ///     can be verified end-to-end — remove the <c>[Ignore]</c> on <c>FieldKeywordSetter_RunsSideEffects</c>
    ///     and watch it go green when the upstream aspect learns the C# 13 field keyword.
    /// </summary>
    [TestClass]
    public class MetalamaFieldKeywordReproShould
    {
        [TestMethod]
        public void ExplicitBackingFieldSetter_RunsSideEffects()
        {
            var sut = new FieldKeywordRepro();
            sut.WithBackingField = 42;
            Assert.AreEqual(1, sut.SideEffectMarker, "Explicit backing field setter body must run on assignment (positive control for the watchdog below).");
        }

        [TestMethod]
        [Ignore("Documents the Metalama [Observable] + C# 13 'field' keyword bug. Verified still broken in Metalama.Patterns.Observability 2026.0.21 and 2026.0.23. DALE029 analyzer guards against the antipattern in user code; re-enable this test after a future Metalama upgrade to verify the upstream fix.")]
        public void FieldKeywordSetter_RunsSideEffects()
        {
            // Currently failing: SideEffectMarker stays at 0 because the [Observable] aspect's
            // setter rewrite drops the body. The assignment APPEARS to succeed (getter returns
            // the assigned value because the synthesized backing field IS updated) but the
            // user-written side effects vanish.
            var sut = new FieldKeywordRepro();
            sut.WithFieldKeyword = 42;
            Assert.AreEqual(1, sut.SideEffectMarker, "field-keyword setter body must run on assignment.");
        }

#pragma warning disable DALE029 // Intentional antipattern under test — see [Ignore] note above.
        private sealed class FieldKeywordRepro : LogicBlockBase
        {
            private int _withBackingField;

            public FieldKeywordRepro() : base(new Mock<ILogger>().Object)
            {
            }

            [ServiceProperty]
            public int WithBackingField
            {
                get => _withBackingField;
                set
                {
                    _withBackingField = value;
                    SideEffectMarker++;
                }
            }

            [ServiceProperty]
            public int WithFieldKeyword
            {
                get;
                set
                {
                    field = value;
                    SideEffectMarker++;
                }
            }

            public int SideEffectMarker { get; private set; }

            protected override void Ready()
            {
            }
        }
#pragma warning restore DALE029
    }
}
