using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    [InternalApi]
    public readonly record struct ServiceIdentifier(string Id)
    {
        // Implicit conversion: ServiceIdentifier → string
        public static implicit operator string(ServiceIdentifier serviceIdentifier)
        {
            return serviceIdentifier.Id;
        }

        // Implicit conversion: string → ServiceIdentifier
        public static implicit operator ServiceIdentifier(string id)
        {
            return new ServiceIdentifier(id);
        }

        // ToString override string interpolation, logging, etc.
        public override string ToString()
        {
            return Id;
        }
    }
}