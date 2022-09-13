using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.AI;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

public enum UnitType
{
    Ally = 0,
    Enemy = 1
}

public struct AllyData : IComponentData
{
    public Entity Entity;
    public Entity TargetEntity;

    public float HP;
    public float Range;
    public float Damage;

    public float3 Position;
}

public struct EnemyData : IComponentData
{
    public Entity Entity;
    public Entity TargetEntity;

    public float HP;
    public float Range;
    public float Damage;

    public float3 Position;
}


public struct SpawnerAllyData : IComponentData
{
    public Entity Prefab;
    public float3 SpawnPosition;
    public int Count;
}

public struct SpawnerData : IComponentData
{
    public Entity EnemyPrefab;
    public float3 SpawnPosition;
    public float3 GoalPosition;
    public float SpawnRange;
    public float CoolDownSeconds;
    public float SecondsUntilGenerate;
    public int MaxEnemiesCount;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(UnitLifecycleManager))]
public partial class UnitSystem : SystemBase
{
    BeginInitializationEntityCommandBufferSystem entityCommandBufferSystem;

    public Dictionary<int, NativeList<AllyData>> Allies = new Dictionary<int, NativeList<AllyData>>();
    public Dictionary<int, NativeList<EnemyData>> Enemies = new Dictionary<int, NativeList<EnemyData>>();
    public NativeList<CrowdAgent> Agents;
    public NativeList<CrowdAgentNavigator> AgentNavigators;
    public NativeList<PolygonIDContainer> PolygonIDs;

    public int EnemyCount;

    NavMeshQuery mapLocationQuery;

    protected override void OnCreate()
    {
        entityCommandBufferSystem = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>();
        EnemyCount = 0;

        var navMeshWorld = NavMeshWorld.GetDefaultWorld();
        mapLocationQuery = new NavMeshQuery(navMeshWorld, Allocator.Persistent);

        Agents = new NativeList<CrowdAgent>(Allocator.Persistent);
        AgentNavigators = new NativeList<CrowdAgentNavigator>(Allocator.Persistent);
        PolygonIDs = new NativeList<PolygonIDContainer>(Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        SpawnAlly();
        SpawnEnemy();

        foreach (var item in Allies)
        {
            item.Value.Clear();
        }

        Entities.WithoutBurst().ForEach((ref AllyData allyData, ref Translation translation) =>
        {
            int gridIndex = GetGridIndex(translation.Value);
            if(Allies.ContainsKey(gridIndex) == false)
            {
                Allies.Add(gridIndex, new NativeList<AllyData>(Allocator.Persistent));
            }
            allyData.Position = translation.Value;
            Allies[gridIndex].Add(allyData);
        }).Run();

        foreach (var item in Enemies)
        {
            item.Value.Clear();
        }
        Agents.Clear();
        AgentNavigators.Clear();
        PolygonIDs.Clear();
        Entities.WithoutBurst().ForEach((ref EnemyData enemyData, ref Translation translation, ref CrowdAgent crowdAgent, ref CrowdAgentNavigator crowdAgentNavigator, ref PolygonIDContainer polygonIDs) =>
        {
            int gridIndex = GetGridIndex(translation.Value);
            if (Enemies.ContainsKey(gridIndex) == false)
            {
                Enemies.Add(gridIndex, new NativeList<EnemyData>(Allocator.Persistent));
            }
            enemyData.Position = translation.Value;
            Enemies[gridIndex].Add(enemyData);
            Agents.Add(crowdAgent);
            AgentNavigators.Add(crowdAgentNavigator);
            PolygonIDs.Add(polygonIDs);
        }).Run();

        
    }

    protected void SpawnAlly()
    {
        var commandBuffer = entityCommandBufferSystem.CreateCommandBuffer();

        Entities.WithoutBurst().ForEach((Entity entityCreate, ref SpawnerAllyData spawnerAllyData) =>
        {
            var entity = commandBuffer.Instantiate(spawnerAllyData.Prefab);
            commandBuffer.SetComponent(entity, new Translation { Value = spawnerAllyData.SpawnPosition });
            commandBuffer.AddComponent(entity, new TextureAnimatorData() { AnimSetID = (int)UnitType.Ally, NewAnimationId = AnimationName.Idle });
            commandBuffer.AddComponent(entity, new AllyData() { HP = 10, Range = 10, Entity = entity });
            commandBuffer.DestroyEntity(entityCreate);
        }).Run();
    }

    protected void SpawnEnemy()
    {
        // Clamp delta time so you can't overshoot.
        var deltaTime = math.min(Time.DeltaTime, 0.05f);
        var commandBuffer = entityCommandBufferSystem.CreateCommandBuffer();

        Entities.WithoutBurst().ForEach((ref SpawnerData spawnerData) =>
        {
            if(EnemyCount < spawnerData.MaxEnemiesCount)
            {
                var secondsUntilGenerate = spawnerData.SecondsUntilGenerate;
                secondsUntilGenerate -= deltaTime;
                if (secondsUntilGenerate <= 0.0f)
                {
                    float3 spawnPosition = spawnerData.SpawnPosition + new float3(UnityEngine.Random.insideUnitCircle, 0) * spawnerData.SpawnRange;
                    NavMeshLocation location = mapLocationQuery.MapLocation(spawnPosition, Vector3.one * 10, 0);
                    NavMeshLocation targetlocation = mapLocationQuery.MapLocation(spawnerData.GoalPosition, Vector3.one, 0);

                    var entity = commandBuffer.Instantiate(spawnerData.EnemyPrefab);

                    commandBuffer.SetComponent(entity, new Translation { Value = spawnerData.SpawnPosition });
                    commandBuffer.AddComponent(entity, new TextureAnimatorData() { AnimSetID = (int)UnitType.Enemy, NewAnimationId = AnimationName.Idle });
                    commandBuffer.AddComponent(entity, new EnemyData() { HP = 1, Range = 1, Entity = entity });
                    commandBuffer.AddComponent(entity, new CrowdAgent() { worldPosition = location.position, type = 0, location = location });
                    var crowd = new CrowdAgentNavigator()
                    {
                        active = 1,
                        newDestinationRequested = 0,
                        goToDestination = 0,
                        destinationInView = 0,
                        destinationReached = 1,
                        speed = 2,
                    };
                    crowd.MoveTo(targetlocation.position);
                    commandBuffer.AddComponent(entity, crowd);
                    //commandBuffer.AddBuffer<PolygonIDContainer>(entity);
                    commandBuffer.AddComponent(entity, new PolygonIDContainer() { Pool = CreatePolygonIDPool() });

                    secondsUntilGenerate = spawnerData.CoolDownSeconds;
                    ++EnemyCount;
                }

                spawnerData.SecondsUntilGenerate = secondsUntilGenerate;
            }
        }).Run();
    }

    protected override void OnDestroy()
    {
        foreach (var item in Allies)
        {
            if (item.Value.IsCreated)
            {
                item.Value.Dispose();
            }
        }
        foreach (var item in Enemies)
        {
            if (item.Value.IsCreated)
            {
                item.Value.Dispose();
            }
        }
        if(Agents.IsCreated)
        {
            Agents.Dispose();
        }
        if(AgentNavigators.IsCreated)
        {
            AgentNavigators.Dispose();
        }
        if(PolygonIDs.IsCreated)
        {
            PolygonIDs.Dispose();
        }
        mapLocationQuery.Dispose();

        base.OnDestroy();
    }

    public static int2 GetGridPos(float3 position)
    {
        return new int2((int)(position.x*0.1f), (int)(position.y*0.1f));
    }

    public static int GetGridIndex(float3 position)
    {
        int2 pos = GetGridPos(position);
        return pos.x * 10000 + pos.y;
    }

    public static int GetGridIndex(int2 pos)
    {
        return pos.x * 10000 + pos.y;
    }

    BlobAssetReference<PolygonIDPool> CreatePolygonIDPool()
    {
        var builder = new BlobBuilder(Allocator.Temp);
        ref PolygonIDPool hobbyPool = ref builder.ConstructRoot<PolygonIDPool>();

        // Allocate enough room for two hobbies in the pool. Use the returned BlobBuilderArray
        // to fill in the data.
        BlobBuilderArray<PolygonId> arrayBuilder = builder.Allocate(
            ref hobbyPool.IDs,
            10
        );

        var result = builder.CreateBlobAssetReference<PolygonIDPool>(Allocator.Persistent);
        builder.Dispose();
        return result;
    }


}