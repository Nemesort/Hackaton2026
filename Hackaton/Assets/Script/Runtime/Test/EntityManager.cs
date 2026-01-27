using System;

[MapNode("EntityManager", MapTag.Manager)]
public class EntityManager
{
    [Exposed] public object[] mobs;
    [Exposed] public object[] allies;
    [Exposed] public object[] npc;
}
