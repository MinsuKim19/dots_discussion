using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

public class SpawnAuthoring : MonoBehaviour, IDeclareReferencedPrefabs, IConvertGameObjectToEntity
{
    public DefenseSetting defenseSetting;
    public float CoolDownSeconds;
    public int MaxEnemiesCount;
    public float SpawnRange;
    public GameObject GoalPos;

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(defenseSetting.AllyPrefab);
        referencedPrefabs.Add(defenseSetting.EnemyPrefab);
    }

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var spawnerData = new SpawnerData
        {
            EnemyPrefab = conversionSystem.GetPrimaryEntity(defenseSetting.EnemyPrefab),
            SpawnPosition = transform.position,
            GoalPosition = GoalPos.transform.position,
            SpawnRange = SpawnRange,
            CoolDownSeconds = CoolDownSeconds,
            MaxEnemiesCount = MaxEnemiesCount,
        };

        dstManager.AddComponentData(entity, spawnerData);
    }
}