using System;

[MapNode("EntityManager", MapTag.Gameplay)]
[Depends(typeof(StatsManager), "pv", "pm", "sp", "actions")]
public class EntityManager
{
    private StatsManager statsManager;

    [Exposed] public object[] mobs;
    [Exposed] public object[] allies;
    [Exposed] public object[] npc;
}
