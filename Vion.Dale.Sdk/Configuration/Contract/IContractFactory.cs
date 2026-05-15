using System;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    public interface IContractFactory
    {
        object Create(Type propertyType, string identifier);
    }
}