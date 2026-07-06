Shader "Custom/URP/Murky Water/Bottom Distortion Clean"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color / Tint", Color) = (0.35, 0.55, 0.45, 1.0)

        _SpecularMap ("Specular Map (RGB), Smoothness (A)", 2D) = "white" {}
        _SpecularStrength ("Specular Strength", Range(0, 4)) = 1.0
        _Smoothness ("Smoothness Multiplier", Range(0.02, 1)) = 0.65

        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1.0

        _DistortionAlphaTex ("Alpha Overlay Texture (RGB/A = Mask)", 2D) = "white" {}
        _AlphaSourceBlend ("Mask Source: 0 RGB, 1 Alpha", Range(0, 1)) = 0
        _AlphaOverlaySpeed ("Alpha Overlay Speed", Range(0, 5)) = 1.5
        _AlphaOverlayContrast ("Alpha Overlay Contrast", Range(0.25, 4)) = 1.25
        _DistortionStrength ("Alpha Texture Distortion Strength", Range(0, 0.25)) = 0.035
        _DistortionScroll ("Distortion Scroll XY / Secondary ZW", Vector) = (0.08, 0.03, -0.04, 0.06)

        [Toggle(_USE_OBJECT_Y)] _UseObjectY ("Use Object-Space Y Gradient", Float) = 0
        _BottomGradientStart ("UV Bottom Gradient Start", Range(0, 1)) = 0.0
        _BottomGradientEnd ("UV Bottom Gradient End", Range(0, 1)) = 0.35
        _ObjectBottomY ("Object Bottom Y", Float) = 0.0
        _ObjectGradientHeight ("Object Gradient Height", Float) = 1.0
        _GradientPower ("Gradient Power", Range(0.1, 6)) = 1.0

        _MurkColor ("Alpha Overlay / Murky Color", Color) = (0.10, 0.22, 0.16, 1)
        _MurkAmount ("Alpha Overlay Strength", Range(0, 1)) = 0.35
        [Toggle(_USE_TWO_ALPHA_COLORS)] _UseTwoAlphaColors ("Use Black/White Alpha Overlay Colors", Float) = 1
        _AlphaBlackColor ("Alpha Black Overlay Color", Color) = (0.05, 0.12, 0.09, 1)
        _AlphaWhiteColor ("Alpha White Overlay Color", Color) = (0.28, 0.48, 0.34, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Universal Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_OBJECT_Y
            #pragma shader_feature_local _USE_TWO_ALPHA_COLORS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_SpecularMap);
            SAMPLER(sampler_SpecularMap);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_DistortionAlphaTex);
            SAMPLER(sampler_DistortionAlphaTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;

                float4 _SpecularMap_ST;
                half _SpecularStrength;
                half _Smoothness;

                float4 _NormalMap_ST;
                half _NormalStrength;

                float4 _DistortionAlphaTex_ST;
                half _AlphaSourceBlend;
                half _AlphaOverlaySpeed;
                half _AlphaOverlayContrast;
                half _DistortionStrength;
                float4 _DistortionScroll;

                half _BottomGradientStart;
                half _BottomGradientEnd;
                float _ObjectBottomY;
                float _ObjectGradientHeight;
                half _GradientPower;

                half4 _MurkColor;
                half _MurkAmount;
                half4 _AlphaBlackColor;
                half4 _AlphaWhiteColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvBase : TEXCOORD0;
                float2 uvSpecular : TEXCOORD1;
                float2 uvNormal : TEXCOORD2;
                float2 uvDistortion : TEXCOORD3;
                float2 gradientData : TEXCOORD4;
                float3 positionWS : TEXCOORD5;
                half3 normalWS : TEXCOORD6;
                half3 tangentWS : TEXCOORD7;
                half3 bitangentWS : TEXCOORD8;
            };

            half BottomMask(float rawUvY, float objectY)
            {
                #if defined(_USE_OBJECT_Y)
                    float height = max(abs(_ObjectGradientHeight), 0.0001);
                    half gradientValue = saturate((objectY - _ObjectBottomY) / height);
                    half mask = 1.0 - smoothstep(0.0, 1.0, gradientValue);
                #else
                    half gradientRange = max(_BottomGradientEnd - _BottomGradientStart, 0.0001);
                    half gradientValue = saturate((rawUvY - _BottomGradientStart) / gradientRange);
                    half mask = 1.0 - smoothstep(0.0, 1.0, gradientValue);
                #endif

                return pow(saturate(mask), _GradientPower);
            }

            half OverlayMask(half4 sampleValue)
            {
                half rgbMask = dot(sampleValue.rgb, half3(0.299, 0.587, 0.114));
                half mask = lerp(rgbMask, sampleValue.a, _AlphaSourceBlend);
                return saturate((mask - 0.5) * _AlphaOverlayContrast + 0.5);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = normalInputs.tangentWS;
                output.bitangentWS = normalInputs.bitangentWS;

                output.uvBase = TRANSFORM_TEX(input.uv, _BaseMap);
                output.uvSpecular = TRANSFORM_TEX(input.uv, _SpecularMap);
                output.uvNormal = TRANSFORM_TEX(input.uv, _NormalMap);
                output.uvDistortion = TRANSFORM_TEX(input.uv, _DistortionAlphaTex);
                output.gradientData = float2(input.uv.y, input.positionOS.y);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvBase) * _BaseColor;
                half4 specSample = SAMPLE_TEXTURE2D(_SpecularMap, sampler_SpecularMap, input.uvSpecular);

                half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uvNormal);
                half3 tangentNormal = UnpackNormalScale(normalSample, _NormalStrength);
                half3x3 tangentToWorld = half3x3(
                    normalize(input.tangentWS),
                    normalize(input.bitangentWS),
                    normalize(input.normalWS)
                );
                half3 normalWS = normalize(mul(tangentNormal, tangentToWorld));

                half bottomMask = BottomMask(input.gradientData.x, input.gradientData.y);
                float2 alphaUv = input.uvDistortion;
                float time = _Time.y * _AlphaOverlaySpeed;
                float2 movingAlphaUv = alphaUv + time * _DistortionScroll.xy;
                float2 warpAlphaUv = alphaUv * 1.37 + 17.13 + time * _DistortionScroll.zw;
                half alphaA = OverlayMask(SAMPLE_TEXTURE2D(_DistortionAlphaTex, sampler_DistortionAlphaTex, movingAlphaUv));
                half alphaB = OverlayMask(SAMPLE_TEXTURE2D(_DistortionAlphaTex, sampler_DistortionAlphaTex, warpAlphaUv));
                float2 alphaDistortion = (float2(alphaB, alphaA) * 2.0 - 1.0) * _DistortionStrength * bottomMask;
                half overlayMask = OverlayMask(SAMPLE_TEXTURE2D(_DistortionAlphaTex, sampler_DistortionAlphaTex, movingAlphaUv + alphaDistortion));
                half murkMask = bottomMask * saturate(overlayMask);

                Light mainLight = GetMainLight();
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 lightDir = SafeNormalize(mainLight.direction);
                half3 halfDir = SafeNormalize(lightDir + viewDir);

                half ndotl = saturate(dot(normalWS, lightDir));
                half smoothness = saturate(specSample.a * _Smoothness);
                half shininess = exp2(2.0 + smoothness * 9.0);
                half specularTerm = pow(saturate(dot(normalWS, halfDir)), shininess) * _SpecularStrength;

                #if defined(_USE_TWO_ALPHA_COLORS)
                    half3 albedo = lerp(baseSample.rgb, lerp(_AlphaBlackColor.rgb, _AlphaWhiteColor.rgb, overlayMask), _MurkAmount * bottomMask);
                #else
                half3 albedo = lerp(baseSample.rgb, _MurkColor.rgb, _MurkAmount * murkMask);
                #endif
                half3 ambient = SampleSH(normalWS) * albedo;
                half3 diffuse = albedo * mainLight.color * ndotl * mainLight.distanceAttenuation;
                half3 specular = specSample.rgb * mainLight.color * specularTerm * mainLight.distanceAttenuation;

                return half4(ambient + diffuse + specular, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
