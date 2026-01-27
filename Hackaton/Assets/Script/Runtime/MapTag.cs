using System;

[Flags]
public enum MapTag
{
    None = 0,
    Manager = 1 << 0,
    UI = 1 << 1,
    Gameplay = 1 << 2,
    Audio = 1 << 3,
    Network = 1 << 4,
    Persistence = 1 << 5,
}
