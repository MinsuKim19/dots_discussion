using UnityEngine;
using UnityEngine.Experimental.AI;

using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(AISystem))]
public partial class CrowdSystem : SystemBase
{
#if DEBUG_CROWDSYSTEM
    public bool drawDebug = true;
    public bool dbgPrintAgentCount = false;
    public bool dbgPrintRequests = false;
    public bool dbgCheckRequests = false;
    int dbgSampleTimespan = 50; //frames
#endif
    /*const int MaxQueryNodes = 2000;           // how many NavMesh nodes should the NavMeshQuery allocate space for
    const int MaxRequestsPerQuery = 100;      // how many requests should be stored by a query queue
    const int QueryCount = 7;                 // how many NavMesh queries can run in parallel - preferably the number of worker threads
    const int PathRequestsPerTick = 24;       // how many requests can be added to the query queues per tick
    const int MaxQueryIterationsPerTick = 100;// how many NavMesh nodes should the query process per tick
    const int AgentsBatchSize = 50;           // how many agents should be processed in one batched job*/

    const int MaxQueryNodes = 1;           // how many NavMesh nodes should the NavMeshQuery allocate space for
    const int MaxRequestsPerQuery = 1;      // how many requests should be stored by a query queue
    const int QueryCount = 1;                 // how many NavMesh queries can run in parallel - preferably the number of worker threads
    const int PathRequestsPerTick = 1;       // how many requests can be added to the query queues per tick
    const int MaxQueryIterationsPerTick = 1;// how many NavMesh nodes should the query process per tick
    const int AgentsBatchSize = 1;           // how many agents should be processed in one batched job

    NativeList<byte> PlanPathForAgent;
    NativeList<byte> EmptyPlanPathForAgent;
    NativeList<uint> PathRequestIdForAgent;
    NativeList<PathQueryQueueEcs.RequestEcs> PathRequests;
    NativeArray<int> PathRequestsRange;
    NativeArray<uint> UniqueIdStore;
    NativeArray<int> CurrentAgentIndex;


    NavMeshQuery NavMeshQuery;
    PathQueryQueueEcs[] QueryQueues;
    bool[] IsEmptyQueryQueue;
    UpdateQueriesJob[] QueryJobs;
    NativeArray<JobHandle> AfterQueriesProcessed;

    JobHandle AfterQueriesCleanup;
    JobHandle AfterMovedRequestsForgotten;

    int InitialCapacity;
    const int PRRStart = 0;
    const int PRRCount = 1;
    const int PRRDataSize = 2;

    protected override void OnCreate()
    {
        InitialCapacity = 2;
        Initialize(2);
        AfterQueriesCleanup = new JobHandle();
        AfterMovedRequestsForgotten = new JobHandle();
    }

    protected override void OnDestroy()
    {
        DisposeEverything();
        
        base.OnDestroy();
    }

    void Initialize(int capacity)
    {
        var world = NavMeshWorld.GetDefaultWorld();
        var queryCount = world.IsValid() ? QueryCount : 0;
        var agentCount = world.IsValid() ? capacity : 0;

        PlanPathForAgent = new NativeList<byte>(agentCount, Allocator.Persistent);
        EmptyPlanPathForAgent = new NativeList<byte>(0, Allocator.Persistent);
        PathRequestIdForAgent = new NativeList<uint>(agentCount, Allocator.Persistent);
        PathRequests = new NativeList<PathQueryQueueEcs.RequestEcs>(PathRequestsPerTick, Allocator.Persistent);
        PathRequests.ResizeUninitialized(PathRequestsPerTick);
        for (var i = 0; i < PathRequests.Length; i++)
        {
            PathRequests[i] = new PathQueryQueueEcs.RequestEcs { uid = PathQueryQueueEcs.RequestEcs.invalidId };
        }
        PathRequestsRange = new NativeArray<int>(PRRDataSize, Allocator.Persistent);
        PathRequestsRange[PRRStart] = 0;
        PathRequestsRange[PRRCount] = 0;
        UniqueIdStore = new NativeArray<uint>(1, Allocator.Persistent);
        CurrentAgentIndex = new NativeArray<int>(1, Allocator.Persistent);
        CurrentAgentIndex[0] = 0;


        NavMeshQuery = new NavMeshQuery(world, Allocator.Persistent);
        QueryQueues = new PathQueryQueueEcs[queryCount];
        QueryJobs = new UpdateQueriesJob[queryCount];
        AfterQueriesProcessed = new NativeArray<JobHandle>(queryCount, Allocator.Persistent);
        AfterQueriesCleanup = new JobHandle();
        AfterMovedRequestsForgotten = new JobHandle();
        IsEmptyQueryQueue = new bool[queryCount];

        for (var i = 0; i < QueryQueues.Length; i++)
        {
            QueryQueues[i] = new PathQueryQueueEcs(MaxQueryNodes, MaxRequestsPerQuery);
            QueryJobs[i] = new UpdateQueriesJob() { maxIterations = MaxQueryIterationsPerTick, queryQueue = QueryQueues[i] };
            AfterQueriesProcessed[i] = new JobHandle();
            IsEmptyQueryQueue[i] = true;
        }
    }

    void DisposeEverything()
    {
        AfterMovedRequestsForgotten.Complete();
        AfterQueriesCleanup.Complete();
        for (var i = 0; i < QueryQueues.Length; i++)
        {
            QueryQueues[i].Dispose();
        }
        PlanPathForAgent.Dispose();
        EmptyPlanPathForAgent.Dispose();
        PathRequestIdForAgent.Dispose();
        PathRequests.Dispose();
        PathRequestsRange.Dispose();
        AfterQueriesProcessed.Dispose();
        UniqueIdStore.Dispose();
        CurrentAgentIndex.Dispose();
        NavMeshQuery.Dispose();
    }

    void AddAgents(int numberOfAdded)
    {
        if (numberOfAdded > 0)
        {
            AfterMovedRequestsForgotten.Complete();
            AfterQueriesCleanup.Complete();

            AddAgentResources(numberOfAdded);
        }
    }

    public void AddAgentResources(int n)
    {
        if (n <= 0)
            return;

        var oldLength = PlanPathForAgent.Length;
        PlanPathForAgent.ResizeUninitialized(oldLength + n);
        PathRequestIdForAgent.ResizeUninitialized(PlanPathForAgent.Length);
        for (var i = oldLength; i < PlanPathForAgent.Length; i++)
        {
            PlanPathForAgent[i] = 0;
            PathRequestIdForAgent[i] = PathQueryQueueEcs.RequestEcs.invalidId;
        }
    }

    protected override void OnUpdate()
    {
        AfterQueriesCleanup.Complete();
        AfterMovedRequestsForgotten.Complete();

        if (QueryQueues.Length < QueryCount)
        {
            var world = NavMeshWorld.GetDefaultWorld();
            if (world.IsValid())
            {
                DisposeEverything();
                Initialize(InitialCapacity);
            }
        }

        var unitSystem = World.GetExistingSystem<UnitSystem>();
        //NativeList<CrowdAgent> agents = unitSystem.Agents;
        //NativeList<CrowdAgentNavigator> agentNavigators = unitSystem.AgentNavigators;
        //NativeList<PolygonIDContainer> paths = unitSystem.PolygonIDs;

        if (unitSystem.AgentNavigators.Length == 0)
            return;

        var missingAgents = unitSystem.AgentNavigators.Length - PlanPathForAgent.Length;
        if (missingAgents > 0)
        {
            AddAgents(missingAgents);
        }

#if DEBUG_CROWDSYSTEM
        if (drawDebug)
        {
            DrawDebug(unitSystem);
            DrawRequestsDebug();
        }
#endif
        var requestsPerQueue = int.MaxValue;
        if (QueryQueues.Length > 0)
        {
            int existingRequests = 0;
            foreach (var queue in QueryQueues)
            {
                existingRequests += queue.GetRequestCount();
            }
            var requestCount = existingRequests + PathRequestsRange[PRRCount];
            requestsPerQueue = requestCount / QueryQueues.Length;
            if (requestCount % QueryQueues.Length != 0 || requestsPerQueue == 0)
                requestsPerQueue += 1;
        }

        for (var i = 0; i < QueryQueues.Length; i++)
        {
            IsEmptyQueryQueue[i] = QueryQueues[i].IsEmpty();
        }

        var makeRequestsJob = new MakePathRequestsJob
        {
            query = NavMeshQuery,
            agents = unitSystem.Agents,
            agentNavigators = unitSystem.AgentNavigators,
            planPathForAgent = EmptyPlanPathForAgent,
            pathRequestIdForAgent = PathRequestIdForAgent,
            pathRequests = PathRequests,
            pathRequestsRange = PathRequestsRange,
            currentAgentIndex = CurrentAgentIndex,
            uniqueIdStore = UniqueIdStore
        };
        var afterRequestsCreated = makeRequestsJob.Schedule();
        var navMeshWorld = NavMeshWorld.GetDefaultWorld();
        navMeshWorld.AddDependency(afterRequestsCreated);

        var afterRequestsMovedToQueries = afterRequestsCreated;
        if (QueryQueues.Length > 0)
        {
            foreach (var queue in QueryQueues)
            {
                var enqueuingJob = new EnqueueRequestsInQueriesJob
                {
                    pathRequests = PathRequests,
                    pathRequestsRange = PathRequestsRange,
                    maxRequestsInQueue = requestsPerQueue,
                    queryQueue = queue
                };
                afterRequestsMovedToQueries = enqueuingJob.Schedule(afterRequestsMovedToQueries);
                navMeshWorld.AddDependency(afterRequestsMovedToQueries);
            }
        }

        var forgetMovedRequestsJob = new ForgetMovedRequestsJob
        {
            pathRequests = PathRequests,
            pathRequestsRange = PathRequestsRange
        };
        AfterMovedRequestsForgotten = forgetMovedRequestsJob.Schedule(afterRequestsMovedToQueries);

        var queriesScheduled = 0;
        for (var i = 0; i < QueryJobs.Length; ++i)
        {
            if (IsEmptyQueryQueue[i])
                continue;

            AfterQueriesProcessed[i] = QueryJobs[i].Schedule(afterRequestsMovedToQueries);
            navMeshWorld.AddDependency(AfterQueriesProcessed[i]);
            queriesScheduled++;
        }
        var afterQueriesProcessed = queriesScheduled > 0 ? JobHandle.CombineDependencies(AfterQueriesProcessed) : afterRequestsMovedToQueries;

        var afterPathsAdded = afterQueriesProcessed;
        foreach (var queue in QueryQueues)
        {
            var resultsJob = new ApplyQueryResultsJob { queryQueue = queue, paths = unitSystem.PolygonIDs, agentNavigators = unitSystem.AgentNavigators };
            afterPathsAdded = resultsJob.Schedule(afterPathsAdded);
            navMeshWorld.AddDependency(afterPathsAdded);
        }

        var advance = new AdvancePathJob { agents = unitSystem.Agents, agentNavigators = unitSystem.AgentNavigators, paths = unitSystem.PolygonIDs };
        var afterPathsTrimmed = advance.Schedule(unitSystem.Agents.Length, AgentsBatchSize, afterPathsAdded);
        
        const int maxCornersPerAgent = 2;
        var totalCornersBuffer = unitSystem.Agents.Length * maxCornersPerAgent;
        var vel = new UpdateVelocityJob
        {
            query = NavMeshQuery,
            agents = unitSystem.Agents,
            agentNavigators = unitSystem.AgentNavigators,
            paths = unitSystem.PolygonIDs,
            straightPath = new NativeArray<NavMeshLocation>(totalCornersBuffer, Allocator.TempJob),
            straightPathFlags = new NativeArray<StraightPathFlags>(totalCornersBuffer, Allocator.TempJob),
            vertexSide = new NativeArray<float>(totalCornersBuffer, Allocator.TempJob)
        };
        var afterVelocitiesUpdated = vel.Schedule(afterPathsTrimmed);
        navMeshWorld.AddDependency(afterVelocitiesUpdated);

        var move = new MoveLocationsJob { query = NavMeshQuery, agents = unitSystem.Agents, dt = UnityEngine.Time.deltaTime };
        var afterAgentsMoved = move.Schedule(afterVelocitiesUpdated);
        navMeshWorld.AddDependency(afterAgentsMoved);

#if DEBUG_CROWDSYSTEM_LOGS
        if (dbgPrintRequests)
        {
            afterPathsAdded.Complete();
            PrintRequestsDebug();
        }
#endif

        var cleanupFence = afterPathsAdded;
        foreach (var queue in QueryQueues)
        {
            var queryCleanupJob = new QueryCleanupJob
            {
                queryQueue = queue,
                pathRequestIdForAgent = PathRequestIdForAgent
            };
            cleanupFence = queryCleanupJob.Schedule(cleanupFence);
            AfterQueriesCleanup = cleanupFence;
            navMeshWorld.AddDependency(cleanupFence);
        }

        afterAgentsMoved.Complete();
    }

#if DEBUG_CROWDSYSTEM
    void DrawDebug(UnitSystem unitSystem)
    {
        var activeAgents = 0;
        for (var i = 0; i < unitSystem.Agents.Length; ++i)
        {
            var agent = unitSystem.Agents[i];
            var agentNavigator = unitSystem.AgentNavigators[i];
            float3 offset = 0.5f * Vector3.up;

            if (agentNavigator.active == 0)
            {
                Debug.DrawRay(agent.worldPosition, 2.0f * Vector3.up, Color.cyan, 2f);
                Debug.DrawRay((Vector3)agent.worldPosition + 2.0f * Vector3.up - 0.4f * Vector3.right, 0.8f * Vector3.right, Color.cyan);
                continue;
            }
            activeAgents++;

            //Debug.DrawRay(agent.worldPosition + offset, agent.velocity, Color.cyan);

            if (agentNavigator.pathSize == 0 || PlanPathForAgent[i] > 0 || agentNavigator.newDestinationRequested > 0 || PathRequestIdForAgent[i] != PathQueryQueueEcs.RequestEcs.invalidId)
            {
                var requestInProcess = PathRequestIdForAgent[i] != PathQueryQueueEcs.RequestEcs.invalidId;
                var stateColor = requestInProcess ? Color.yellow : ((PlanPathForAgent[i] >0 || agentNavigator.newDestinationRequested > 0) ? Color.magenta : Color.red);
                Debug.DrawRay(agent.worldPosition + offset, 0.5f * Vector3.up, stateColor, 2f);
                continue;
            }

            offset = 0.9f * offset;
            float3 pathEndPos = agentNavigator.pathEnd.position;
            Debug.DrawLine(agent.worldPosition + offset, pathEndPos, Color.black);

            if (agentNavigator.destinationInView > 0)
            {
                Debug.DrawLine(agent.worldPosition + offset, agentNavigator.requestedDestination, Color.white, 2f);
            }
            else
            {
                Debug.DrawLine(agent.worldPosition + offset, agentNavigator.steeringTarget + offset, Color.white, 2f);
                Debug.DrawLine(agentNavigator.steeringTarget + offset, agentNavigator.requestedDestination, Color.gray, 2f);
            }
        }
    }

    void DrawRequestsDebug()
    {
        foreach (var queue in QueryQueues)
        {
            NativeArray<PathQueryQueueEcs.RequestEcs> requestQueue;
            PathQueryQueueEcs.RequestEcs inProgress;
            int countWaiting;
            int countDone;
            queue.DbgGetRequests(out requestQueue, out countWaiting, out countDone, out inProgress);

            var hasRequestInProgress = inProgress.uid != PathQueryQueueEcs.RequestEcs.invalidId;
            if (hasRequestInProgress)
            {
                DrawDebugArrow(inProgress.start, inProgress.end, 1.0f, Color.green);
            }

            for (var j = 0; j < countDone + countWaiting; j++)
            {
                if (hasRequestInProgress && j == countDone - 1)
                    continue;

                var isDone = j < countDone;
                var color = isDone ? Color.black : Color.yellow;
                var height = isDone ? 1.3f : 0.7f;
                var req = requestQueue[j];
                DrawDebugArrow(req.start, req.end, height, color);
            }

            var rangeEnd = PathRequestsRange[PRRStart] + PathRequestsRange[PRRCount];
            for (var k = PathRequestsRange[PRRStart]; k < rangeEnd; k++)
            {
                var req = PathRequests[k];
                DrawDebugArrow(req.start, req.end, 0.35f, Color.red);
            }
        }
    }

    static void DrawDebugArrow(Vector3 start, Vector3 end, float height, Color color)
    {
        var upCorner = start + height * Vector3.up;
        Debug.DrawLine(upCorner, start, color, 2f);
        Debug.DrawLine(upCorner, end, color, 2f);
        Debug.DrawLine(start, end, color, 2f);
    }
#endif
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CrowdSystem))]
public partial class CrowdAgentsToTransformSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Entities.WithoutBurst().ForEach((ref EnemyData enemyData, ref Translation translation, ref CrowdAgent crowdAgent) =>
        {
            Debug.Log("translation.Value = " + translation.Value + " , crowdAgent.worldPosition = " + crowdAgent.worldPosition);
            translation.Value = crowdAgent.worldPosition;
        }).Run();
    }
}
