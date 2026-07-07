Shader "BotBuds/VFX/Aura Fresnel Xray"
{
    Properties
    {
        [HDR] _AuraColor ("Aura Color", Color) = (0, 0.9, 1, 0.75)
        [HDR] _EdgeColor ("Edge Color", Color) = (1, 1, 1, 1)
        _Alpha ("Body Alpha", Range(0, 1)) = 0.28
        _FresnelAlpha ("Fresnel Alpha", Range(0, 2)) = 1.05
        _FresnelPower ("Fresnel Width", Range(0.25, 8)) = 1.7
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 1.15
        _NoiseScale ("Wisp Scale", Range(0.5, 40)) = 9
        _NoiseStrength ("Wisp Strength", Range(0, 1)) = 0.28
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+60"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AuraFresnelOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _AuraColor;
                half4 _EdgeColor;
                half _Alpha;
                half _FresnelAlpha;
                half _FresnelPower;
                half _PulseSpeed;
                half _NoiseScale;
                half _NoiseStrength;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = normal.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half fresnel = pow(1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS))), _FresnelPower);
                half pulse = 0.86 + 0.14 * sin(_Time.y * _PulseSpeed * 6.28318);
                half wisps = Hash21(floor((input.positionWS.xy + input.positionWS.z) * _NoiseScale + _Time.y * 2.0));
                half wispMask = lerp(1.0, wisps, _NoiseStrength);
                half alpha = saturate(_Alpha + fresnel * _FresnelAlpha) * _AuraColor.a * pulse * wispMask;
                half3 color = _AuraColor.rgb * (_Alpha * 1.4) + _EdgeColor.rgb * fresnel * _FresnelAlpha;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
