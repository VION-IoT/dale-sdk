using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Controls persistence behavior for properties.
    ///     - On writable service properties: Use [Persistent(Exclude = true)] to opt-out
    ///     - On other properties: Use [Persistent] to opt-in
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class PersistentAttribute : Attribute
    {
        /// <summary>
        ///     Set to true to exclude a writable service property from persistence
        /// </summary>
        public bool Exclude { get; set; }
    }
}