using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(UnitSystem))]
public partial class AISystem : SystemBase
{
    protected override void OnUpdate()
    {
        //var unitSystem = World.GetExistingSystem<UnitSystem>();
        
    }
}
