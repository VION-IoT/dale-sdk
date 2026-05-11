using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Configuration.Timers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration
{
    public class LogicBlockConfigurationBuilder : ILogicBlockConfigurationBuilder
    {
        public LogicBlockConfigurationBuilder(Action<string, LogicBlockContractBase> addContract,
                                              Action<string, LogicSenderInterfaceBase> addInterface,
                                              ServiceBinder serviceBinder,
                                              Action<string, TimeSpan, Action> addTimerCallback,
                                              Func<LogicBlockId> logicBlockId,
                                              IActorContext actorContext,
                                              Action<IActorContext, string, TimeSpan> scheduleNextTimerTick,
                                              IServiceProvider serviceProvider)
        {
            Contracts = new ContractFactory(addContract, actorContext, serviceProvider);
            Interfaces = new InterfaceFactory(addInterface, logicBlockId, actorContext, new LoggerFactory());

            Services = serviceBinder;

            Timers = new TimerFactory((identifier, interval, callback) =>
                                      {
                                          addTimerCallback.Invoke(identifier, interval, callback);
                                          scheduleNextTimerTick(actorContext, identifier, interval);
                                      });
        }

        /// <inheritdoc />
        public IContractFactory Contracts { get; }

        /// <inheritdoc />
        public IInterfaceFactory Interfaces { get; }

        /// <inheritdoc />
        public IServiceFactory Services { get; }

        /// <inheritdoc />
        public ITimerFactory Timers { get; }
    }
}
