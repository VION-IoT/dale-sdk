using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The emission knobs one stream of a member resolves to. Each knob is taken on its own from the first
    ///     place that assigned it: the stream's attribute on the implementing property, then the stream's
    ///     attribute on the <c>[ServiceInterface]</c> property, then — for <c>MinInterval</c> only — the value
    ///     type's <see cref="DefaultMinIntervalAttribute" />, then the SDK default. The emission gate and
    ///     introspection both resolve through here, so the policy a member reports is the one it is gated by.
    /// </summary>
    internal sealed class EmissionKnobs : IThrottleConfigured
    {
        /// <summary>The SDK's interval for a stream nothing assigns one to.</summary>
        internal const string DefaultMinInterval = "250ms";

        /// <summary>The property whose attribute assigned <see cref="MinChange" />; <c>null</c> when none did.</summary>
        public PropertyInfo? MinChangeSource { get; }

        /// <summary>
        ///     The value type whose <see cref="DefaultMinIntervalAttribute" /> supplied <see cref="MinInterval" />;
        ///     <c>null</c> otherwise.
        /// </summary>
        public Type? MinIntervalDefaultedBy { get; }

        private EmissionKnobs(string minInterval, string? minChange, bool immediate, PropertyInfo? minChangeSource, Type? minIntervalDefaultedBy)
        {
            MinInterval = minInterval;
            MinChange = minChange;
            Immediate = immediate;
            MinChangeSource = minChangeSource;
            MinIntervalDefaultedBy = minIntervalDefaultedBy;
        }

        public string MinInterval { get; }

        public string? MinChange { get; }

        public bool Immediate { get; }

        /// <summary>
        ///     Resolves the knobs of <paramref name="stream" /> for a member implemented by
        ///     <paramref name="implementation" /> and bound through <paramref name="serviceInterface" /> (the same
        ///     property, or <c>null</c>, for a member no interface declares). Returns <c>null</c> when neither
        ///     property carries the stream's attribute: such a stream has no policy to apply.
        /// </summary>
        public static EmissionKnobs? Resolve(PropertyInfo implementation, PropertyInfo? serviceInterface, ServiceElementStream stream, Type valueType)
        {
            var implementationAttribute = AttributeOf(implementation, stream);

            // Reading the one property twice would let its unassigned knobs answer for the interface.
            var interfaceAttribute = serviceInterface == null || serviceInterface == implementation ? null : AttributeOf(serviceInterface, stream);
            if (implementationAttribute == null && interfaceAttribute == null)
            {
                return null;
            }

            var minIntervalSource = Assigning(EmissionKnob.MinInterval, implementationAttribute, interfaceAttribute);
            var minChangeSource = Assigning(EmissionKnob.MinChange, implementationAttribute, interfaceAttribute);
            var immediateSource = Assigning(EmissionKnob.Immediate, implementationAttribute, interfaceAttribute);

            var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            var typeDefault = minIntervalSource == null ? underlyingType.GetCustomAttribute<DefaultMinIntervalAttribute>(false) : null;

            return new EmissionKnobs(minIntervalSource?.MinInterval ?? typeDefault?.MinInterval ?? DefaultMinInterval,
                                     minChangeSource?.MinChange,
                                     immediateSource?.Immediate ?? false,
                                     minChangeSource == null ? null : minChangeSource == implementationAttribute ? implementation : serviceInterface,
                                     typeDefault == null ? null : underlyingType);
        }

        private static IEmissionAttribute? AttributeOf(PropertyInfo property, ServiceElementStream stream)
        {
            // Read the attribute belonging to the stream being resolved, never the sibling's. A dual-annotated
            // member declares two independent policies; falling back to the other attribute would hand the
            // measuring point the property's interval and silently ignore its own.
            var attributeType = stream == ServiceElementStream.Property ? typeof(ServicePropertyAttribute) : typeof(ServiceMeasuringPointAttribute);
            return (IEmissionAttribute?)property.GetCustomAttribute(attributeType, true);
        }

        private static IEmissionAttribute? Assigning(EmissionKnob knob, IEmissionAttribute? implementationAttribute, IEmissionAttribute? interfaceAttribute)
        {
            if (implementationAttribute != null && (implementationAttribute.Assigned & knob) != 0)
            {
                return implementationAttribute;
            }

            return interfaceAttribute != null && (interfaceAttribute.Assigned & knob) != 0 ? interfaceAttribute : null;
        }
    }
}