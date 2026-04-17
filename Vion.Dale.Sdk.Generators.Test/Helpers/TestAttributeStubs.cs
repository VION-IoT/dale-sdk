// Minimal attribute stubs for analyzer test compilations.
// These mirror the real Vion.Dale.Sdk attributes' shapes so that test source code compiles
// and the analyzers can match them by fully-qualified name.

using System;

namespace Vion.Dale.Sdk.Core
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TimerAttribute : Attribute
    {
        public double IntervalSeconds { get; }

        public string? Identifier { get; }

        public TimerAttribute(double intervalSeconds, string? identifier = null)
        {
            IntervalSeconds = intervalSeconds;
            Identifier = identifier;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ServiceProviderContractAttribute : Attribute
    {
        public string? Identifier { get; }

        public ServiceProviderContractAttribute(string? identifier = null)
        {
            Identifier = identifier;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ServicePropertyAttribute : Attribute
    {
        public string? DefaultName { get; set; }

        public string? Unit { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ServiceMeasuringPointAttribute : Attribute
    {
        public string? DefaultName { get; set; }

        public string? Unit { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class PersistentAttribute : Attribute
    {
        public bool Exclude { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class StatusIndicatorAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ContractAttribute : Attribute
    {
        public required string BetweenInterface { get; init; }

        public required string AndInterface { get; init; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public class CommandAttribute : Attribute
    {
        public required string From { get; init; }

        public required string To { get; init; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public class StateUpdateAttribute : Attribute
    {
        public required string From { get; init; }

        public required string To { get; init; }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public class RequestResponseAttribute : Attribute
    {
        public required string From { get; init; }

        public required string To { get; init; }

        public required Type ResponseType { get; init; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct)]
    public class PublicApiAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct)]
    public class InternalApiAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class PublicApiNamespaceAttribute : Attribute
    {
        public string Namespace { get; }

        public PublicApiNamespaceAttribute(string ns)
        {
            Namespace = ns;
        }
    }
}

namespace Vion.Dale.Sdk.Configuration.Contract
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceProviderContractTypeAttribute : Attribute
    {
    }
}