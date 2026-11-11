using System;

namespace Vion.Dale.Plugin
{
    /// <summary>
    ///     A library needs a newer <c>Vion.Dale.Sdk</c> than the host has loaded, within the same major. The
    ///     library still loads; a block that calls into SDK surface the host lacks fails when it does.
    /// </summary>
    /// <param name="Minimum">The library's minimum, <c>MAJOR.MINOR.PATCH</c>.</param>
    /// <param name="IsDeclared">
    ///     <c>true</c> when the library's author declared the minimum; <c>false</c> when it is the version
    ///     the library was built against.
    /// </param>
    /// <param name="Host">The <c>Vion.Dale.Sdk</c> version the host has loaded, <c>MAJOR.MINOR.PATCH</c>.</param>
    public sealed record UnmetDaleSdkMinimum(Version Minimum, bool IsDeclared, Version Host);
}
