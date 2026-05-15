using System;

namespace Vion.Dale.Plugin
{
    /// <summary>
    ///     Thrown when a plugin assembly references a <c>Vion.Dale.Sdk</c> whose MAJOR version differs
    ///     from the SDK version the host runtime has loaded. A differing major version signals a
    ///     binary-incompatible (breaking) SDK change, so the plugin load is failed fast — before any
    ///     reflection over plugin types — rather than risking obscure <see cref="MissingMethodException" />
    ///     / <see cref="TypeLoadException" /> failures deep inside the runtime.
    /// </summary>
    /// <remarks>
    ///     Differing minor/patch versions are NOT a hard failure: those remain warn-and-continue
    ///     (handled by <c>PluginLoadContext.LogDefaultContextLoad</c>). This exception is reserved for
    ///     the unrecoverable major-version case only.
    /// </remarks>
    public sealed class PluginSdkVersionMismatchException : Exception
    {
        public PluginSdkVersionMismatchException(string message) : base(message)
        {
        }

        public PluginSdkVersionMismatchException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
