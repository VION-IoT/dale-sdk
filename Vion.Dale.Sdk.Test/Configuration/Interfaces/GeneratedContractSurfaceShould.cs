using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration.Interfaces
{
    /// <summary>
    ///     The surface a <c>[LogicBlockContract]</c> declaration produces, read off the types the generator
    ///     emitted into this test assembly for <see cref="BindLinkContract" /> and
    ///     <see cref="BindOneWayContract" />. The generated types are the artifact under test, so they are read
    ///     by reflection rather than through a seam — three of the conventions asserted here exist only so the
    ///     interface factory can find them again.
    /// </summary>
    [TestClass]
    public class GeneratedContractSurfaceShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-002.1")]
        [TestProperty("spec", "AC-BIND-003.1")]
        public void GenerateOneHandlerInterfacePerDeclaredRole()
        {
            // Arrange
            var contract = typeof(BindLinkContract);

            // Act
            var roles = contract.Assembly
                                .GetTypes()
                                .Where(candidate => candidate.GetCustomAttribute<LogicInterfaceAttribute>()?.ContractType == contract)
                                .Select(role => role.Name);

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(IBindSource), nameof(IBindSink) }, roles.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-002.2")]
        [TestProperty("spec", "AC-BIND-002.4")]
        [DataRow(typeof(IBindSource), typeof(IHandleStateUpdate<BindLinkContract.Level>), DisplayName = "the source handles the state update declared to it")]
        [DataRow(typeof(IBindSource), typeof(IHandleResponse<BindLinkContract.Reading>), DisplayName = "the requesting source handles the answer")]
        [DataRow(typeof(IBindSink), typeof(IHandleCommand<BindLinkContract.Nudge>), DisplayName = "the sink handles the command declared to it")]
        [DataRow(typeof(IBindSink), typeof(IHandleRequest<BindLinkContract.Poll, BindLinkContract.Reading>), DisplayName = "the sink answers the request declared to it")]
        public void GiveRoleHandleFacePerMessageDeclaredToIt(Type role, Type expectedFace)
        {
            // Arrange / Act
            var faces = role.GetInterfaces();

            // Assert
            CollectionAssert.Contains(faces, expectedFace);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-002.2")]
        [TestProperty("spec", "AC-BIND-002.4")]
        [DataRow(typeof(IBindSourceSenderInterface), typeof(ISendCommand<BindLinkContract.Nudge>), DisplayName = "the source sends the command declared from it")]
        [DataRow(typeof(IBindSourceSenderInterface), typeof(ISendRequest<BindLinkContract.Poll>), DisplayName = "the source sends the request declared from it")]
        [DataRow(typeof(IBindSinkSenderInterface), typeof(ISendStateUpdate<BindLinkContract.Level>), DisplayName = "the sink sends the state update declared from it")]
        public void GiveRoleSendFacePerMessageDeclaredFromIt(Type senderInterface, Type expectedFace)
        {
            // Arrange / Act
            var faces = senderInterface.GetInterfaces();

            // Assert
            CollectionAssert.Contains(faces, expectedFace);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-002.1")]
        public void GiveNoFaceToMessageDeclaredOutsideItsContract()
        {
            // Arrange — StrayNudge carries [Command] naming this contract's roles but is not nested in it.
            var faces = typeof(IBindSourceSenderInterface).GetInterfaces().Concat(typeof(IBindSink).GetInterfaces());

            // Act
            var strayFaces = faces.Where(face => face.GetGenericArguments().Contains(typeof(StrayNudge)));

            // Assert
            Assert.IsEmpty(strayFaces);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-002.3")]
        public void GenerateNothingForContractWhoseRolesDoNotBeginWithI()
        {
            // Arrange
            var contract = typeof(LowercaseRolesContract);

            // Act
            var roles = contract.Assembly.GetTypes().Where(candidate => candidate.GetCustomAttribute<LogicInterfaceAttribute>()?.ContractType == contract);

            // Assert
            Assert.IsEmpty(roles);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.2")]
        [DataRow(typeof(IBindSource), typeof(IBindSink), typeof(IBindSourceSenderInterface), DisplayName = "the source names the sink as its counterpart")]
        [DataRow(typeof(IBindSink), typeof(IBindSource), typeof(IBindSinkSenderInterface), DisplayName = "the sink names the source as its counterpart")]
        public void MarkRoleWithCounterpartSenderAndContract(Type role, Type expectedCounterpart, Type expectedSender)
        {
            // Arrange / Act
            var marker = role.GetCustomAttribute<LogicInterfaceAttribute>();

            // Assert
            Assert.IsNotNull(marker);
            Assert.AreEqual(expectedCounterpart, marker.MatchingInterface);
            Assert.AreEqual(expectedSender, marker.SenderInterface);
            Assert.AreEqual(typeof(BindLinkContract), marker.ContractType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.3")]
        [DataRow(nameof(IBindSource), DisplayName = "the source's sender pair")]
        [DataRow(nameof(IBindSink), DisplayName = "the sink's sender pair")]
        public void NameSenderInterfaceAndClassAfterRole(string roleName)
        {
            // Arrange
            var assembly = typeof(BindLinkContract).Assembly;

            // Act
            var senderInterface = assembly.GetType($"{typeof(BindLinkContract).Namespace}.{roleName}SenderInterface");
            var senderClass = assembly.GetType($"{typeof(BindLinkContract).Namespace}.{roleName[1..]}SenderInterface");

            // Assert
            Assert.IsNotNull(senderInterface);
            Assert.IsNotNull(senderClass);
            Assert.IsTrue(senderInterface.IsAssignableFrom(senderClass));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.4")]
        public void GiveSenderClassConstructorFactoryInstantiatesBy()
        {
            // Arrange — resolved by the same naming convention the factory uses, not by a compile-time reference.
            var senderClass = typeof(BindLinkContract).Assembly.GetType($"{typeof(BindLinkContract).Namespace}.{nameof(IBindSource)[1..]}SenderInterface");

            // Act
            var parameters = senderClass!.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType);

            // Assert
            CollectionAssert.AreEqual(new[] { typeof(string), typeof(IBindSource), typeof(Func<LogicBlockId>), typeof(IActorContext), typeof(ILogger) }, parameters.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.5")]
        public void GiveSendingRoleExtensionClassCarryingNonPublicRegistration()
        {
            // Arrange
            var extensionClass = typeof(BindLinkContract).Assembly.GetType($"{typeof(BindLinkContract).Namespace}.{nameof(IBindSource)}Extensions");

            // Act
            var registration = extensionClass?.GetMethod("RegisterInstance", BindingFlags.NonPublic | BindingFlags.Static);

            // Assert
            Assert.IsNotNull(registration);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.5")]
        public void GiveReceivingRoleNoExtensionClass()
        {
            // Arrange
            var assembly = typeof(BindOneWayContract).Assembly;

            // Act
            var extensionClass = assembly.GetType($"{typeof(BindOneWayContract).Namespace}.{nameof(IBindListener)}Extensions");

            // Assert
            Assert.IsNull(extensionClass);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.6")]
        public void NameLinkedCounterpartHelperAfterCounterpartRole()
        {
            // Arrange
            var extensionClass = typeof(BindLinkContract).Assembly.GetType($"{typeof(BindLinkContract).Namespace}.{nameof(IBindSource)}Extensions");

            // Act
            var helper = extensionClass?.GetMethod("GetLinkedBindSinks", BindingFlags.Public | BindingFlags.Static);

            // Assert
            Assert.IsNotNull(helper);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.6")]
        public void ReportNoLinkedCounterpartForUnregisteredImplementation()
        {
            // Arrange — never configured, so the endpoint was never registered against it.
            var block = new BindSourceBlock();

            // Act
            var linked = block.GetLinkedBindSinks();

            // Assert
            Assert.IsEmpty(linked);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.7")]
        public void GiveSendingRoleOneExtensionMethodPerMessageItSends()
        {
            // Arrange
            var extensionClass = typeof(BindLinkContract).Assembly.GetType($"{typeof(BindLinkContract).Namespace}.{nameof(IBindSource)}Extensions");

            // Act
            var methods = extensionClass!.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(method => method.Name);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "GetLinkedBindSinks", "SendCommand", "SendRequest" }, methods.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-003.7")]
        public void SendNothingThroughExtensionOfUnregisteredImplementation()
        {
            // Arrange
            var block = new BindSourceBlock();

            // Act
            block.SendCommand(new InterfaceId(new LogicBlockId("elsewhere"), "Sink"), new BindLinkContract.Nudge(1));

            // Assert — no registered endpoint, so nothing was reached and nothing threw.
            Assert.IsEmpty(block.GetLinkedBindSinks());
        }
    }

    // Deliberately the shape DALE047 reports: this fixture exists to prove the generator contributes
    // nothing for it, so the declaration has to survive the diagnostic that now names it.
#pragma warning disable DALE047

    /// <summary>A message declared beside its contract rather than inside it, which contributes nothing.</summary>
    [Command(From = "IBindSource", To = "IBindSink")]
    public readonly record struct StrayNudge(int Amount);
#pragma warning restore DALE047

    // DALE009 refuses these role names at compile time, which is the point: this fixture is the shape a
    // hand-built compilation can still reach, and the generator's own answer to it is what is asserted.
#pragma warning disable DALE009

    /// <summary>A contract whose role names do not begin with <c>I</c>, so nothing is generated for it.</summary>
    [LogicBlockContract(BetweenInterface = "BindLower", AndInterface = "BindUpper")]
    public static class LowercaseRolesContract
    {
        [Command(From = "BindLower", To = "BindUpper")]
        public readonly record struct Shove(int Amount);
    }
#pragma warning restore DALE009
}