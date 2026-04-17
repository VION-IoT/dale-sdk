using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declare a service interface as a C# interface. Use the ServiceProperty and ServiceMeasuringPoint attributes on
    ///     properties.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceInterfaceAttribute : Attribute
    {
    }
}