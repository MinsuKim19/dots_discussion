using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class SpawnerAllyAuthoring : MonoBehaviour, IDeclareReferencedPrefabs, IConvertGameObjectToEntity
{
    public GameObject Prefab;
    public Vector3 SpawnPosition;
    public int Count;

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(Prefab);
    }

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var spawnerData = new SpawnerAllyData
        {
            Prefab = conversionSystem.GetPrimaryEntity(Prefab),
            SpawnPosition = SpawnPosition,
            Count = 1
        };

        dstManager.AddComponentData(entity, spawnerData);
    }
}

/*
[UpdateInGroup(typeof(GameObjectConversionGroup))]
class SpawnerConversionSystem : GameObjectConversionSystem
{
    protected override void OnCreate()
    {
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        Entities.ForEach((SpawnerAllyAuthoring input) =>
        {
            // Do the conversion and add the ECS component
            var spawnerData = new SpawnerAllyData
            {
                Prefab = GetPrimaryEntity(input.Prefab),
                SpawnPosition = input.SpawnPosition,
                Count = 1
            };

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, spawnerData);
        });
    }
}*/