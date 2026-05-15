namespace Vion.Dale.Sdk.Configuration.Services
{
    internal interface IServiceFactory
    {
        ServiceBuilder CreateService(string serviceIdentifier);
    }
}