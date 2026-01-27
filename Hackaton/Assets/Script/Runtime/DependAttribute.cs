using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class DependsAttribute : Attribute
{
    public Type TargetType { get; }
    public string[] Uses { get; }

    public DependsAttribute(Type targetType, params string[] uses)
    {
        TargetType = targetType;
        Uses = uses ?? Array.Empty<string>();
    }
}
