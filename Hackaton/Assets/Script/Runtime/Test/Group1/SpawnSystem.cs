using System;

[MapNode("SpawnSystem", MapTag.Gameplay)]
[Depends(typeof(EntityManager), "mobs")]
public class SpawnSystem
{
    private EntityManager entityManager;
}
