using System;

[MapNode("CombatSystem", MapTag.Gameplay)]
[MapNodeComment("Manage interactions between entities when entering a battle instance")]

public class CombatSystem
{
    private StatsManager statsManager;
    private EntityManager entityManager;
}
