using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Exception thrown when a TestKit verification assertion fails.
    /// </summary>
    [PublicApi]
    public class TestKitVerificationException : Exception
    {
        public TestKitVerificationException(string message) : base(message)
        {
        }
    }
}