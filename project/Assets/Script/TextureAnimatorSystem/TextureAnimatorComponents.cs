using Unity.Mathematics;
using Unity.Entities;

[System.Serializable]
public struct TextureAnimatorData : IComponentData
{
    public float AnimationNormalizedTime;

    public int CurrentAnimationId;
    public int NewAnimationId;

    public int AnimSetID;

    public float AnimationSpeedVariation;
}
