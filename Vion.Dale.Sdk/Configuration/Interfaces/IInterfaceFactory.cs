namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public interface IInterfaceFactory
    {
        public TInterface Create<TInterface, TImplementation>(string identifier, TImplementation implementation);

        /// <summary>
        ///     Registers an endpoint that describes itself but dispatches nothing — the definition view's path
        ///     for a binding whose component is null. Everything <see cref="Create{TInterface,TImplementation}" />
        ///     does except handing the implementation to the generated extension registration, which refuses a
        ///     null.
        /// </summary>
        public TInterface Describe<TInterface, TImplementation>(string identifier);
    }
}