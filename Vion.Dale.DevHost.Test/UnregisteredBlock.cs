using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A logic block that is deliberately absent from every <c>IConfigureServices</c> in this assembly —
    ///     the fixture for the "topology names a block whose <c>AddTransient&lt;T&gt;()</c> line is missing"
    ///     regression (VION-66). Registration here is hand-written (nothing in this repo generates it), so
    ///     leaving this type out of <see cref="TestDependencyInjection" /> is what reproduces the fault;
    ///     do NOT add it there.
    /// </summary>
    [LogicBlock(Name = "Unregistered")]
    public class UnregisteredBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        public UnregisteredBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A second never-registered block — proves the failure lists EVERY offender, not just the first.</summary>
    [LogicBlock(Name = "SecondUnregistered")]
    public class SecondUnregisteredBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        public SecondUnregisteredBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}