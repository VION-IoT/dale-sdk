using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Block with TWO nested interface-bound services declaring the SAME property name — the duplicate
    ///     member name the flat per-block name map collapses last-service-wins. Guards the service-qualified
    ///     control overloads and the RFC 0006 revision 5 name-path ambiguity rule (a two-segment
    ///     <c>DualPoint.Limit</c> must be rejected as ambiguous; <c>DualPoint.PointA.Limit</c> resolves).
    /// </summary>
    [LogicBlock(Name = "DualPoint")]
    public class DualPointBlock : LogicBlockBase
    {
        [LogicBlockInterfaceBinding(typeof(ISink))]
        public Point PointA { get; }

        [LogicBlockInterfaceBinding(typeof(ISink))]
        public Point PointB { get; }

        public DualPointBlock(ILogger logger) : base(logger)
        {
            PointA = new Point();
            PointB = new Point();
        }

        protected override void Ready()
        {
        }

        public class Point : ISink
        {
            [ServiceProperty(Title = "Limit")]
            public double Limit { get; set; } = 1.0;

            public PollLink.Ack HandleRequest(PollLink.Poll request)
            {
                return new PollLink.Ack();
            }
        }
    }
}