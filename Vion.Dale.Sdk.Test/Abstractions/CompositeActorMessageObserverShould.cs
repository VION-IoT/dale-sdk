using System;
using System.Collections.Generic;
using Moq;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    [TestClass]
    public class CompositeActorMessageObserverShould
    {
        [TestMethod]
        public void ReturnNullWhenCombiningNoObservers()
        {
            var combined = CompositeActorMessageObserver.Combine(new List<IActorMessageObserver>());

            Assert.IsNull(combined);
        }

        [TestMethod]
        public void ReturnTheSingleObserverUnwrappedWhenCombiningOne()
        {
            var only = new Mock<IActorMessageObserver>().Object;

            var combined = CompositeActorMessageObserver.Combine(new[] { only });

            Assert.AreSame(only, combined);
        }

        [TestMethod]
        public void FanOutOnHandledToEveryObserver()
        {
            var a = new Mock<IActorMessageObserver>();
            var b = new Mock<IActorMessageObserver>();
            var combined = CompositeActorMessageObserver.Combine(new[] { a.Object, b.Object })!;
            var message = new object();

            combined.OnHandled("x", message, TimeSpan.FromMilliseconds(4), null);

            a.Verify(o => o.OnHandled("x", message, TimeSpan.FromMilliseconds(4), null), Times.Once);
            b.Verify(o => o.OnHandled("x", message, TimeSpan.FromMilliseconds(4), null), Times.Once);
        }

        [TestMethod]
        public void IsolateAFaultyObserverSoTheOthersStillRun()
        {
            var faulty = new Mock<IActorMessageObserver>();
            faulty.Setup(o => o.OnHandled(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<Exception?>())).Throws(new InvalidOperationException("boom"));
            var healthy = new Mock<IActorMessageObserver>();
            var combined = CompositeActorMessageObserver.Combine(new[] { faulty.Object, healthy.Object })!;

            combined.OnHandled("x", new object(), TimeSpan.Zero, null);

            healthy.Verify(o => o.OnHandled("x", It.IsAny<object>(), TimeSpan.Zero, null), Times.Once);
        }
    }
}