namespace Vion.Dale.Sdk.Configuration.Services
{
    public interface IServiceFactory
    {
        ServiceBuilder CreateService(string serviceIdentifier);
    }
}