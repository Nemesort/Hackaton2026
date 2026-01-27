using System;

[MapNode("ExplorationSystem", MapTag.Gameplay)]
[Depends(typeof(StatsManager), "pv", "sp")]
public class ExplorationSystem
{
    private StatsManager statsManager;
}
