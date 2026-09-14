using System.Runtime.CompilerServices;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Web.Services
{
    /// <summary>
    ///     The web port of one built host: the port a supervisor pins it to, and the port its web host bound. The
    ///     runner holds only the <see cref="IDevHost" /> and the web host only its injected services, and the
    ///     control surface is the one object both reach, so the record is keyed on it.
    /// </summary>
    internal sealed class WebUiBinding
    {
        private static readonly ConditionalWeakTable<IDevHostControl, WebUiBinding> Bindings = new();

        /// <summary>Set by the supervisor for every generation after the first: bind exactly this port, or fail.</summary>
        public int? PinnedPort { get; set; }

        /// <summary>The port the web host is serving on, once it bound one.</summary>
        public int? BoundPort { get; set; }

        public static WebUiBinding For(IDevHostControl control)
        {
            return Bindings.GetOrCreateValue(control);
        }
    }
}