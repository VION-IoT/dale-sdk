namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     The analyzer-wiring probe for <c>Vion.Dale.Sdk.TestKit</c>: a public type in the kit's declared published
    ///     namespace carrying neither surface mark, which DALE014 must reject. Linked into that project only
    ///     under <c>-p:DaleAnalyzerWiringProbe=true</c>, where the build MUST fail. It is nobody's source
    ///     file otherwise. <c>AnalyzerWiringShould</c> runs it.
    /// </summary>
    public class TestKitPublicApiWiringProbe
    {
    }
}
