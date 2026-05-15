using System;

namespace Vion.Dale.Sdk.Configuration.Timers
{
    public interface ITimerFactory
    {
        void RegisterTimer(string identifier, TimeSpan interval, Action callback);
    }
}