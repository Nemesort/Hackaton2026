using System;

[MapNode("CombatSystem", MapTag.Gameplay)]
[Depends(typeof(StatsManager), "pv", "pm", "actions")]
[Depends(typeof(EntityManager), "mobs")]
[MapNodeComment("Manage interactions between entities when entering a battle instance")]
public class CombatSystem
{
    private StatsManager statsManager;
    private EntityManager entityManager;
}
