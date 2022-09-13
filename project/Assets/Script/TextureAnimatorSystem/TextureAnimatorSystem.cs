using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Rendering;
using Unity.Transforms;

public struct AnimationName
{
    public const int Run = 0;
    public const int Idle = 1;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CrowdAgentsToTransformSystem))]
public partial class TextureAnimatorSystem : SystemBase
{
    private NativeArray<AnimationClipDataBaked> animationClipData;


    public List<DataPerAnimSetID> perUnitTypeDataHolder;
    private JobHandle previousFrameFence;

    public int lod0Count;

    public class DataPerAnimSetID
    {
        public int AnimSetID;
        public int TotalCount;
        public KeyframeTextureBaker.BakedData BakedData;

        public InstancedSkinningDrawer Drawer;
        //public NativeArray<IntPtr> BufferPointers;
        public NativeList<TextureAnimatorData> Animations;
        public NativeList<Translation> Translations;
        public NativeList<Rotation> Rotations;

        public Material[] Materials;
        public int Count;

        public void Dispose()
        {
            if (Drawer != null) Drawer.Dispose();
            //if (BufferPointers.IsCreated) BufferPointers.Dispose();
            if (Animations.IsCreated) Animations.Dispose();
            if (Translations.IsCreated) Translations.Dispose();
            if (Rotations.IsCreated) Rotations.Dispose();
        }
    }

    public struct AnimationClipDataBaked
    {
        public float TextureOffset;
        public float TextureRange;
        public float OnePixelOffset;
        public int TextureWidth;

        public float AnimationLength;
        public int Looping;
    }

    #region JOBS
    /*[BurstCompile]
    struct PrepareAnimatorDataJob : IJobParallelFor
    {
        public ComponentDataArray<TextureAnimatorData> textureAnimatorData;

        [NativeFixedLength(100)]
        [ReadOnly]
        public NativeArray<AnimationClipDataBaked> animationClips;

        public float dt;

        public void Execute(int i)
        {
            // CHECK: We can't modify values inside of a struct directly?
            var animatorData = textureAnimatorData[i];

            if (animatorData.CurrentAnimationId != animatorData.NewAnimationId)
            {
                animatorData.CurrentAnimationId = animatorData.NewAnimationId;
                animatorData.AnimationNormalizedTime = 0f;
            }

            AnimationClipDataBaked clip = animationClips[(int)animatorData.UnitType * 25 + animatorData.CurrentAnimationId];
            float normalizedTime = textureAnimatorData[i].AnimationNormalizedTime + dt / clip.AnimationLength;
            if (normalizedTime > 1.0f)
            {
                if (clip.Looping == 1) normalizedTime = normalizedTime % 1.0f;
                else normalizedTime = 1f;
            }

            animatorData.AnimationNormalizedTime = normalizedTime;

            textureAnimatorData[i] = animatorData;
        }
    }*/
    struct CullAndComputeParametersSafe : IJob
    {
        [ReadOnly]
        public NativeList<TextureAnimatorData> textureAnimatorData;

        [ReadOnly]
        public NativeList<Translation> translations;

        [ReadOnly]
        public NativeList<Rotation> rotations;

        [NativeFixedLength(100)]
        [ReadOnly]
        public NativeArray<AnimationClipDataBaked> animationClips;

        [ReadOnly]
        public float dt;

        public NativeList<float4> Lod0Positions;
        public NativeList<quaternion> Lod0Rotations;
        public NativeList<float3> Lod0TexturePositions;

        public void Execute()
        {
            for (int i = 0; i < translations.Length; i++)
            {
                var translation = translations[i];
                var rotation = rotations[i];

                var animatorData = textureAnimatorData[i];

                AnimationClipDataBaked clip = animationClips[(int)animatorData.AnimSetID * 25 + animatorData.CurrentAnimationId];
                //Quaternion rotation = Quaternion.LookRotation(unitTransform.Forward, new Vector3(0.0f, 1.0f, 0.0f));
                Quaternion rotationQ = rotation.Value;
                float texturePosition = textureAnimatorData[i].AnimationNormalizedTime * clip.TextureRange + clip.TextureOffset;
                int lowerPixelInt = (int)math.floor(texturePosition * clip.TextureWidth);

                float lowerPixelCenter = (lowerPixelInt * 1.0f) / clip.TextureWidth;
                float upperPixelCenter = lowerPixelCenter + clip.OnePixelOffset;
                float lerpFactor = (texturePosition - lowerPixelCenter) / clip.OnePixelOffset;
                float3 texturePositionData = new float3(lowerPixelCenter, upperPixelCenter, lerpFactor);

                float4 position = new float4(translation.Value, 1);

                Lod0Positions.Add(position);
                Lod0Rotations.Add(rotationQ);
                Lod0TexturePositions.Add(texturePositionData);
            }
        }
    }
    #endregion

    private void InstantiatePerUnitTypeData(int animSetID, GameObject prefab, GameObject prefabForInstance)
    {
        //var minionPrefab = prefab;
        //var renderingData = minionPrefab.GetComponentInChildren<RenderingDataWrapper>().Value;
        var bakingObject = GameObject.Instantiate(prefab);
        SkinnedMeshRenderer renderer = bakingObject.GetComponentInChildren<SkinnedMeshRenderer>();

        var dataPerUnitType = new DataPerAnimSetID
        {
            AnimSetID = animSetID,
            BakedData = KeyframeTextureBaker.BakeClips(renderer, GetAllAnimationClips(bakingObject.GetComponentInChildren<Animation>())),
            Materials = prefabForInstance.GetComponent<MeshRenderer>().sharedMaterials,
        };
        dataPerUnitType.Drawer = new InstancedSkinningDrawer(dataPerUnitType, dataPerUnitType.BakedData.NewMesh);
        dataPerUnitType.Animations = new NativeList<TextureAnimatorData>(Allocator.Persistent);
        dataPerUnitType.Translations = new NativeList<Translation>(Allocator.Persistent);
        dataPerUnitType.Rotations = new NativeList<Rotation>(Allocator.Persistent);

        perUnitTypeDataHolder.Add(dataPerUnitType);
        TransferAnimationData(animSetID);
        GameObject.Destroy(bakingObject);
    }

    private void TransferAnimationData(int animSetID)
    {
        var bakedData = perUnitTypeDataHolder.Find(x => x.AnimSetID == animSetID).BakedData;
        for (int i = 0; i < bakedData.Animations.Count; i++)
        {
            AnimationClipDataBaked data = new AnimationClipDataBaked();
            data.AnimationLength = bakedData.Animations[i].Clip.length;
            GetTextureRangeAndOffset(bakedData, bakedData.Animations[i], out data.TextureRange, out data.TextureOffset, out data.OnePixelOffset, out data.TextureWidth);
            data.Looping = (bakedData.Animations[i].Clip.wrapMode == WrapMode.Loop)?1:0;
            animationClipData[(int)animSetID * 25 + i] = data;
        }
    }

    private AnimationClip[] GetAllAnimationClips(Animation animation)
    {
        List<AnimationClip> animationClips = new List<AnimationClip>();
        foreach (AnimationState state in animation)
        {
            animationClips.Add(state.clip);
        }
        return animationClips.ToArray();
    }

    private void GetTextureRangeAndOffset(KeyframeTextureBaker.BakedData bakedData, KeyframeTextureBaker.AnimationClipData clipData, out float range, out float offset, out float onePixelOffset, out int textureWidth)
    {
        float onePixel = 1f / bakedData.Texture0.width;
        float start = (float)clipData.PixelStart / bakedData.Texture0.width + onePixel * 0.5f;
        float end = (float)clipData.PixelEnd / bakedData.Texture0.width + onePixel * 0.5f;
        onePixelOffset = onePixel;
        textureWidth = bakedData.Texture0.width;
        range = end - start;
        offset = start;
    }

    protected override void OnCreate()
    {
        animationClipData = new NativeArray<AnimationClipDataBaked>(100, Allocator.Persistent);
        perUnitTypeDataHolder = new List<DataPerAnimSetID>();
    }

    protected override void OnStartRunning()
    {
        InstantiatePerUnitTypeData((int)UnitType.Ally, DefenseSetting.Instance.AllyBakingPrefab, DefenseSetting.Instance.AllyPrefab);
        InstantiatePerUnitTypeData((int)UnitType.Enemy, DefenseSetting.Instance.EnemyBakingPrefab, DefenseSetting.Instance.EnemyPrefab);
    }

    private void ComputeFences(NativeList<TextureAnimatorData> textureAnimatorDataForUnitType, float dt, DataPerAnimSetID data, NativeArray<JobHandle> jobHandles, int index)
    {
		data.Drawer.ObjectPositions.Clear();
		data.Drawer.ObjectRotations.Clear();
		data.Drawer.TextureCoordinates.Clear();

		var cullAndComputeJob = new CullAndComputeParametersSafe()
		{
			translations = data.Translations,
            rotations = data.Rotations,
            textureAnimatorData = textureAnimatorDataForUnitType,
			animationClips = animationClipData,
			dt = dt,
			Lod0Positions = data.Drawer.ObjectPositions,
			Lod0Rotations = data.Drawer.ObjectRotations,
			Lod0TexturePositions = data.Drawer.TextureCoordinates,
		};
		var computeShaderJobFence0 = cullAndComputeJob.Schedule();
		jobHandles[index] = computeShaderJobFence0;
    }

    protected override void OnUpdate()
    {
        float dt = Time.DeltaTime;

        if (perUnitTypeDataHolder != null)
        {
            previousFrameFence.Complete();

            for(int i = 0; i < perUnitTypeDataHolder.Count; ++i)
            {
                var data = perUnitTypeDataHolder[i];
                data.Drawer.Draw();
                data.Animations.Clear();
                data.Translations.Clear();
                data.Rotations.Clear();
            }

            Entities.WithoutBurst().ForEach((ref TextureAnimatorData animatorData, ref Translation translation, ref Rotation rotation) =>
            {
                if (animatorData.CurrentAnimationId != animatorData.NewAnimationId)
                {
                    animatorData.CurrentAnimationId = animatorData.NewAnimationId;
                    animatorData.AnimationNormalizedTime = 0f;
                }

                AnimationClipDataBaked clip = animationClipData[(int)animatorData.AnimSetID * 25 + animatorData.CurrentAnimationId];
                float normalizedTime = animatorData.AnimationNormalizedTime + dt / clip.AnimationLength;
                if (normalizedTime > 1.0f)
                {
                    if (clip.Looping == 1) normalizedTime = normalizedTime % 1.0f;
                    else normalizedTime = 1f;
                }

                animatorData.AnimationNormalizedTime = normalizedTime;

                perUnitTypeDataHolder[animatorData.AnimSetID].Animations.Add(animatorData);
                perUnitTypeDataHolder[animatorData.AnimSetID].Translations.Add(translation);
                perUnitTypeDataHolder[animatorData.AnimSetID].Rotations.Add(rotation);
            }).Run();

            NativeArray<JobHandle> jobHandles = new NativeArray<JobHandle>(perUnitTypeDataHolder.Count, Allocator.Temp);
            for (int i = 0; i < perUnitTypeDataHolder.Count; ++i)
            {
                var data = perUnitTypeDataHolder[i];
                ComputeFences(data.Animations, dt, data, jobHandles, i);
                data.Count = data.Animations.Length;
            }

            previousFrameFence = JobHandle.CombineDependencies(jobHandles);
            jobHandles.Dispose();
        }
    }

    protected override void OnDestroy()
    {
        previousFrameFence.Complete();

        if (perUnitTypeDataHolder != null)
        {
            foreach(var data in perUnitTypeDataHolder)
            {
                data.Dispose();
            }
        }

        if(animationClipData.IsCreated)
        {
            animationClipData.Dispose();
        }

        base.OnDestroy();
    }
}

public class InstancedSkinningDrawer : IDisposable
{
    private const int PreallocatedBufferSize = 32 * 1024;

    private ComputeBuffer argsBuffer;

    private readonly uint[] indirectArgs = new uint[5] { 0, 0, 0, 0, 0 };

    private ComputeBuffer textureCoordinatesBuffer;
    private ComputeBuffer objectRotationsBuffer;
    private ComputeBuffer objectPositionsBuffer;

    public NativeList<float3> TextureCoordinates;
    public NativeList<float4> ObjectPositions;
    public NativeList<quaternion> ObjectRotations;

    private Material material;

    private Mesh mesh;

    private TextureAnimatorSystem.DataPerAnimSetID data;

    public InstancedSkinningDrawer(TextureAnimatorSystem.DataPerAnimSetID data, Mesh meshToDraw)
    {
        this.data = data;
        this.mesh = meshToDraw;
        this.material = new Material(data.Materials[0]);

        argsBuffer = new ComputeBuffer(1, indirectArgs.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        indirectArgs[0] = mesh.GetIndexCount(0);
        indirectArgs[1] = (uint)0;
        argsBuffer.SetData(indirectArgs);

        objectRotationsBuffer = new ComputeBuffer(PreallocatedBufferSize, 16);
        objectPositionsBuffer = new ComputeBuffer(PreallocatedBufferSize, 16);
        textureCoordinatesBuffer = new ComputeBuffer(PreallocatedBufferSize, 12);

		TextureCoordinates = new NativeList<float3>(PreallocatedBufferSize, Allocator.Persistent);
		ObjectPositions = new NativeList<float4>(PreallocatedBufferSize, Allocator.Persistent);
		ObjectRotations = new NativeList<quaternion>(PreallocatedBufferSize, Allocator.Persistent);

        material.SetBuffer("textureCoordinatesBuffer", textureCoordinatesBuffer);
        material.SetBuffer("objectPositionsBuffer", objectPositionsBuffer);
        material.SetBuffer("objectRotationsBuffer", objectRotationsBuffer);
        material.SetTexture("_AnimationTexture0", data.BakedData.Texture0);
        material.SetTexture("_AnimationTexture1", data.BakedData.Texture1);
        material.SetTexture("_AnimationTexture2", data.BakedData.Texture2);
    }

    public void Dispose()
    {
        if (argsBuffer != null) argsBuffer.Dispose();
        if (objectPositionsBuffer != null) objectPositionsBuffer.Dispose();
        if (ObjectPositions.IsCreated) ObjectPositions.Dispose();

        if (objectRotationsBuffer != null) objectRotationsBuffer.Dispose();
        if (ObjectRotations.IsCreated) ObjectRotations.Dispose();

        if (textureCoordinatesBuffer != null) textureCoordinatesBuffer.Dispose();
        if (TextureCoordinates.IsCreated) TextureCoordinates.Dispose();
    }

    public void Draw()
    {
        if (objectRotationsBuffer == null || data.Count == 0) return;

        int count = UnitToDrawCount;
        if (count == 0) return;

		objectPositionsBuffer.SetData((NativeArray<float4>)ObjectPositions, 0, 0, count);
		objectRotationsBuffer.SetData((NativeArray<quaternion>)ObjectRotations, 0, 0, count);
		textureCoordinatesBuffer.SetData((NativeArray<float3>)TextureCoordinates, 0, 0, count);

        material.SetBuffer("textureCoordinatesBuffer", textureCoordinatesBuffer);
        material.SetBuffer("objectPositionsBuffer", objectPositionsBuffer);
        material.SetBuffer("objectRotationsBuffer", objectRotationsBuffer);
        material.SetTexture("_AnimationTexture0", data.BakedData.Texture0);
        material.SetTexture("_AnimationTexture1", data.BakedData.Texture1);
        material.SetTexture("_AnimationTexture2", data.BakedData.Texture2);

        // CHECK: Systems seem to be called when exiting playmode once things start getting destroyed, such as the mesh here.
        if (mesh == null || material == null) return;

        //indirectArgs[1] = (uint)data.Count;
        indirectArgs[1] = (uint)count;
        argsBuffer.SetData(indirectArgs);

        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, new Bounds(Vector3.zero, 1000000 * Vector3.one), argsBuffer, 0, new MaterialPropertyBlock());
    }

    public int UnitToDrawCount
    {
        get
        {
			return ObjectPositions.Length;
        }
    }
}