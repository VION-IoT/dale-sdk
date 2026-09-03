using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Services
{
    /// <summary>
    ///     The single authority on what makes a type <i>service-bearing</i> — i.e. whether binding it
    ///     produces a service node. Two passes depend on the same answer and must never drift:
    ///     <see cref="DeclarativeServiceBinder" /> uses it to decide whether a property becomes a component
    ///     service, and <c>DeclarativeInterfaceBinder</c> uses it to decide whether a
    ///     property-bound interface endpoint has an owning service to attach its relation half to.
    /// </summary>
    internal static class ServiceSurface
    {
        /// <summary>
        ///     Whether <paramref name="type" /> carries a service surface: it implements at least one
        ///     <see cref="ServiceInterfaceAttribute" /> interface, or declares
        ///     <see cref="ServicePropertyAttribute" /> / <see cref="ServiceMeasuringPointAttribute" />
        ///     members of its own.
        /// </summary>
        public static bool IsServiceBearing(Type type)
        {
            return GetImplementedServiceInterfaces(type).Length > 0 || HasServicePropertiesOrMeasuringPoints(type);
        }

        /// <summary>The <see cref="ServiceInterfaceAttribute" />-marked interfaces <paramref name="type" /> implements.</summary>
        public static Type[] GetImplementedServiceInterfaces(Type type)
        {
            return type.GetInterfaces().Where(i => i.GetCustomAttribute<ServiceInterfaceAttribute>() != null).ToArray();
        }

        /// <summary>Whether <paramref name="type" /> declares any service property or measuring point.</summary>
        public static bool HasServicePropertiesOrMeasuringPoints(Type type)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<ServicePropertyAttribute>() != null || property.GetCustomAttribute<ServiceMeasuringPointAttribute>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}