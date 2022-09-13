#ifndef STANDARD_SKIN_INSTANCED_INCLUDED
#define STANDARD_SKIN_INSTANCED_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

sampler2D _AnimationTexture0;
sampler2D _AnimationTexture1;
sampler2D _AnimationTexture2;

#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
//StructuredBuffer<float4x4> objectToWorldBuffer;
StructuredBuffer<float4> objectPositionsBuffer;
StructuredBuffer<float4> objectRotationsBuffer;
StructuredBuffer<float3> textureCoordinatesBuffer;
#endif

void setup()
{
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
	//unity_ObjectToWorld = objectToWorldBuffer[unity_InstanceID];
	//unity_WorldToObject = unity_ObjectToWorld;

	// Construct an identity matrix
	unity_ObjectToWorld = float4x4(1, 0, 0, 0,
		0, 1, 0, 0,
		0, 0, 1, 0,
		0, 0, 0, 1);
	unity_WorldToObject = unity_ObjectToWorld;

	unity_WorldToObject._14_24_34 *= -1;
	unity_WorldToObject._11_22_33 = 1.0f / unity_WorldToObject._11_22_33;
#endif
}

inline float4 QuaternionMul(float4 v, float4 q)
{
	v = float4(v.xyz + 2 * cross(q.xyz, cross(q.xyz, v.xyz) + q.w * v.xyz), v.w);
	return v;
}

struct VertexInputSkinning
{
	float4 positionOS   : POSITION;
	half3 normalOS		: NORMAL;
	float2 texcoord     : TEXCOORD0;
	float2 boneIds		: TEXCOORD1;
	float2 boneInfluences : TEXCOORD2;
	half4 tangentOS		: TANGENT;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput
{
	float2 uv                       : TEXCOORD0;
	DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 1);

	float3 posWS                    : TEXCOORD2;    // xyz: posWS

#ifdef _NORMALMAP
	float4 normal                   : TEXCOORD3;    // xyz: normal, w: viewDir.x
	float4 tangent                  : TEXCOORD4;    // xyz: tangent, w: viewDir.y
	float4 bitangent                : TEXCOORD5;    // xyz: bitangent, w: viewDir.z
#else
	float3  normal                  : TEXCOORD3;
	float3 viewDir                  : TEXCOORD4;
#endif

	half4 fogFactorAndVertexLight   : TEXCOORD6; // x: fogFactor, yzw: vertex light

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
	float4 shadowCoord              : TEXCOORD7;
#endif

	float4 positionCS               : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
		UNITY_VERTEX_OUTPUT_STEREO
};

inline float4x4 CreateMatrix(float texturePosition, float boneId)
{
	float4 row0 = tex2Dlod(_AnimationTexture0, float4(texturePosition, boneId, 0, 0));
	float4 row1 = tex2Dlod(_AnimationTexture1, float4(texturePosition, boneId, 0, 0));
	float4 row2 = tex2Dlod(_AnimationTexture2, float4(texturePosition, boneId, 0, 0));

	float4x4 reconstructedMatrix = float4x4(row0, row1, row2, float4(0, 0, 0, 1));

	return reconstructedMatrix;
}
#endif // STANDARD_SKIN_INSTANCED_INCLUDED