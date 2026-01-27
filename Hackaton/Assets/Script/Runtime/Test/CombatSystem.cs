using System;

[MapNode("CombatSystem", MapTag.Gameplay)]
[Depends(typeof(StatsManager), "pv", "pm", "actions")]
[Depends(typeof(EntityManager), "mobs")]
public class CombatSystem
{
}
