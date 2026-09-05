using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit.Test.TestHelpers;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     The two construction entry points a block author starts from. Both reach the same reflection
    ///     call, so every refusal row runs against both — the rows are the entry point, the assertion is
    ///     what the caller is told.
    /// </summary>
    [TestClass]
    public class LogicBlockTestHelperShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.1")]
        public void ConstructBlockFromLoggerConstructor()
        {
            // Act
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            // Assert
            Assert.IsNotNull(block);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.1")]
        public void ReturnLoggerMockAlongsideBlock()
        {
            // Act
            var (block, loggerMock) = LogicBlockTestHelper.CreateWithLogger<SampleLogicBlock>();

            // Assert
            Assert.IsNotNull(block);
            Assert.IsNotNull(loggerMock.Object);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.1")]
        public void GiveEachConstructedBlockItsOwnLoggerMock()
        {
            // Act
            var (_, firstMock) = LogicBlockTestHelper.CreateWithLogger<SampleLogicBlock>();
            var (_, secondMock) = LogicBlockTestHelper.CreateWithLogger<SampleLogicBlock>();

            // Assert
            Assert.AreNotSame(firstMock, secondMock);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.1")]
        public void CreateLoggerMockThatRecordsWhatBlockLogs()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();

            // Act
            loggerMock.Object.LogInformation("power settled");

            // Assert
            loggerMock.VerifyLogContains("power settled", LogLevel.Information, Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.2")]
        [DataRow(false, DisplayName = "Create")]
        [DataRow(true, DisplayName = "CreateWithLogger")]
        public void RefuseBlockWithoutLoggerConstructorNamingRequiredConstructor(bool withLogger)
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<MissingMethodException>(() => Construct<NoLoggerConstructorBlock>(withLogger));
            StringAssert.Contains(thrown.Message, "public constructor taking a single Microsoft.Extensions.Logging.ILogger");
            StringAssert.Contains(thrown.Message, nameof(NoLoggerConstructorBlock));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.2")]
        [DataRow(false, DisplayName = "Create")]
        [DataRow(true, DisplayName = "CreateWithLogger")]
        public void RefuseAbstractBlockSayingItCannotBeConstructed(bool withLogger)
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<MissingMethodException>(() => Construct<AbstractBlock>(withLogger));
            StringAssert.Contains(thrown.Message, "is abstract");

            // An abstract type must not be told a constructor is missing: it has one, and it is still
            // unconstructable, so the missing-constructor text would send the reader looking for the wrong thing.
            Assert.DoesNotContain("public constructor taking a single", thrown.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-001.3")]
        [DataRow(false, DisplayName = "Create")]
        [DataRow(true, DisplayName = "CreateWithLogger")]
        public void PropagateExceptionBlockConstructorThrew(bool withLogger)
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<NotSupportedException>(() => Construct<ThrowingConstructorBlock>(withLogger));
            Assert.AreEqual("this block refuses to be constructed", thrown.Message);
        }

        // The two entry points are the rows; each reaches the same Activator call, so a guard added to one
        // and not the other would pass half of every refusal row above.
        private static void Construct<T>(bool withLogger)
            where T : LogicBlockBase
        {
            if (withLogger)
            {
                LogicBlockTestHelper.CreateWithLogger<T>();
                return;
            }

            LogicBlockTestHelper.Create<T>();
        }
    }
}