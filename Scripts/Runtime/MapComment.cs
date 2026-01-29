using System;

[AttributeUsage(AttributeTargets.Class)]
public class MapNodeComment : Attribute
{
    public string Comment { get; }

    public MapNodeComment(string comment)
    {
        Comment = comment;
    }
}
