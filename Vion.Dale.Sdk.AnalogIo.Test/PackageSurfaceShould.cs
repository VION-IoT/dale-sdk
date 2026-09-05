using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;

namespace Vion.Dale.Sdk.AnalogIo.Test
{
    /// <summary>
    ///     What the package declares, read off the shipped assembly rather than off the source: the members
    ///     each face carries, the message types the two sides of a pair share, the contract-type names a
    ///     platform matches a binding on, and the API classification of every public type.
    /// </summary>
    [TestClass]
    public class PackageSurfaceShould
    {
        private static readonly Assembly Package = typeof(IAnalogInput).Assembly;

        [TestMethod]
        [TestProperty("spec", "AC-IO-001.1")]
        public void GiveInputFaceOneChangeEventAndNoOperation()
        {
            // Arrange / Act
            var face = typeof(IAnalogInput);

            // Assert
            CollectionAssert.AreEqual(new[] { nameof(IAnalogInput.InputChanged) }, face.GetEvents().Select(declared => declared.Name).ToList());
            Assert.IsEmpty(face.GetMethods().Where(declared => !declared.IsSpecialName));
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-001.1")]
        public void GiveOutputFaceOneCommandAndOneChangeEvent()
        {
            // Arrange / Act
            var face = typeof(IAnalogOutput);

            // Assert
            CollectionAssert.AreEqual(new[] { nameof(IAnalogOutput.OutputChanged) }, face.GetEvents().Select(declared => declared.Name).ToList());
            CollectionAssert.AreEqual(new[] { nameof(IAnalogOutput.Set) }, face.GetMethods().Where(declared => !declared.IsSpecialName).Select(declared => declared.Name).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-001.1")]
        public void GiveInputProviderFaceOneOperationAndNoEvent()
        {
            // Arrange / Act
            var face = typeof(IAnalogInputProvider);

            // Assert — write-only: a simulator drives the signal and hears nothing back.
            Assert.IsEmpty(face.GetEvents());
            CollectionAssert.AreEqual(new[] { nameof(IAnalogInputProvider.Drive) },
                                      face.GetMethods().Where(declared => !declared.IsSpecialName).Select(declared => declared.Name).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-001.1")]
        public void GiveOutputProviderFaceOneCommandEventAndOneConfirmation()
        {
            // Arrange / Act
            var face = typeof(IAnalogOutputProvider);

            // Assert
            CollectionAssert.AreEqual(new[] { nameof(IAnalogOutputProvider.SetReceived) }, face.GetEvents().Select(declared => declared.Name).ToList());
            CollectionAssert.AreEqual(new[] { nameof(IAnalogOutputProvider.Confirm) },
                                      face.GetMethods().Where(declared => !declared.IsSpecialName).Select(declared => declared.Name).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-001.2")]
        public void CarryOneMessageTypeAcrossBothSidesOfPair()
        {
            // Arrange
            var harness = new ContractHarness();
            var output = harness.Output();
            var provider = harness.OutputProvider();

            // Act — the command one side sends and the command the other side handles.
            output.Set(4.2);
            provider.Confirm(4.2);

            // Assert — a provider face is the consumer face inverted, over the consumer's own message types;
            // a copy on either side would make the pair unbridgeable.
            CollectionAssert.AreEquivalent(new[] { typeof(SetAnalogOutput), typeof(AnalogOutputChanged) },
                                           harness.Sent.Select(message => message.GetType().GetGenericArguments().Single()).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-001.3")]
        [DataRow(typeof(IAnalogInput), "AnalogInput")]
        [DataRow(typeof(IAnalogOutput), "AnalogOutput")]
        [DataRow(typeof(IAnalogInputProvider), "AnalogInputProvider")]
        [DataRow(typeof(IAnalogOutputProvider), "AnalogOutputProvider")]
        public void NameContractTypeOnFace(Type face, string expectedContractType)
        {
            // Arrange / Act
            var declaration = face.GetCustomAttribute<ServiceProviderContractTypeAttribute>();

            // Assert — the platform matches a binding to its handler through this string, so it is an
            // identifier and not a label.
            Assert.IsNotNull(declaration);
            Assert.AreEqual(expectedContractType, declaration.ServiceProviderContractType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.1")]
        public void AddressHandlerNamedOnEachFace()
        {
            // Arrange
            var harness = new ContractHarness();

            // Act
            var handlerNames = new[]
                               {
                                   harness.Input().ContractHandlerActorName,
                                   harness.Output().ContractHandlerActorName,
                                   harness.InputProvider().ContractHandlerActorName,
                                   harness.OutputProvider().ContractHandlerActorName,
                               };

            // Assert — the name is how the runtime and the development host find the actor servicing the contract.
            CollectionAssert.AreEqual(new[]
                                      {
                                          nameof(AnalogInputHandler),
                                          nameof(AnalogOutputHandler),
                                          nameof(AnalogInputProviderHandler),
                                          nameof(AnalogOutputProviderHandler),
                                      },
                                      handlerNames);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.1")]
        [DataRow(typeof(AnalogInputChanged))]
        [DataRow(typeof(AnalogOutputChanged))]
        [DataRow(typeof(SetAnalogOutput))]
        public void CarryBareValueOnEachMessage(Type message)
        {
            // Arrange / Act
            var properties = message.GetProperties();

            // Assert — one field, the value itself: no unit, range, scale, timestamp or quality rides along.
            var carried = properties.Single();
            Assert.AreEqual("Value", carried.Name);
            Assert.AreEqual(typeof(double), carried.PropertyType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-008.2")]
        public void ClassifyEveryPublicTypeAsPublishedOrInternal()
        {
            // Arrange / Act
            var unclassified = Package.GetExportedTypes()
                                      .Where(declared => declared.GetCustomAttribute<PublicApiAttribute>() is null && declared.GetCustomAttribute<InternalApiAttribute>() is null)
                                      .Select(declared => declared.FullName)
                                      .ToList();

            // Assert — the mark is what puts a type on the published-surface manifest or deliberately keeps it off;
            // the analyzer only asks for one inside a declared API namespace, so an unmarked type elsewhere is silent.
            Assert.IsEmpty(unclassified);
        }
    }
}
