using Vion.Dale.Sdk.TestKit;
using Xunit;

namespace VionIotLibraryTemplate.Test
{
    public class HelloWorldShould
    {
        [Fact]
        public void Greet_WhenCalled_IncrementsTimesGreeted()
        {
            // Arrange
            var helloWorld = new HelloWorld(LogicBlockTestHelper.CreateLoggerMock().Object);
            helloWorld.InitializeForTest(); // Initialize the logic block for testing

            var timesGreetedBefore = helloWorld.TimesGreeted;

            // Act
            helloWorld.Greet();

            // Assert
            var timesGreetedAfter = helloWorld.TimesGreeted;
            Assert.Equal(timesGreetedBefore + 1, timesGreetedAfter);
        }
    }
}