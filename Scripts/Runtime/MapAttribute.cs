using System;

[AttributeUsage(AttributeTargets.Class)]
public class MapNodeAttribute : Attribute
{
    public string DisplayName { get; }
    public MapTag Tags { get; }

    public MapNodeAttribute(string displayName, MapTag tags)
    {
        DisplayName = displayName;
        Tags = tags;
    }
}
