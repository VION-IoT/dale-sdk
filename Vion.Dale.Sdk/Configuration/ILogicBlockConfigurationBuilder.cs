using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Configuration.Timers;

namespace Vion.Dale.Sdk.Configuration
{
    internal interface ILogicBlockConfigurationBuilder
    {
        public IContractFactory Contracts { get; }

        public IInterfaceFactory Interfaces { get; }

        public IServiceFactory Services { get; }

        public ITimerFactory Timers { get; }
    }
}