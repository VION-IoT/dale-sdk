using System;
using System.Linq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class LinkMultiplicityShould
    {
        [TestMethod]
        public void DeclareExactlyTheFourMultiplicityMembers()
        {
            var names = Enum.GetNames(typeof(LinkMultiplicity)).OrderBy(n => n).ToArray();

            CollectionAssert.AreEqual(
                new[] { "ExactlyOne", "OneOrMore", "ZeroOrMore", "ZeroOrOne" },
                names);
        }
    }
}
