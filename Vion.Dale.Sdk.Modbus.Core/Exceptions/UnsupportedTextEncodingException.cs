using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when an unsupported text encoding value is specified.
    /// </summary>
    public class UnsupportedTextEncodingException : Exception
    {
        /// <summary>
        ///     Gets the unsupported text encoding value.
        /// </summary>
        public TextEncoding TextEncoding { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnsupportedTextEncodingException" /> class.
        /// </summary>
        /// <param name="textEncoding">The unsupported text encoding value.</param>
        public UnsupportedTextEncodingException(TextEncoding textEncoding) : base($"Unsupported text encoding specified: {textEncoding}.")
        {
            TextEncoding = textEncoding;
        }
    }
}