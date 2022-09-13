using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.AI;

public struct PathQueryQueueEcs
{
    public struct RequestEcs
    {
        public Vector3 start;
        public Vector3 end;
        public int agentIndex;
        public int agentType;
        public int mask;
        public uint uid;

        public const uint invalidId = 0;
    }

    struct QueryQueueState
    {
        public int requestCount;
        public int requestIndex;
        public int resultNodesCount;
        public int resultPathsCount;
        public int currentAgentIndex;
        public PathInfo currentPathRequest;
    }

    struct PathInfo
    {
        public int begin;
        public int size;
        public NavMeshLocation start;
        public NavMeshLocation end;
    }

    NavMeshQuery NMQuery;
    NativeArray<RequestEcs> Requests;
    NativeArray<PolygonId> ResultNodes;
    NativeArray<PathInfo> ResultRanges;
    NativeArray<int> AgentIndices;
    NativeArray<float> Costs;
    NativeArray<QueryQueueState> QueryStates;

    public PathQueryQueueEcs(int nodePoolSize, int maxRequestCount)
    {
        var world = NavMeshWorld.GetDefaultWorld();
        NMQuery = new NavMeshQuery(world, Allocator.Persistent, nodePoolSize);
        Requests = new NativeArray<RequestEcs>(maxRequestCount, Allocator.Persistent);
        ResultNodes = new NativeArray<PolygonId>(2 * nodePoolSize, Allocator.Persistent);
        ResultRanges = new NativeArray<PathInfo>(maxRequestCount + 1, Allocator.Persistent);
        AgentIndices = new NativeArray<int>(maxRequestCount + 1, Allocator.Persistent);
        Costs = new NativeArray<float>(32, Allocator.Persistent);
        for (var i = 0; i < Costs.Length; ++i)
            Costs[i] = 1.0f;

        QueryStates = new NativeArray<QueryQueueState>(1, Allocator.Persistent);
        QueryStates[0] = new QueryQueueState()
        {
            requestCount = 0,
            requestIndex = 0,
            resultNodesCount = 0,
            resultPathsCount = 0,
            currentAgentIndex = -1,
            currentPathRequest = new PathInfo()
        };
    }

    public void Dispose()
    {
        if (Requests.IsCreated)
        {
            Requests.Dispose();
        }
        if (ResultNodes.IsCreated)
        {
            ResultNodes.Dispose();
        }
        if (ResultRanges.IsCreated)
        {
            ResultRanges.Dispose();
        }
        if (AgentIndices.IsCreated)
        {
            AgentIndices.Dispose();
        }
        if (Costs.IsCreated)
        {
            Costs.Dispose();
        }
        if (QueryStates.IsCreated)
        {
            QueryStates.Dispose();
        }
        NMQuery.Dispose();
    }

    public bool Enqueue(RequestEcs request)
    {
        var state = QueryStates[0];
        if (state.requestCount == Requests.Length)
            return false;

        Requests[state.requestCount] = request;
        state.requestCount++;
        QueryStates[0] = state;

        return true;
    }

    public int GetRequestCount()
    {
        return QueryStates[0].requestCount;
    }

    public int GetProcessedRequestsCount()
    {
        return QueryStates[0].requestIndex;
    }

    public bool IsEmpty()
    {
        var state = QueryStates[0];
        return state.requestCount == 0 && state.currentAgentIndex < 0;
    }

    public bool HasRequestForAgent(int index)
    {
        var state = QueryStates[0];
        if (state.currentAgentIndex == index)
            return true;

        for (var i = 0; i < state.requestCount; i++)
        {
            if (Requests[state.requestIndex + i].agentIndex == index)
                return true;
        }

        return false;
    }

    public int GetResultPathsCount()
    {
        return QueryStates[0].resultPathsCount;
    }

    public void CopyResultsTo(in NativeList<PolygonIDContainer> agentPaths, ref NativeList<CrowdAgentNavigator> agentNavigators)
    {
        var state = QueryStates[0];
        for (var i = 0; i < state.resultPathsCount; i++)
        {
            var index = AgentIndices[i];
            var resultPathInfo = ResultRanges[i];
            var resultNodes = new NativeSlice<PolygonId>(ResultNodes, resultPathInfo.begin, resultPathInfo.size);
            var agentPathBuffer = agentPaths[index];

            var pathLength = math.min(resultNodes.Length, agentPathBuffer.Pool.Value.IDs.Length);
            for (var j = 0; j < pathLength; j++)
            {
                var apb = agentPathBuffer.Pool.Value.IDs[j];
                apb = resultNodes[j];
                agentPathBuffer.Pool.Value.IDs[j] = apb;
            }

            var navigator = agentNavigators[index];
            navigator.pathStart = resultPathInfo.start;
            navigator.pathEnd = resultPathInfo.end;
            navigator.pathSize = pathLength;
            navigator.StartMoving();
            agentNavigators[index] = navigator;
        }
    }

    public void ClearResults()
    {
        var state = QueryStates[0];
        state.resultNodesCount = 0;
        state.resultPathsCount = 0;
        QueryStates[0] = state;
    }

    public void RemoveAgentRecords(int index, int replacementAgent)
    {
#if DEBUG_CROWDSYSTEM_ASSERTS
        Debug.Assert(index >= 0);
#endif

        var stateChanged = false;
        var state = QueryStates[0];
        if (state.currentAgentIndex == index)
        {
            state.currentAgentIndex = -1;
            stateChanged = true;
        }
        else if (state.currentAgentIndex == replacementAgent)
        {
            state.currentAgentIndex = index;
            stateChanged = true;
        }

        // remove results for that agent
        for (var i = 0; i < state.resultPathsCount; i++)
        {
            if (AgentIndices[i] == index)
            {
                var backIndex = state.resultPathsCount - 1;
                if (i != backIndex)
                {
                    ResultRanges[i] = ResultRanges[backIndex];
                    AgentIndices[i] = AgentIndices[backIndex];
                }
                state.resultPathsCount--;
                stateChanged = true;
                i--; // rewinds i one step back to account for the newly moved item
            }
            else if (AgentIndices[i] == replacementAgent)
            {
                AgentIndices[i] = index;
            }
        }

        //remove requests in queue for that agent
        for (var q = 0; q < state.requestCount; q++)
        {
            var i = state.requestIndex + q;
            if (Requests[i].agentIndex == index)
            {
                var backIndex = state.requestIndex + state.requestCount - 1;
                if (i != backIndex)
                {
                    Requests[i] = Requests[backIndex];
                }
                state.requestCount--;
                stateChanged = true;
                q--;
            }
            else if (Requests[i].agentIndex == replacementAgent)
            {
                var req = Requests[i];
                req.agentIndex = index;
                Requests[i] = req;
            }
        }

        if (stateChanged)
        {
            QueryStates[0] = state;
        }
    }

    public void UpdateTimesliced(int maxIter = 100)
    {
        var state = QueryStates[0];
        while (maxIter > 0 && (state.currentAgentIndex >= 0 || state.requestCount > 0 && state.requestIndex < state.requestCount))
        {
            if (state.currentAgentIndex < 0 && state.requestCount > 0 && state.requestIndex < state.requestCount)
            {
                // Initialize a new query
                var request = Requests[state.requestIndex];
                request.uid = RequestEcs.invalidId;
                Requests[state.requestIndex] = request;
                state.requestIndex++;
                var startLoc = NMQuery.MapLocation(request.start, 10.0f * Vector3.one, 0, request.mask);
                var endLoc = NMQuery.MapLocation(request.end, 10.0f * Vector3.one, 0, request.mask);
                if (!NMQuery.IsValid(startLoc) || !NMQuery.IsValid(endLoc))
                    continue;

                state.currentPathRequest = new PathInfo()
                {
                    begin = 0,
                    size = 0,
                    start = startLoc,
                    end = endLoc
                };

                var status = NMQuery.BeginFindPath(startLoc, endLoc, request.mask, Costs);
                if (status != PathQueryStatus.Failure)
                {
                    state.currentAgentIndex = request.agentIndex;
                }
            }

            if (state.resultPathsCount >= ResultRanges.Length)
                break;

            if (state.currentAgentIndex >= 0)
            {
                // Continue existing query
                int niter;
                var status = NMQuery.UpdateFindPath(maxIter, out niter);
                maxIter -= niter;

                if ((status & PathQueryStatus.Success) > 0)
                {
                    int npath;
                    status = NMQuery.EndFindPath(out npath);
                    if ((status & PathQueryStatus.Success) > 0)
                    {
                        var resPolygons = new NativeArray<PolygonId>(npath, Allocator.Temp);
                        var pathInfo = state.currentPathRequest;
                        pathInfo.size = NMQuery.GetPathResult(resPolygons);
                        if (pathInfo.size > 0)
                        {
                            pathInfo.begin = state.resultNodesCount;
                            for (var i = 0; i < npath; i++)
                            {
                                ResultNodes[state.resultNodesCount] = resPolygons[i];
                                state.resultNodesCount++;
                            }
                            ResultRanges[state.resultPathsCount] = pathInfo;
                            AgentIndices[state.resultPathsCount] = state.currentAgentIndex;
                            state.resultPathsCount++;
                        }
                        state.currentPathRequest = pathInfo;
                        resPolygons.Dispose();
                    }
                }

                if (status != PathQueryStatus.InProgress)
                {
                    state.currentAgentIndex = -1;
                }
            }
        }

        QueryStates[0] = state;
    }

    public void CleanupProcessedRequests(ref NativeArray<uint> pathRequestIdForAgent)
    {
        var state = QueryStates[0];
        if (state.requestIndex > 0)
        {
            for (var i = 0; i < state.requestIndex; i++)
            {
                var req = Requests[i];
                if (req.uid == RequestEcs.invalidId || req.uid == pathRequestIdForAgent[req.agentIndex])
                {
                    pathRequestIdForAgent[req.agentIndex] = RequestEcs.invalidId;
                }
            }

            var dst = 0;
            var src = state.requestIndex;
            for (; src < state.requestCount; src++, dst++)
            {
                Requests[dst] = Requests[src];
            }
            state.requestCount -= state.requestIndex;
            state.requestIndex = 0;

            QueryStates[0] = state;
        }
    }

    public void DbgGetRequests(out NativeArray<RequestEcs> requestQueue, out int countWaiting, out int countDone, out RequestEcs inProgress)
    {
        requestQueue = Requests;
        var state = QueryStates[0];
        countWaiting = state.requestCount - state.requestIndex;
        countDone = state.requestIndex;
        inProgress = new RequestEcs
        {
            uid = state.currentAgentIndex >= 0 ? uint.MaxValue : RequestEcs.invalidId,
            agentIndex = state.currentAgentIndex,
            start = state.currentPathRequest.start.position,
            end = state.currentPathRequest.end.position
        };
    }

    public bool DbgRequestExistsInQueue(uint requestUid)
    {
        var existsInQ = false;
        var state = QueryStates[0];
        for (var i = state.requestIndex; i < state.requestCount; ++i)
        {
            existsInQ = (Requests[i].uid == requestUid);
            if (existsInQ)
                break;
        }

        return existsInQ;
    }
}
