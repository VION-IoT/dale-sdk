using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>
    ///     The installation topic every suite that parses one needs. It is process-wide and write-once, so the
    ///     first suite to call this wins and every later call is the no-op the configuration promises — which
    ///     is why callers build their topics from <see cref="MqttConfiguration.InstallationTopic" /> rather
    ///     than from the value passed here.
    /// </summary>
    public static class MqttTopics
    {
        private const string Installation = "vion/test-installation";

        public static void Configure()
        {
            MqttConfiguration.InstallationTopic = Installation;
        }
    }
}