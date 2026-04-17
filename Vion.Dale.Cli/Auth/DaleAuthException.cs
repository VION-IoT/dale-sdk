using System;

namespace Vion.Dale.Cli.Auth
{
    public class DaleAuthException : Exception
    {
        public DaleAuthException(string message) : base(message)
        {
        }

        public DaleAuthException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}