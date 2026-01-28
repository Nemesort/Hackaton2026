using System;

<<<<<<< HEAD:Hackaton/Assets/Script/Runtime/Test/CombatSystem.cs
=======
[MapNode("CombatSystem", MapTag.Gameplay)]
[Depends(typeof(StatsManager), "pv", "pm", "actions")]
[Depends(typeof(EntityManager), "mobs")]
[MapNodeComment("Manage interactions between entities when entering a battle instance")]
>>>>>>> origin/UI:Hackaton/Assets/Script/Runtime/Test/Group2/CombatSystem.cs
public class CombatSystem
{
    private StatsManager statsManager;
    private EntityManager entityManager;
}
