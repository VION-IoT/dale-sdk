using System;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Mqtt
{
    /// <summary>
    ///     The installation topic is process-wide and taken once, so this suite asserts the rule from
    ///     whatever the process already holds rather than from a value of its own — which is itself the
    ///     behaviour under test.
    /// </summary>
    [TestClass]
    public class MqttConfigurationShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.5")]
        public void IgnoreEveryLaterAssignment()
        {
            // Arrange
            MqttTopics.Configure();
            var taken = MqttConfiguration.InstallationTopic;

            // Act
            MqttConfiguration.InstallationTopic = taken + "/something-else";

            // Assert
            Assert.AreEqual(taken, MqttConfiguration.InstallationTopic);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.5")]
        public void RefuseNullInstallationTopic()
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentNullException>(() => MqttConfiguration.InstallationTopic = null!);
        }
    }
}