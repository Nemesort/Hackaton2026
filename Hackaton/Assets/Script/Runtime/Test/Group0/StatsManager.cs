using System;

[MapNode("StatsManager", MapTag.Manager | MapTag.Gameplay)]
[MapNodeComment("Stats that are used by any entities")]
public class StatsManager
{
    [Exposed] public int pv;
    [Exposed] public int pm;
    [Exposed] public int sp;
    [Exposed] public int actions;
}
