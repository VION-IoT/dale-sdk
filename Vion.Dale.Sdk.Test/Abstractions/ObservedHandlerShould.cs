using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    [TestClass]
    public class ObservedHandlerShould
    {
        [TestMethod]
        public async Task NotifyTheObserverOfASuccessfulHandlerWithItsDuration()
        {
            var clock = new FakeTimeProvider();
            var observer = new Mock<IActorMessageObserver>();
            var message = new object();

            await ObservedHandler.RunAsync(observer.Object,
                                           "a",
                                           message,
                                           clock,
                                           () =>
                                           {
                                               clock.Advance(TimeSpan.FromMilliseconds(7));
                                               return Task.CompletedTask;
                                           });

            observer.Verify(o => o.OnHandled("a", message, TimeSpan.FromMilliseconds(7), null), Times.Once);
        }

        [TestMethod]
        public async Task NotifyTheObserverOfAThrowingHandlerAndRethrow()
        {
            var observer = new Mock<IActorMessageObserver>();
            var message = new object();
            Exception? caught = null;

            try
            {
                await ObservedHandler.RunAsync(observer.Object, "a", message, new FakeTimeProvider(), () => Task.FromException(new InvalidOperationException("boom")));
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.IsInstanceOfType<InvalidOperationException>(caught);
            observer.Verify(o => o.OnHandled("a", message, It.IsAny<TimeSpan>(), It.IsNotNull<Exception>()), Times.Once);
        }

        [TestMethod]
        public async Task RunTheHandlerWhenNoObserverIsRegistered()
        {
            var ran = false;

            await ObservedHandler.RunAsync(null,
                                           "a",
                                           new object(),
                                           new FakeTimeProvider(),
                                           () =>
                                           {
                                               ran = true;
                                               return Task.CompletedTask;
                                           });

            Assert.IsTrue(ran);
        }

        [TestMethod]
        public async Task NotLetAFaultyObserverBreakHandling()
        {
            var observer = new Mock<IActorMessageObserver>();
            observer.Setup(o => o.OnHandled(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<Exception?>()))
                    .Throws(new InvalidOperationException("observer boom"));
            var ran = false;

            await ObservedHandler.RunAsync(observer.Object,
                                           "a",
                                           new object(),
                                           new FakeTimeProvider(),
                                           () =>
                                           {
                                               ran = true;
                                               return Task.CompletedTask;
                                           });

            Assert.IsTrue(ran);
        }
    }
}