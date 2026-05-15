namespace Vion.Dale.Sdk.Persistence
{
    public readonly record struct PersistentDataEntry(string Key, string TypeFullName, object Value);
}