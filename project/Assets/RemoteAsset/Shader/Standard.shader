// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Skinning Standard"
{
    Properties
    {
		[MainTexture] _BaseMap("Base Map (RGB) Smoothness / Alpha (A)", 2D) = "white" {}
		[MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)

		_Cutoff("Alpha Clipping", Range(0.0, 1.0)) = 0.5

		_SpecColor("Specular Color", Color) = (0.5, 0.5, 0.5, 0.5)
		_SpecGlossMap("Specular Map", 2D) = "white" {}
		[Enum(Specular Alpha,0,Albedo Alpha,1)] _SmoothnessSource("Smoothness Source", Float) = 0.0
		[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0

		[HideInInspector] _BumpScale("Scale", Float) = 1.0
		[NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

		[HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
		[NoScaleOffset]_EmissionMap("Emission Map", 2D) = "white" {}

		// Blending state
		_Surface("__surface", Float) = 0.0

		[ToggleOff] _ReceiveShadows("Receive Shadows", Float) = 1.0

		// Editmode props
		[HideInInspector] _QueueOffset("Queue offset", Float) = 0.0
		[HideInInspector] _Smoothness("Smoothness", Float) = 0.5

		// ObsoleteProperties
		[HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
		[HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
		[HideInInspector] _Shininess("Smoothness", Float) = 0.0
		[HideInInspector] _GlossinessSource("GlossinessSource", Float) = 0.0
		[HideInInspector] _SpecSource("SpecularHighlights", Float) = 0.0

		[HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" "ShaderModel"="4.5"}

        // ------------------------------------------------------------------
        //  Deferred pass
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma target 4.5
            #pragma exclude_renderers gles gles3 glcore

            // -------------------------------------

            #pragma shader_feature _NORMALMAP
            #pragma shader_feature _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature _EMISSION
            #pragma shader_feature _METALLICGLOSSMAP
            #pragma shader_feature _ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature ___ _DETAIL_MULX2
            #pragma shader_feature _PARALLAXMAP

            #pragma multi_compile_prepassfinal
            #pragma multi_compile_instancing
			#pragma multi_compile _ DOTS_INSTANCING_ON

            #pragma vertex vertSkinning
            #pragma fragment LitPassFragmentSimple

			#pragma instancing_options procedural:setup
			
			#include "StandardSkinInstance.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitForwardPass.hlsl"

			VertexOutput vertSkinning(VertexInputSkinning input)
			{
				VertexOutput output = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				float3 animationTextureCoords = textureCoordinatesBuffer[unity_InstanceID];

				float4x4 firstBoneMatrix0 = CreateMatrix(animationTextureCoords.x, input.boneIds.x);
				float4x4 firstBoneMatrix1 = CreateMatrix(animationTextureCoords.y, input.boneIds.x);
				float4x4 firstBoneMatrix = firstBoneMatrix0 * (1 - animationTextureCoords.z) + firstBoneMatrix1 * animationTextureCoords.z;

				float4x4 secondBoneMatrix0 = CreateMatrix(animationTextureCoords.x, input.boneIds.y);
				float4x4 secondBoneMatrix1 = CreateMatrix(animationTextureCoords.y, input.boneIds.y);
				float4x4 secondBoneMatrix = secondBoneMatrix0 * (1 - animationTextureCoords.z) + secondBoneMatrix1 * animationTextureCoords.z;

				float4x4 combinedMatrix = firstBoneMatrix * input.boneInfluences.x + secondBoneMatrix * input.boneInfluences.y;

				float4 skinnedVertex = mul(combinedMatrix, input.positionOS);

				//skinnedVertex *= objectPositionsBuffer[unity_InstanceID].w;
				float4 posWorld = QuaternionMul(skinnedVertex, objectRotationsBuffer[unity_InstanceID]);
				posWorld.xyz = posWorld + objectPositionsBuffer[unity_InstanceID].xyz;

				float3 normalSkinningRotated = mul(combinedMatrix, float4(input.normalOS.xyz, 0));
				float3 normalWorld = QuaternionMul(float4(normalSkinningRotated, 1), objectRotationsBuffer[unity_InstanceID]);

				VertexPositionInputs vertexInput = GetVertexPositionInputs(posWorld.xyz);
				VertexNormalInputs normalInput = GetVertexNormalInputs(normalWorld, input.tangentOS);
	#else

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
	#endif
				half3 viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
				half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
				half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

				output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

				output.posWS.xyz = vertexInput.positionWS;
				output.positionCS = vertexInput.positionCS;

	#ifdef _NORMALMAP
				output.normal = half4(normalInput.normalWS, viewDirWS.x);
				output.tangent = half4(normalInput.tangentWS, viewDirWS.y);
				output.bitangent = half4(normalInput.bitangentWS, viewDirWS.z);
	#else
				output.normal = NormalizeNormalPerVertex(normalInput.normalWS);
				output.viewDir = viewDirWS;
	#endif

				OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
				OUTPUT_SH(output.normal.xyz, output.vertexSH);

				output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);

	#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
				output.shadowCoord = GetShadowCoord(vertexInput);
	#endif
				return output;
			}
			ENDHLSL
        }

		// ------------------------------------------------------------------
		//  Shadow rendering pass
		Pass{
			Name "ShadowCaster"
			Tags{"LightMode" = "ShadowCaster"}

			HLSLPROGRAM
			#pragma exclude_renderers gles gles3 glcore
			#pragma target 4.5

			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing
			#pragma multi_compile _ DOTS_INSTANCING_ON

			#pragma vertex vertSkinning
			#pragma fragment ShadowPassFragment

			#pragma instancing_options procedural:setup

			#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

			#include "StandardSkinInstance.hlsl"

			VertexOutput vertSkinning(VertexInputSkinning input)
			{
				VertexOutput output = (VertexOutput)0;

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
				float3 animationTextureCoords = textureCoordinatesBuffer[unity_InstanceID];

				float4x4 firstBoneMatrix0 = CreateMatrix(animationTextureCoords.x, input.boneIds.x);
				float4x4 firstBoneMatrix1 = CreateMatrix(animationTextureCoords.y, input.boneIds.x);
				float4x4 firstBoneMatrix = firstBoneMatrix0 * (1 - animationTextureCoords.z) + firstBoneMatrix1 * animationTextureCoords.z;

				float4x4 secondBoneMatrix0 = CreateMatrix(animationTextureCoords.x, input.boneIds.y);
				float4x4 secondBoneMatrix1 = CreateMatrix(animationTextureCoords.y, input.boneIds.y);
				float4x4 secondBoneMatrix = secondBoneMatrix0 * (1 - animationTextureCoords.z) + secondBoneMatrix1 * animationTextureCoords.z;

				float4x4 combinedMatrix = firstBoneMatrix * input.boneInfluences.x + secondBoneMatrix * input.boneInfluences.y;

				float4 skinnedVertex = mul(combinedMatrix, input.positionOS);

				skinnedVertex *= objectPositionsBuffer[unity_InstanceID].w;
				float4 posWorld = QuaternionMul(skinnedVertex, objectRotationsBuffer[unity_InstanceID]);
				posWorld.xyz = posWorld + objectPositionsBuffer[unity_InstanceID].xyz;

				float3 normalSkinningRotated = mul(combinedMatrix, float4(input.normalOS.xyz, 0));
				float3 normalWorld = QuaternionMul(float4(normalSkinningRotated, 1), objectRotationsBuffer[unity_InstanceID]);

				VertexPositionInputs vertexInput = GetVertexPositionInputs(posWorld.xyz);
				VertexNormalInputs normalInput = GetVertexNormalInputs(normalWorld, input.tangentOS);
	#else

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
	#endif
				half3 viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
				half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
				half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

				output.posWS.xyz = vertexInput.positionWS;
				output.positionCS = vertexInput.positionCS;

				return output;
			}

			ENDHLSL
		}
    }

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
