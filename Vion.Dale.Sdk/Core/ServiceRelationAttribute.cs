using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Defines a relation to another service interface.
    ///     A matching declaration (same RelationType, opposite Direction) must exist on the other service interface.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceRelationAttribute : Attribute
    {
        /// <summary>
        ///     The identifier of the relation. Must be the same for the inwards and outwards side of the declaration.
        /// </summary>
        public string RelationType { get; }

        /// <summary>
        ///     Side of the relation this service interface represents. (start or end of the arrow)
        /// </summary>
        public ServiceRelationDirection Direction { get; }

        /// <summary>
        ///     Function interface type to match with the relation.
        /// </summary>
        public Type FunctionInterfaceType { get; }

        public string? DefaultName { get; }

        public Dictionary<string, object> Annotations
        {
            get
            {
                var annotations = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(DefaultName))
                {
                    annotations[nameof(DefaultName)] = DefaultName;
                }

                return annotations;
            }
        }

        public ServiceRelationAttribute(string relationType, ServiceRelationDirection direction, Type functionInterfaceType, string? defaultName = null)
        {
            RelationType = relationType;
            Direction = direction;
            FunctionInterfaceType = functionInterfaceType;
            DefaultName = defaultName;
        }
    }

    /// <summary>
    ///     Specifies the direction of a service relation (inwards or outwards).
    /// </summary>
    [PublicApi]
    public enum ServiceRelationDirection
    {
        /// <summary>
        ///     This service is the target (end) of the relation.
        /// </summary>
        Inwards,

        /// <summary>
        ///     This service is the source (start) of the relation.
        /// </summary>
        Outwards,
    }
}