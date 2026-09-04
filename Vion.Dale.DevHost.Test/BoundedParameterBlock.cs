using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Bounds a parameter editor cannot render. The catalog builds an <c>[InstantiationParameter]</c>'s
    ///     editor schema from its paired <c>[ServiceProperty]</c>'s declared bounds; a bound that is not a
    ///     number, or one outside the integer range the schema carries, has no representation there and belongs
    ///     out of the schema rather than in it as a plausible-looking wrong limit.
    ///     <para>
    ///         Two shapes, both accepted by the compiler and judged by no diagnostic: the declaration's own
    ///         defaults are the two infinities, so "finite" is the same test as "declared" — and a
    ///         <c>NaN</c> is neither, while a finite <c>1e30</c> is declared but not carryable.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Bounded parameter")]
    public sealed class BoundedParameterBlock : LogicBlockBase
    {
        /// <summary>A bound that is not a number — it passes an infinity test and converts to a plausible zero.</summary>
        [ServiceProperty(Title = "Not a number", Minimum = double.NaN, Maximum = 10)]
        [InstantiationParameter]
        public int NotANumber { get; init; } = 1;

        /// <summary>A finite bound far outside the integer range the editor schema carries — it saturates.</summary>
        [ServiceProperty(Title = "Out of range", Minimum = 0, Maximum = 1e30)]
        [InstantiationParameter]
        public int OutOfRange { get; init; } = 1;

        /// <summary>Both bounds carryable — the control case.</summary>
        [ServiceProperty(Title = "Carryable", Minimum = 1, Maximum = 12)]
        [InstantiationParameter]
        public int Carryable { get; init; } = 1;

        public BoundedParameterBlock(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}
