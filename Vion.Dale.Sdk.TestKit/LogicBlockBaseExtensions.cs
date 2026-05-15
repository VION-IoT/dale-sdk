using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Extension methods to initialize logic blocks for testing with a fluent builder API.
    /// </summary>
    [PublicApi]
    public static class LogicBlockBaseExtensions
    {
        /// <summary>
        ///     Initializes the given logic block for testing, returning a typed test context.
        /// </summary>
        public static LogicBlockTestContext<T> InitializeForTest<T>(this T logicBlock)
            where T : LogicBlockBase
        {
            return logicBlock.CreateTestContext().Build();
        }

        /// <summary>
        ///     Creates a test context builder for the given logic block to allow test context customization. Call Build() at the
        ///     end to get the test context.
        /// </summary>
        public static LogicBlockTestContextBuilder<T> CreateTestContext<T>(this T logicBlock)
            where T : LogicBlockBase

        {
            return new LogicBlockTestContextBuilder<T>(logicBlock);
        }
    }
}