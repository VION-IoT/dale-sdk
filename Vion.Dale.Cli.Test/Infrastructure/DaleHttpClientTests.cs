using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Infrastructure;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class DaleHttpClientTests
    {
        // The shape cloud-api's ApiExceptionFilter returns for every failure.
        private const string ErrorEnvelope = """
                                             {"statusCode":409,"exceptionType":"ConflictException","exceptionId":"6f1b2e0c-6f2f-4b0e-9a1a-3c1d5e7b9a11",
                                              "message":"Package id 'Acme.Chargers' is already registered on the platform. Package ids are globally unique and compared case-insensitively."}
                                             """;

        [TestMethod]
        public void DescribeError_ExtractsTheServerMessage()
        {
            Assert.AreEqual("Package id 'Acme.Chargers' is already registered on the platform. Package ids are globally unique and compared case-insensitively.",
                            DaleHttpClient.DescribeError(ErrorEnvelope));
        }

        [TestMethod]
        public void DescribeError_ToleratesPascalCasedEnvelope()
        {
            Assert.AreEqual("Boom", DaleHttpClient.DescribeError("""{"StatusCode":500,"Message":"Boom"}"""));
        }

        [TestMethod]
        public void DescribeError_FallsBackToTheRawBodyWhenNotAnEnvelope()
        {
            Assert.AreEqual("<html>502 Bad Gateway</html>", DaleHttpClient.DescribeError("<html>502 Bad Gateway</html>"));
            Assert.AreEqual("""{"statusCode":500}""", DaleHttpClient.DescribeError("""{"statusCode":500}"""));
        }

        [TestMethod]
        public void DescribeError_HandlesAnEmptyBody()
        {
            Assert.AreEqual("(no response body)", DaleHttpClient.DescribeError(""));
            Assert.AreEqual("(no response body)", DaleHttpClient.DescribeError(null));
        }
    }
}