using Vion.Dale.Cli.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class CommandContextTests
    {
        [TestMethod]
        public void ResolveLocal_ProductionEnvironment()
        {
            var ctx = CommandContext.ResolveLocal("production");

            Assert.AreEqual("production", ctx.Environment);
            Assert.AreEqual("https://cloudapi.ecocoa.ch", ctx.ApiBaseUrl);
            Assert.AreEqual("https://auth.ecocoa.ch/realms/vion", ctx.AuthBaseUrl);
        }

        [TestMethod]
        public void ResolveLocal_UsesEnvironmentFlag()
        {
            var ctx = CommandContext.ResolveLocal("test");

            Assert.AreEqual("test", ctx.Environment);
            Assert.AreEqual("https://cloudapi.test.ecocoa.ch", ctx.ApiBaseUrl);
            Assert.AreEqual("https://auth.test.ecocoa.ch/realms/vion", ctx.AuthBaseUrl);
        }

        [TestMethod]
        public void ResolveLocal_StagingEnvironment()
        {
            var ctx = CommandContext.ResolveLocal("staging");

            Assert.AreEqual("staging", ctx.Environment);
            Assert.AreEqual("https://cloudapi.staging.ecocoa.ch", ctx.ApiBaseUrl);
        }
    }
}
