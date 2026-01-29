using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ExposedAttribute : Attribute
{
    public string Alias { get; }
    public ExposedAttribute(string alias = null)
    {
        Alias = alias;
    }
}
