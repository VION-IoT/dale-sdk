using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;
using Vion.Examples.Gating.Contracts;

namespace Vion.Examples.Gating.LogicBlocks
{
    /// <summary>
    ///     The <b>second block</b>. It binds <c>IChargeController</c> at the class level (unconditional) and is
    ///     wired to a station's charge points through topology <c>interfaceMappings</c>. Because each charge
    ///     point's interface binding is gated by the station's <c>ChargePointCount</c>, a mapping to
    ///     <c>Point2</c>/<c>Point3</c> is an <b>included / excluded interface mapping</b> depending on the count
    ///     — and a mapping to a gated-out point is skipped (the block stays up), so it is absent from the
    ///     dashboard's wiring view.
    /// </summary>
    [LogicBlock(Name = "Charge Controller", Icon = "dashboard-3-line")]
    [LogicBlockInterfaceBinding(typeof(IChargeController), Multiplicity = LinkMultiplicity.ZeroOrMore)]
    public class ChargeController : LogicBlockBase, IChargeController
    {
        [ServiceProperty(Title = "Target current", Unit = "A", Minimum = 6, Maximum = 32)]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double TargetCurrent { get; set; } = 16.0;

        [ServiceProperty(Title = "Linked charge points")]
        [Presentation(Group = PropertyGroup.Status)]
        public int LinkedChargePoints { get; private set; }

        public ChargeController(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, ChargeLink.ChargeState response)
        {
            // The point echoed its charge state; nothing more to do in this demo.
        }

        [Timer(1)]
        public void OnTick()
        {
            var points = this.GetLinkedChargePoints();
            LinkedChargePoints = points.Count;
            foreach (var point in points)
            {
                this.SendRequest(point, new ChargeLink.SetCurrentLimit(TargetCurrent));
            }
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}