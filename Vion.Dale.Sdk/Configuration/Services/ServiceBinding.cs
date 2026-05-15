using System;
using System.Reflection;
using Vion.Contracts.TypeRef;

namespace Vion.Dale.Sdk.Configuration.Services
{
    /// <summary>
    ///     Common binding class used for both service properties and measuring points
    /// </summary>
    public class ServiceBinding
    {
        /// <summary>
        ///     The source object containing the property to bind (INotifyPropertyChanged)
        /// </summary>
        public object Source { get; init; } = null!;

        /// <summary>
        ///     The full path to the property on the source object (supports nested properties with dot notation)
        /// </summary>
        public string SourcePropertyName { get; init; } = null!;

        /// <summary>
        ///     The root property name in the source object (the first segment of SourcePropertyName)
        /// </summary>
        public string RootSourcePropertyName { get; init; } = null!;

        /// <summary>
        ///     The PropertyInfo of the root source property (for accessing attributes like BindPropertyAttribute)
        /// </summary>
        public PropertyInfo RootSourcePropertyInfo { get; init; } = null!;

        /// <summary>
        ///     The type of the target property (used for type conversions like enum handling)
        /// </summary>
        public Type TargetPropertyType { get; init; } = null!;

        /// <summary>
        ///     The compiled getter function to retrieve the property value from the source object
        /// </summary>
        public Func<object, object?> Getter { get; init; } = null!;

        /// <summary>
        ///     the compiled setter action to set the property value on the source object (null if read-only)
        /// </summary>
        public Action<object, object?>? Setter { get; init; }

        public string ServicePropertyName { get; init; } = null!; // for the parser

        /// <summary>
        ///     Per-property metadata document — Schema (data shape), Presentation (UI hints), Runtime (dale flags).
        ///     Populated at binding-registration time by ServiceBuilderBase / ServiceDeclarationBase via
        ///     <see cref="Vion.Dale.Sdk.Introspection.PropertyMetadataBuilder" />. The codec uses
        ///     <c>Metadata.Schema.Type</c> for FB encode/decode dispatch.
        /// </summary>
        public PropertyMetadata Metadata { get; init; } = null!;
    }
}
