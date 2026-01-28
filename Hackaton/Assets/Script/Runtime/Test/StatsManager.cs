using System;

[MapNode("StatsManager", MapTag.Manager | MapTag.Gameplay)]
public class StatsManager
{
    [Exposed] public int pv;
    [Exposed] public int pm;
    [Exposed] public int sp;
    [Exposed] public int actions;

    private ExplorationSystem explorationSystem;
}
