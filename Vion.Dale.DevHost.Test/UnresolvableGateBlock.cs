using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A block whose contract binding is gated on an <c>[InstantiationParameter]</c> that has no value
    ///     yet — an optional parameter the operator has not chosen. The gate is well formed and every check
    ///     that reads it syntactically passes; only EVALUATING it fails, because the predicate profile treats a
    ///     null reference as a hard error rather than as false.
    ///     <para>
    ///         That is the one input the development host's live view is fail-OPEN for
    ///         (<c>AC-GATE-012.5</c>): it leaves the member visible and logs, because the running block is the
    ///         strict gate and a development host that hid a member on a predicate it could not read would be
    ///         lying about the network. Read through the PRE-START configuration, where the resolver runs and
    ///         no block has bound.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Unresolvable gate")]
    public sealed class UnresolvableGateBlock : LogicBlockBase
    {
        /// <summary>
        ///     The gate. It resolves — <c>Count</c> is a declared parameter, so the compile-time check and the
        ///     bind-time one both pass — and it cannot be EVALUATED while no value has been chosen: the
        ///     parameter's own default is null, and the predicate profile treats a null reference as a hard
        ///     error rather than as false.
        /// </summary>
        public const string GateOnAnUnchosenParameter = "Count >= 2";

        [ServiceProperty(Title = "Count", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int? Count { get; init; }

        [ServiceProviderContractBinding(DefaultName = "Demand")]
        [IncludedWhen(GateOnAnUnchosenParameter)]
        public SmokeHost.Contracts.IGridDemand? Demand { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Ungated")]
        public SmokeHost.Contracts.IGridDemand? Ungated { get; private set; }

        public UnresolvableGateBlock(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}