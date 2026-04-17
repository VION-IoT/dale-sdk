using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public class FunctionInterfaceMetaData
    {
        public string? DefaultName { get; set; }

        public List<string> Tags { get; set; } = [];

        public FunctionInterfaceDependencyMetaData? Dependency { get; set; }

        public Dictionary<string, object> Annotations
        {
            get
            {
                var annotations = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(DefaultName))
                {
                    annotations[nameof(DefaultName)] = DefaultName;
                }

                if (Tags.Count > 0)
                {
                    annotations[nameof(Tags)] = Tags;
                }

                return annotations;
            }
        }

        public class FunctionInterfaceDependencyMetaData
        {
            public required Type Type { get; init; }

            public required Type MatchingType { get; init; }

            public required string DefaultName { get; init; }

            public List<string> Tags { get; init; } = [];

            public required CardinalityType Cardinality { get; init; }

            public required SharingType Sharing { get; init; }

            public required DependencyCreationType CreationType { get; init; }

            public Dictionary<string, object> Annotations
            {
                get
                {
                    var annotations = new Dictionary<string, object>();

                    if (!string.IsNullOrEmpty(DefaultName))
                    {
                        annotations[nameof(DefaultName)] = DefaultName;
                    }

                    if (Tags.Count > 0)
                    {
                        annotations[nameof(Tags)] = Tags;
                    }

                    if (Cardinality != default)
                    {
                        annotations[nameof(Cardinality)] = Cardinality;
                    }

                    if (Sharing != default)
                    {
                        annotations[nameof(Sharing)] = Sharing;
                    }

                    if (CreationType != default)
                    {
                        annotations[nameof(CreationType)] = CreationType;
                    }

                    return annotations;
                }
            }
        }
    }
}