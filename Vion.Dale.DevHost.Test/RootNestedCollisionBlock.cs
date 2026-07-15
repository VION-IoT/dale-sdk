using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A block whose ROOT service property shares its NAME with a property on each of its nested
    ///     interface-bound components — the DF-47 collision (a standard telemetry name such as
    ///     <c>ActivePowerTotalKw</c> carried by a buffer's own surface AND by its charge points). The flat
    ///     per-block name map collapses the shared name last-service-wins onto a component, so a bare
    ///     single-property read returned a component's value (0) instead of the root's, and a dotted
    ///     <c>PointX.SharedPower</c> path returned null. Guards: (a) a bare read of the shared name resolves
    ///     to the ROOT service; (b) a <c>PointX.SharedPower</c> dotted read resolves the specific component.
    /// </summary>
    [LogicBlock(Name = "collide")]
    public class RootNestedCollisionBlock : LogicBlockBase
    {
        [LogicBlockInterfaceBinding(typeof(ISink))]
        public Point PointA { get; }

        [LogicBlockInterfaceBinding(typeof(ISink))]
        public Point PointB { get; }

        // Root service property (declared on the block type). Shares its name with Point.SharedPower below;
        // set to a distinctive non-default value in Ready so the leading-edge publish lands under the ROOT
        // service id — a component's default 0 must not shadow it on a bare read.
        [ServiceProperty(Title = "Shared power")]
        public double SharedPower { get; private set; }

        public RootNestedCollisionBlock(ILogger logger) : base(logger)
        {
            PointA = new Point();
            PointB = new Point();
        }

        protected override void Ready()
        {
            SharedPower = -40.0;
        }

        /// <summary>A nested interface-bound component whose own <see cref="SharedPower" /> collides with the root's.</summary>
        public class Point : ISink
        {
            [ServiceProperty(Title = "Shared power")]
            public double SharedPower { get; set; }

            public PollLink.Ack HandleRequest(PollLink.Poll request)
            {
                return new PollLink.Ack();
            }
        }
    }
}
