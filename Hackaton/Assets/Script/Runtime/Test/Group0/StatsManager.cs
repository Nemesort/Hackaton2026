using System;

[MapNode("StatsManager", MapTag.Manager | MapTag.Gameplay)]
[MapNodeComment("Stats that are used by any entities")]
public class StatsManager
{
    public int pv;
    public int pm;
    public int sp;
    public int actions;

    private ExplorationSystem explorationSystem;
}
