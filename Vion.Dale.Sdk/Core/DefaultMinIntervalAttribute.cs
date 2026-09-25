using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     The <c>MinInterval</c> a service property or measuring point of this struct type is published at when
    ///     neither the member's own attribute nor its service interface's assigns one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Put it on a struct that changes on nearly every update — a diagnostics summary whose counters move with
    ///         every transaction. The dedup floor never holds such a value back and a struct has no deadband, so its
    ///         interval is the only thing that limits how often it is published; this makes that interval slow unless
    ///         the member asks for another.
    ///     </para>
    ///     <para>
    ///         A <c>MinInterval</c> assigned on the member's attribute, or on the attribute of the service-interface
    ///         property it implements, wins over this default. It applies to a member of the struct type and of its
    ///         nullable form, never to an array of it. The value is a duration in the same format as
    ///         <c>MinInterval</c>.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     [DefaultMinInterval("30s")]
    ///     public readonly record struct PumpStatistics(
    ///         [StructField(Title = "Starts")] long Starts,
    ///         [StructField(Title = "Run time")] TimeSpan RunTime);
    ///     </code>
    /// </example>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class DefaultMinIntervalAttribute : Attribute
    {
        /// <param name="minInterval">A duration such as <c>"30s"</c>, in the format <c>MinInterval</c> takes.</param>
        public DefaultMinIntervalAttribute(string minInterval)
        {
            MinInterval = minInterval;
        }

        /// <summary>The interval members of this type are published at when none is assigned.</summary>
        public string MinInterval { get; }
    }
}
