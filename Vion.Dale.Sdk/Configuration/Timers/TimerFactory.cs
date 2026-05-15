using System;

namespace Vion.Dale.Sdk.Configuration.Timers
{
    public class TimerFactory : ITimerFactory
    {
        private readonly Action<string, TimeSpan, Action> _addAndStartTimer;

        public TimerFactory(Action<string, TimeSpan, Action> addAndStartAndStartTimer)
        {
            _addAndStartTimer = addAndStartAndStartTimer;
        }

        public void RegisterTimer(string identifier, TimeSpan interval, Action callback)
        {
            _addAndStartTimer(identifier, interval, callback);
        }
    }
}