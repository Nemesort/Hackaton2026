using System;

[MapNode("EntityManager", MapTag.Gameplay)]
public class EntityManager
{
    private StatsManager statsManager;

    public object[] mobs;
    public object[] allies;
    public object[] npc;
}
