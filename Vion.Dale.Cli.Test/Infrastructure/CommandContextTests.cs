using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Infrastructure;

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
            Assert.AreEqual("https://api.vion.swiss", ctx.ApiBaseUrl);
            Assert.AreEqual("https://auth.vion.swiss/realms/vion", ctx.AuthBaseUrl);
        }

        [TestMethod]
        public void ResolveLocal_UsesEnvironmentFlag()
        {
            var ctx = CommandContext.ResolveLocal("test");

            Assert.AreEqual("test", ctx.Environment);
            Assert.AreEqual("https://api.test.vion.swiss", ctx.ApiBaseUrl);
            Assert.AreEqual("https://auth.test.vion.swiss/realms/vion", ctx.AuthBaseUrl);
        }
    }
}
