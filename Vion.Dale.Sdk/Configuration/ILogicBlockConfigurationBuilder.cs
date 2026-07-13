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

        /// <summary>
        ///     RFC 0016: whether the binders resolve <c>[IncludedWhen]</c> gates (<see cref="BindingMode.Live" />)
        ///     or bind the full definition set and record the predicates (<see cref="BindingMode.Definition" />).
        /// </summary>
        public BindingMode Mode { get; }
    }
}