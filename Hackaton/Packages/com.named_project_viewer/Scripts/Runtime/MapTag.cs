using System;

[Flags]
public enum MapTag
{
    None = 0 << 0,
    Manager = 1 << 1,
    UI = 1 << 2,
    Gameplay = 1 << 3,
    Audio = 1 << 4,
    Network = 1 << 5,
    Persistence = 1 << 6
}
