namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public interface IInterfaceFactory
    {
        public TInterface Create<TInterface, TImplementation>(string identifier, TImplementation implementation);
    }
}