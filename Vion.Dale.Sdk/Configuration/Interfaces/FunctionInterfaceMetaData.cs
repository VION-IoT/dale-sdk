using System.Collections.Generic;
using Vion.Contracts.Conventions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public class FunctionInterfaceMetaData
    {
        public string? DefaultName { get; set; }

        public List<string> Tags { get; set; } = [];

        public LinkMultiplicity Multiplicity { get; set; } = LinkMultiplicity.ZeroOrMore;

        /// <summary>
        ///     RFC 0016 config-time inclusion predicate for this interface binding (<c>[IncludedWhen]</c>),
        ///     or <c>null</c> when the binding is unconditional. Emitted into the definition-view annotation
        ///     bag under <see cref="LogicBlockWiringConventions.IncludedWhenAnnotationKey" />.
        /// </summary>
        public string? IncludedWhen { get; set; }

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

                if (!string.IsNullOrEmpty(IncludedWhen))
                {
                    annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey] = IncludedWhen;
                }

                return annotations;
            }
        }
    }
}