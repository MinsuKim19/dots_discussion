using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Experimental.AI;

public struct CrowdAgent : IComponentData
{
    public int type;    // NavMeshAgent type
    public float3 worldPosition;
    public float3 velocity;
    public NavMeshLocation location;
}

public struct CrowdAgentNavigator : IComponentData
{
    public float3 requestedDestination;
    public NavMeshLocation requestedDestinationLocation;
    public float distanceToDestination;
    public NavMeshLocation pathStart;
    public NavMeshLocation pathEnd;
    public int pathSize;
    public float speed;
    public float nextCornerSide;
    public float3 steeringTarget;
    public byte newDestinationRequested;
    public byte goToDestination;
    public byte destinationInView;
    public byte destinationReached;
    public byte active;

    public void MoveTo(float3 dest)
    {
        requestedDestination = dest;
        newDestinationRequested = 1;
    }

    public void StartMoving()
    {
        goToDestination = 1;
        destinationInView = 0;
        destinationReached = 0;
        distanceToDestination = -1f;
    }
}

public struct PolygonIDPool
{
    public BlobArray<PolygonId> IDs;
}

public struct PolygonIDContainer : IComponentData
{
    public BlobAssetReference<PolygonIDPool> Pool;
}