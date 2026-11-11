using System;

namespace Vion.Dale.Plugin
{
    /// <summary>
    ///     The two <c>Vion.Dale.Sdk</c> versions a logic-block library carries, each as
    ///     <c>MAJOR.MINOR.PATCH</c> or <c>null</c> when unknown. They are stored as facts: the library's
    ///     minimum is <see cref="DeclaredMinimum" /> when declared, else <see cref="Target" />.
    /// </summary>
    /// <param name="Target">The highest <c>Vion.Dale.Sdk</c> version any assembly of the library was built against.</param>
    /// <param name="DeclaredMinimum">
    ///     The minimum the library's author declared with the <c>MinimumDaleSdkVersion</c> MSBuild
    ///     property.
    /// </param>
    public sealed record LibraryDaleSdkVersions(Version? Target, Version? DeclaredMinimum);
}