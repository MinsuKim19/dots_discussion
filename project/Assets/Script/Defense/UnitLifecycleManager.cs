using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using UnityEngine.Profiling;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[UpdateAfter(typeof(ProjectileSystem))]
public partial class UnitLifecycleManager : SystemBase
{
    protected override void OnUpdate()
    {
    }
}