using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class RoamingLight : MonoBehaviour
{
    public float Speed;
    public float Range;
    public Vector3 Direction;
}


struct RoamingLightData : IComponentData
{
    public float Speed;
    public float Range;
    public Vector3 Direction;
}
