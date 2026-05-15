using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public class ServiceRelationInfo
    {
        /// <summary>
        ///     The relation type identifier (e.g. from the ServiceRelationAttribute)
        /// </summary>
        public required string RelationType { get; init; }

        /// <summary>
        ///     The interface identifier that this service relates to
        /// </summary>
        public required string InterfaceIdentifier { get; init; }

        /// <summary>
        ///     The interface type full name
        /// </summary>
        public required string InterfaceTypeFullName { get; init; }

        /// <summary>
        ///     The direction of this relation (Inwards or Outwards)
        /// </summary>
        public ServiceRelationDirection Direction { get; init; }

        /// <summary>
        ///     Additional annotations for the UI
        /// </summary>
        public Dictionary<string, object> Annotations { get; init; } = [];
    }
}
