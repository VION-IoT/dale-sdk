using System.Collections.Generic;
using Vion.Contracts.Conventions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    public class ContractMetaData
    {
        public string? DefaultName { get; set; }

        public List<string> Tags { get; set; } = [];

        public LinkMultiplicity Multiplicity { get; set; } = LinkMultiplicity.ZeroOrMore;

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

                if (Multiplicity != LinkMultiplicity.ZeroOrMore)
                {
                    annotations[LogicBlockWiringConventions.MultiplicityAnnotationKey] = LinkMultiplicityWire.ToToken(Multiplicity);
                }

                return annotations;
            }
        }
    }
}
