using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>
    ///     The measurement around one actor's handler: what the observer is told, and what it cannot affect.
    ///     Duration runs on the injected clock, so the assertion is an exact span rather than a bound.
    /// </summary>
    [TestClass]
    public sealed class ObservedHandlerShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.1")]
        public async Task NotifyObserverOfSuccessfulHandlerWithItsDuration()
        {
            // Arrange
            var clock = new FakeTimeProvider();
            var observer = new Mock<IActorMessageObserver>();
            var message = new object();

            // Act
            await ObservedHandler.RunAsync(observer.Object,
                                           "a",
                                           message,
                                           clock,
                                           () =>
                                           {
                                               clock.Advance(TimeSpan.FromMilliseconds(7));
                                               return Task.CompletedTask;
                                           });

            // Assert
            observer.Verify(observer => observer.OnHandled("a", message, TimeSpan.FromMilliseconds(7), null),
                            Times.Once,
                            "The duration is measured on the registered clock, which is what makes it exact under a stepped host.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.1")]
        public async Task NotifyObserverOfThrowingHandlerAndRethrow()
        {
            // Arrange
            var observer = new Mock<IActorMessageObserver>();
            var message = new object();

            // Act / Assert
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await ObservedHandler.RunAsync(observer.Object,
                                                                                                                  "a",
                                                                                                                  message,
                                                                                                                  new FakeTimeProvider(),
                                                                                                                  () => Task.FromException(new InvalidOperationException())));
            observer.Verify(observer => observer.OnHandled("a", message, It.IsAny<TimeSpan>(), It.IsNotNull<Exception>()),
                            Times.Once,
                            "The observer is additive: it is told, and the caller's own error handling is left as it was.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.1")]
        public async Task RunHandlerWithoutObserver()
        {
            // Arrange
            var ran = false;

            // Act
            await ObservedHandler.RunAsync(null,
                                           "a",
                                           new object(),
                                           new FakeTimeProvider(),
                                           () =>
                                           {
                                               ran = true;
                                               return Task.CompletedTask;
                                           });

            // Assert
            Assert.IsTrue(ran, "The production runtime registers none, so the seam must cost it nothing.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.3")]
        public async Task RunHandlerWhenObserverThrows()
        {
            // Arrange
            var observer = new Mock<IActorMessageObserver>();
            observer.Setup(candidate => candidate.OnHandled(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<Exception?>()))
                    .Throws(new InvalidOperationException());
            var ran = false;

            // Act
            await ObservedHandler.RunAsync(observer.Object,
                                           "a",
                                           new object(),
                                           new FakeTimeProvider(),
                                           () =>
                                           {
                                               ran = true;
                                               return Task.CompletedTask;
                                           });

            // Assert
            Assert.IsTrue(ran, "A faulty observer must never affect the message it is observing.");
        }
    }
}