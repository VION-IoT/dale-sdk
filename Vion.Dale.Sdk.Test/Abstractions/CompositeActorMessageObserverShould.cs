using System;
using System.Collections.Generic;
using Moq;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>
    ///     The pipeline notifies one observer, and a development host wants two — its message tap alongside
    ///     the vitals core the SDK registers. Combining them is what lets both exist without either knowing
    ///     about the other.
    /// </summary>
    [TestClass]
    public sealed class CompositeActorMessageObserverShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.4")]
        public void CombineNoObserversIntoNothing()
        {
            // Act
            var combined = CompositeActorMessageObserver.Combine(new List<IActorMessageObserver>());

            // Assert
            Assert.IsNull(combined, "The pipeline reads a missing observer as a seam nobody registered, so it does no per-message work at all.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.4")]
        public void CombineLoneObserverIntoItself()
        {
            // Arrange
            var only = new Mock<IActorMessageObserver>().Object;

            // Act
            var combined = CompositeActorMessageObserver.Combine(new[] { only });

            // Assert
            Assert.AreSame(only, combined, "One observer is used as it is, so a host with a single one pays nothing for the fan-out.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.4")]
        public void NotifyEveryObserverOfHandledMessage()
        {
            // Arrange
            var observer1 = new Mock<IActorMessageObserver>();
            var observer2 = new Mock<IActorMessageObserver>();
            var combined = CompositeActorMessageObserver.Combine(new[] { observer1.Object, observer2.Object })!;
            var message = new object();

            // Act
            combined.OnReceived("x", message);
            combined.OnHandled("x", message, TimeSpan.FromMilliseconds(4), null);

            // Assert
            observer1.Verify(observer => observer.OnReceived("x", message), Times.Once);
            observer1.Verify(observer => observer.OnHandled("x", message, TimeSpan.FromMilliseconds(4), null), Times.Once);
            observer2.Verify(observer => observer.OnReceived("x", message), Times.Once);
            observer2.Verify(observer => observer.OnHandled("x", message, TimeSpan.FromMilliseconds(4), null), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.3")]
        public void NotifyRemainingObserversWhenOneThrows()
        {
            // Arrange
            var faulty = new Mock<IActorMessageObserver>();
            faulty.Setup(observer => observer.OnHandled(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<TimeSpan>(), It.IsAny<Exception?>()))
                  .Throws(new InvalidOperationException());
            faulty.Setup(observer => observer.OnReceived(It.IsAny<string>(), It.IsAny<object>())).Throws(new InvalidOperationException());
            var healthy = new Mock<IActorMessageObserver>();
            var combined = CompositeActorMessageObserver.Combine(new[] { faulty.Object, healthy.Object })!;

            // Act
            combined.OnReceived("x", new object());
            combined.OnHandled("x", new object(), TimeSpan.Zero, null);

            // Assert
            healthy.Verify(observer => observer.OnReceived("x", It.IsAny<object>()), Times.Once);
            healthy.Verify(observer => observer.OnHandled("x", It.IsAny<object>(), TimeSpan.Zero, null),
                           Times.Once,
                           "The tap and the vitals core share the slot, and one must not be able to take the other down.");
        }
    }
}