Shader "Custom/env_molotov_groundimpact"
{
    Properties
    {
        [MainTexture] _MainTex ("Mask Texture", 2D) = "white" {}
        [HDR] _ImpactColor ("Impact Color", Color) = (3.0, 0.08, 0.02, 1.0)
        [HDR] _RimColor ("Rim Color", Color) = (2.0, 0.18, 0.04, 1.0)
        _ImpactCenter ("Impact Center UV", Vector) = (0.5, 0.5, 0.0, 0.0)
        _Age ("Age", Float) = 0.0
        _Duration ("Duration", Float) = 0.45
        _MaxRadius ("Max Radius", Range(0.05, 1.5)) = 0.72
        _RingWidth ("Ring Width", Range(0.005, 0.25)) = 0.055
        _BumpStrength ("Bump Strength", Range(0.0, 3.0)) = 1.15
        _Distortion ("UV Distortion", Range(0.0, 0.2)) = 0.035
        _NoiseScale ("Noise Scale", Range(1.0, 80.0)) = 23.0
        _NoiseAmount ("Noise Amount", Range(0.0, 1.0)) = 0.28
        _CenterFlash ("Center Flash", Range(0.0, 1.0)) = 0.55
        _FadeOutPower ("Fade Out Power", Range(0.2, 6.0)) = 1.45
        _Alpha ("Alpha", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Bumping Impact"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _ImpactColor;
                half4 _RimColor;
                float4 _ImpactCenter;
                float _Age;
                float _Duration;
                float _MaxRadius;
                float _RingWidth;
                float _BumpStrength;
                float _Distortion;
                float _NoiseScale;
                float _NoiseAmount;
                float _CenterFlash;
                float _FadeOutPower;
                float _Alpha;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float RingRaw(float2 uv, float progress)
            {
                float2 fromCenter = uv - _ImpactCenter.xy;
                float distanceFromCenter = length(fromCenter);
                float radius = _MaxRadius * progress;
                float width = max(_RingWidth, 0.0001);
                float ringDistance = (distanceFromCenter - radius) / width;
                float ring = exp(-ringDistance * ringDistance);

                float animatedNoise = ValueNoise(uv * _NoiseScale + _Age * 5.0);
                float brokenEdge = lerp(0.62, 1.28, animatedNoise);
                return ring * lerp(1.0, brokenEdge, _NoiseAmount);
            }

            float CenterFlashRaw(float2 uv, float progress)
            {
                float2 fromCenter = uv - _ImpactCenter.xy;
                float distanceSquared = dot(fromCenter, fromCenter);
                return exp(-distanceSquared * 34.0) * _CenterFlash * (1.0 - progress);
            }

            float ImpactHeight(float2 uv, float progress, float fade)
            {
                float ring = RingRaw(uv, progress);
                float centerFlash = CenterFlashRaw(uv, progress);
                return saturate(ring + centerFlash) * fade;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float safeDuration = max(_Duration, 0.0001);
                float progress = saturate(_Age / safeDuration);
                float fade = pow(saturate(1.0 - progress), _FadeOutPower);

                float height = ImpactHeight(input.uv, progress, fade);

                float sampleStep = 0.0025;
                float heightX = ImpactHeight(input.uv + float2(sampleStep, 0.0), progress, fade)
                              - ImpactHeight(input.uv - float2(sampleStep, 0.0), progress, fade);
                float heightY = ImpactHeight(input.uv + float2(0.0, sampleStep), progress, fade)
                              - ImpactHeight(input.uv - float2(0.0, sampleStep), progress, fade);

                float3 fakeNormal = normalize(float3(-heightX * _BumpStrength, -heightY * _BumpStrength, 1.0));
                float3 lightDirection = normalize(float3(-0.35, 0.55, 0.76));
                float lighting = saturate(dot(fakeNormal, lightDirection));

                float2 radialDirection = normalize(input.uv - _ImpactCenter.xy + 0.00001);
                float2 maskUV = input.uv * _MainTex_ST.xy + _MainTex_ST.zw + radialDirection * height * _Distortion;
                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, maskUV).r;

                float ring = RingRaw(input.uv, progress) * fade;
                float centerFlash = CenterFlashRaw(input.uv, progress) * fade;
                float rim = pow(saturate(1.0 - fakeNormal.z), 0.8);

                float3 impactColor = _ImpactColor.rgb * (0.05 + lighting * 2.25);
                float3 rimColor = _RimColor.rgb * (ring * 0.05 + rim * 2.35);
                float3 finalColor = impactColor * (height + centerFlash * 2.5) + rimColor;

                float alpha = saturate((ring * 1.05 + centerFlash * 0.75 + rim * 0.4) * mask * _Alpha);
                alpha *= smoothstep(0.0, 0.035, _Age);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
