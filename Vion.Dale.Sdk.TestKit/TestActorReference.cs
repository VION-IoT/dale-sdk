using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Mock actor reference implementation for testing without a real actor system.
    /// </summary>
    [PublicApi]
    public sealed class TestActorReference : IActorReference
    {
        private readonly string _name;

        public TestActorReference(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return $"TestActorRef({_name})";
        }
    }
}