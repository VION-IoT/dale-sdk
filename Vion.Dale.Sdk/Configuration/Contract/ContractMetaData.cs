using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    public class ContractMetaData
    {
        public string? DefaultName { get; set; }

        public List<string> Tags { get; set; } = [];

        public CardinalityType Cardinality { get; set; }

        public SharingType Sharing { get; set; }

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

                return annotations;
            }
        }
    }
}
