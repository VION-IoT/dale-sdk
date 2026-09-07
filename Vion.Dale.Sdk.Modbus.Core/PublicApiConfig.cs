using Vion.Dale.Sdk.Core;

// The root namespace, which subsumes every namespace beneath it: DALE014 matches a declaration
// exactly or as a prefix, so this one declaration asks the whole assembly - Client, Conversion,
// Diagnostics, Exceptions, Server and Validation - for a surface mark. Declaring the six
// individually would say the same thing six times, and would leave a seventh namespace added later
// unasked. The sibling packages' declarations are already this shape.
[assembly: PublicApiNamespace("Vion.Dale.Sdk.Modbus.Core")]