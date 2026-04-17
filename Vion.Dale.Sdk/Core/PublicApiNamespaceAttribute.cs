using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    /// Declares a namespace where public types are expected to be marked with
    /// <see cref="PublicApiAttribute"/> or <see cref="InternalApiAttribute"/>.
    /// Applied at the assembly level. The PublicApiDocumentationAnalyzer uses these
    /// declarations to warn about unmarked public types.
    /// </summary>
    [InternalApi]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class PublicApiNamespaceAttribute : Attribute
    {
        public string Namespace { get; }
        public PublicApiNamespaceAttribute(string ns) => Namespace = ns;
    }
}
