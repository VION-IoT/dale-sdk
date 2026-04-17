using Vion.Dale.Sdk.Core;

// Namespaces where [PublicApi] is expected on all public types.
// The analyzer warns (DALE014) about public types in these namespaces
// that are not marked [PublicApi] or [InternalApi].
[assembly: PublicApiNamespace("Vion.Dale.Sdk.Core")]
[assembly: PublicApiNamespace("Vion.Dale.Sdk.Utils")]