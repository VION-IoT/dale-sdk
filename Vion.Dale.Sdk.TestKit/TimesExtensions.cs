using Vion.Dale.Sdk.Core;
using Moq;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Extension methods to validate Moq Times constraints against actual invocation counts.
    /// </summary>
    [PublicApi]
    public static class TimesExtensions
    {
        public static void AssertCount(this Times times, int actualCount, string assertMessage)
        {
            var str = times.ToString();
            var valid = false;
            if (times == Times.Once())
            {
                valid = actualCount == 1;
            }
            else if (times == Times.Never())
            {
                valid = actualCount == 0;
            }
            else if (times == Times.AtLeastOnce())
            {
                valid = actualCount >= 1;
            }
            else if (str.StartsWith("Exactly"))
            {
                valid = actualCount == ExtractNum(str);
            }
            else if (str.StartsWith("AtLeast"))
            {
                valid = actualCount >= ExtractNum(str);
            }
            else if (str.StartsWith("AtMost"))
            {
                valid = actualCount <= ExtractNum(str);
            }

            if (!valid)
            {
                throw new TestKitVerificationException($"{assertMessage}: Expected {times} but found {actualCount}.");
            }
        }

        private static int ExtractNum(string str)
        {
            var start = str.IndexOf('(') + 1;
            var end = str.IndexOf(')');
            return int.Parse(str.Substring(start, end - start));
        }
    }
}