using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Block whose service properties live on a nested interface-bound member — the "multi charging
    ///     point" pattern from Vion.Examples.Energy. Guards the nested-service write path: the HTTP set
    ///     route must resolve the CLR property on the MEMBER's type (here <see cref="NestedPoint" />), not
    ///     the block type, to decode the JSON value before it reaches the service binder.
    /// </summary>
    [LogicBlock(Name = "MultiPoint")]
    public class MultiPointBlock : LogicBlockBase
    {
        [LogicBlockInterfaceBinding(typeof(ISink))]
        public NestedPoint PointA { get; }

        public MultiPointBlock(ILogger logger) : base(logger)
        {
            PointA = new NestedPoint();
        }

        protected override void Ready()
        {
        }

        public class NestedPoint : ISink
        {
            // Unique name across the block's services on purpose: the flat name-keyed control surfaces
            // (GetAllProperties / the state route) collapse duplicate member names across services.
            [ServiceProperty(Title = "Nested threshold")]
            public double NestedThreshold { get; set; } = 1.0;

            public PollLink.Ack HandleRequest(PollLink.Poll request)
            {
                return new PollLink.Ack();
            }
        }
    }
}