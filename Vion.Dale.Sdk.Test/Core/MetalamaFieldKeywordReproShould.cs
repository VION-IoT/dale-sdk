using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Regression guard for the Metalama [Observable]-aspect bug where a property setter body
    ///     using the C# 'field' keyword was silently dropped (metalama/Metalama#1644, fixed upstream
    ///     in Metalama.Patterns.Observability 2026.1.18). Both accessor shapes must run their setter
    ///     side effects on assignment. The now-retired DALE029 analyzer previously flagged the pattern.
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
        public void FieldKeywordSetter_RunsSideEffects()
        {
            // Before the 2026.1.18 fix this failed: the [Observable] setter rewrite dropped the body,
            // so SideEffectMarker stayed 0 even though the getter returned the assigned value.
            var sut = new FieldKeywordRepro();
            sut.WithFieldKeyword = 42;
            Assert.AreEqual(1, sut.SideEffectMarker, "field-keyword setter body must run on assignment.");
        }

        private sealed class FieldKeywordRepro : LogicBlockBase
        {
            private int _withBackingField;

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

            public FieldKeywordRepro() : base(new Mock<ILogger>().Object)
            {
            }

            protected override void Ready()
            {
            }
        }
    }
}