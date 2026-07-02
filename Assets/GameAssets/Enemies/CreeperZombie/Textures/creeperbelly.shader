Shader "Custom/creeperbelly"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture]_BaseMap ("Base Color Texture", 2D) = "white" {}
        [MainColor]_BaseColor ("Surface Color", Color) = (0.035, 0.04, 0.055, 1)
        _SurfaceColorInfluence ("Surface Color Influence", Range(0, 1)) = 1
        _LightingStrength ("Lighting Strength", Range(0, 1)) = 0.35
        _BaseEmission ("Base Emission", Range(0, 5)) = 0

        [Header(Blob Gradient)]
        [HDR]_BlobColorEdge ("Blob Edge Color", Color) = (0.02, 0.55, 1.0, 1)
        [HDR]_BlobColorMid ("Blob Mid Color", Color) = (0.8, 0.18, 1.0, 1)
        [HDR]_BlobColorCore ("Blob Core Color", Color) = (1.0, 0.92, 0.35, 1)
        _GradientPower ("Gradient Power", Range(0.2, 6)) = 1.8

        [Header(Blob Shape)]
        _BlobCount ("Blob Count", Range(1, 24)) = 12
        _BlobSize ("Blob Size", Range(0.02, 1.5)) = 0.32
        _BlobSoftness ("Blob Softness", Range(0.005, 1)) = 0.18
        _SizeVariation ("Size Variation", Range(0, 1)) = 0.35

        [Header(Animation)]
        _AppearSpeed ("Appear / Disappear Speed", Range(0, 5)) = 0.28
        _MinVisibility ("Minimum Visibility", Range(0, 1)) = 0.02
        _PulseSharpness ("Pulse Sharpness", Range(0.2, 8)) = 2.2
        _PositionDrift ("Blob Drift", Range(0, 1)) = 0.035
        _PositionSeed ("Position Seed", Float) = 3

        [Header(Middle Effect Mask)]
        _EffectAxis ("Middle Axis - Object Space", Vector) = (0, 1, 0, 0)
        _MiddleBandUseNormals ("Use Normals For Middle Band", Range(0, 1)) = 1
        _MiddleBandCenter ("Middle Center", Range(-1, 1)) = 0
        _MiddleBandWidth ("Middle Width", Range(0.01, 2)) = 1.25
        _MiddleBandSoftness ("Middle Edge Softness", Range(0.001, 1)) = 0.25
        _MiddleBandFalloffPower ("Middle Falloff Power", Range(0.1, 8)) = 1
        _MiddleBandEffectStrength ("Middle Effect Strength", Range(0, 1)) = 1

        [Header(Optional Path Mask)]
        [NoScaleOffset]_EffectMaskMap ("Effect Mask Texture", 2D) = "white" {}
        _EffectMaskMapStrength ("Effect Mask Texture Strength", Range(0, 1)) = 0
        _VertexColorMaskStrength ("Vertex Color Red Mask Strength", Range(0, 1)) = 0

        [Header(Emission)]
        _EmissionStrength ("Blob Emission Strength", Range(0, 30)) = 8
        _SurfaceTintFromBlobs ("Surface Tint From Blobs", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_BLOBS 24
            #define GLOWING_BLOBS_TWO_PI 6.28318530718

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EffectMaskMap);
            SAMPLER(sampler_EffectMaskMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _SurfaceColorInfluence;
                float _LightingStrength;
                float _BaseEmission;

                float4 _BlobColorEdge;
                float4 _BlobColorMid;
                float4 _BlobColorCore;
                float _GradientPower;

                float _BlobCount;
                float _BlobSize;
                float _BlobSoftness;
                float _SizeVariation;

                float _AppearSpeed;
                float _MinVisibility;
                float _PulseSharpness;
                float _PositionDrift;
                float _PositionSeed;

                float4 _EffectAxis;
                float _MiddleBandUseNormals;
                float _MiddleBandCenter;
                float _MiddleBandWidth;
                float _MiddleBandSoftness;
                float _MiddleBandFalloffPower;
                float _MiddleBandEffectStrength;
                float _EffectMaskMapStrength;
                float _VertexColorMaskStrength;

                float _EmissionStrength;
                float _SurfaceTintFromBlobs;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
                float2 baseUV : TEXCOORD3;
                float2 meshUV : TEXCOORD4;
                float3 positionOS : TEXCOORD5;
                float4 vertexColor : TEXCOORD6;
            };

            float Hash11(float n)
            {
                return frac(sin(n) * 43758.5453123);
            }

            float3 RandomPointOnSphere(float id, float seed)
            {
                float u = Hash11(id * 17.173 + seed * 3.137);
                float v = Hash11(id * 41.719 + seed * 11.977);
                float theta = u * GLOWING_BLOBS_TWO_PI;
                float z = v * 2.0 - 1.0;
                float r = sqrt(max(0.0, 1.0 - z * z));

                return float3(r * cos(theta), z, r * sin(theta));
            }

            float3 RotateAroundAxis(float3 value, float3 axis, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return value * c + cross(axis, value) * s + axis * dot(axis, value) * (1.0 - c);
            }

            float3 BlobGradient(float t)
            {
                t = pow(saturate(t), _GradientPower);

                float3 edgeToMid = lerp(_BlobColorEdge.rgb, _BlobColorMid.rgb, saturate(t * 2.0));
                float3 midToCore = lerp(_BlobColorMid.rgb, _BlobColorCore.rgb, saturate((t - 0.5) * 2.0));

                return lerp(edgeToMid, midToCore, step(0.5, t));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.normalOS = normalize(input.normalOS);
                output.baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
                output.meshUV = input.uv;
                output.positionOS = input.positionOS.xyz;
                output.vertexColor = input.color;

                return output;
            }

            float MiddleBandFromHeight(float height)
            {
                float halfWidth = max(0.0001, _MiddleBandWidth * 0.5);
                float softness = max(0.0001, _MiddleBandSoftness);
                float distanceFromMiddle = abs(height - _MiddleBandCenter);
                float middleMask = 1.0 - smoothstep(halfWidth, halfWidth + softness, distanceFromMiddle);

                return pow(saturate(middleMask), _MiddleBandFalloffPower) * _MiddleBandEffectStrength;
            }

            float MiddleBandMask(float3 positionOS, float3 normalOS)
            {
                float3 axis = _EffectAxis.xyz;
                axis = dot(axis, axis) < 0.0001 ? float3(0.0, 1.0, 0.0) : normalize(axis);

                float normalHeight = dot(normalize(normalOS), axis);
                float3 positionDirection = dot(positionOS, positionOS) < 0.0001 ? normalize(normalOS) : normalize(positionOS);
                float positionHeight = dot(positionDirection, axis);
                float positionMask = MiddleBandFromHeight(positionHeight);
                float normalMask = MiddleBandFromHeight(normalHeight);

                return saturate(lerp(positionMask, normalMask, saturate(_MiddleBandUseNormals)));
            }

            float PathEffectMask(float2 uv, float vertexRed)
            {
                float textureMask = SAMPLE_TEXTURE2D(_EffectMaskMap, sampler_EffectMaskMap, uv).r;
                float pathMask = lerp(1.0, textureMask, saturate(_EffectMaskMapStrength));
                pathMask *= lerp(1.0, vertexRed, saturate(_VertexColorMaskStrength));

                return saturate(pathMask);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 surfaceDirOS = normalize(input.normalOS);
                float4 baseTexture = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.baseUV);
                float3 unmodifiedBaseColor = baseTexture.rgb;
                float3 multipliedSurfaceColor = unmodifiedBaseColor * _BaseColor.rgb;
                float3 surfaceTintedBaseColor = lerp(multipliedSurfaceColor, _BaseColor.rgb, saturate(_SurfaceColorInfluence));
                float middleStrength = MiddleBandMask(input.positionOS, input.normalOS);
                float pathStrength = PathEffectMask(input.meshUV, input.vertexColor.r);
                float blobEffectStrength = middleStrength * pathStrength;

                float3 blobColorSum = 0.0;
                float blobWeightSum = 0.0;

                [loop]
                for (int i = 0; i < MAX_BLOBS; i++)
                {
                    float id = (float)i + 1.0;
                    float enabled = step(id, _BlobCount);

                    float3 center = RandomPointOnSphere(id, _PositionSeed);
                    float3 driftAxis = normalize(RandomPointOnSphere(id + 91.0, _PositionSeed + 23.0));
                    float driftRate = lerp(0.35, 1.2, Hash11(id * 13.711 + _PositionSeed));
                    center = RotateAroundAxis(center, driftAxis, _Time.y * _PositionDrift * driftRate);

                    float sizeRand = Hash11(id * 5.431 + _PositionSeed * 2.0);
                    float sizeScale = lerp(1.0 - _SizeVariation * 0.45, 1.0 + _SizeVariation, sizeRand);
                    float radius = max(0.001, _BlobSize * sizeScale);
                    float softness = max(0.001, _BlobSoftness);

                    float innerEdge = cos(radius);
                    float outerEdge = cos(radius + softness);
                    float closeness = dot(surfaceDirOS, center);
                    float blobShape = smoothstep(outerEdge, innerEdge, closeness);

                    float pulseRand = Hash11(id * 29.113 + _PositionSeed * 7.0);
                    float pulseRate = _AppearSpeed * lerp(0.65, 1.35, pulseRand);
                    float pulse = 0.5 + 0.5 * sin(_Time.y * pulseRate + pulseRand * GLOWING_BLOBS_TWO_PI);
                    pulse = lerp(_MinVisibility, 1.0, pow(saturate(pulse), _PulseSharpness));

                    float weight = blobShape * pulse * enabled;
                    blobColorSum += BlobGradient(blobShape) * weight;
                    blobWeightSum += weight;
                }

                float3 averageBlobColor = blobColorSum / max(0.0001, blobWeightSum);
                float blobMask = saturate(blobWeightSum);

                Light mainLight = GetMainLight();
                float3 ambient = SampleSH(normalWS);
                float mainLightAmount = saturate(dot(normalWS, mainLight.direction));
                float3 lit = ambient + mainLight.color * mainLightAmount;
                float3 litSurfaceColor = surfaceTintedBaseColor * lerp(float3(1.0, 1.0, 1.0), lit, _LightingStrength);
                float3 baseColor = lerp(unmodifiedBaseColor, litSurfaceColor, middleStrength);

                float3 surfaceTint = averageBlobColor * blobMask * _SurfaceTintFromBlobs;
                float3 baseEmission = surfaceTintedBaseColor * _BaseEmission;
                float3 blobEmission = blobColorSum * _EmissionStrength;
                float3 finalColor = baseColor + baseEmission * middleStrength + (surfaceTint + blobEmission) * blobEffectStrength;
                float finalAlpha = baseTexture.a * lerp(1.0, _BaseColor.a, middleStrength);

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
