Shader "Custom/URPStandardShaderUnlit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _Shininess ("Shininess", Range(8,256)) = 32
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalRenderPipeline" "RenderType"="Opaque" "RenderQueue"="Opaque"}
        ZWrite On Cull Back ZTest LEqual
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 5.0

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile _ _SHADOWS_SOFT


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);

            float4 _BaseColor,_SpecColor;
            float  _Shininess;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posH        : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 posWS       : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                OUT.posWS     = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.posH      = TransformWorldToHClip(OUT.posWS);
                OUT.normalWS  = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.uv        = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.posH.z);

                // shadow coord for main light
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.posWS);

                return OUT;
            }

            float3 BlinnPhongSingleLight(float3 N, float3 V, Light light, float3 albedo)
            {
                float3 L = normalize(light.direction);
                float  diff = saturate(dot(N, L));

                float3 H    = normalize(L + V);
                float  spec = pow(saturate(dot(N, H)), _Shininess);

                float3 lit =
                    albedo        * light.color * diff +
                    _SpecColor.rgb * light.color * spec;

                // distanceAttenuation handles range/spot falloff etc
                return lit * light.distanceAttenuation;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb * _BaseColor.rgb;

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.posWS));

                // Ambient (from SH) – NOT shadowed
                float3 ambient = SampleSH(N) * albedo;

                // Main light + self-shadow
                Light mainLight = GetMainLight();
                float shadow    = MainLightRealtimeShadow(IN.shadowCoord); // 0 in shadow, ~1 in light

                float3 direct = BlinnPhongSingleLight(N, V, mainLight, albedo) * shadow;

                float3 color = ambient + direct;

                // Additional lights (no shadows unless you add punctual shadow sampling)
                uint lightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < lightCount; i++)
                {
                    Light l = GetAdditionalLight(i, IN.posWS);
                    color += BlinnPhongSingleLight(N, V, l, albedo);
                }

                color = MixFog(color, IN.fogFactor);
                float4 finalCol = float4(color, _BaseColor.a);
                return finalCol;
            }
            ENDHLSL
        }

        // ---------- SHADOW CASTER ----------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 GetShadowCasterPositionCS(float3 positionWS, float3 normalWS)
            {
                // tweak this if you get peter-panning or acne
                float bias = 0.005;
                positionWS += normalWS * bias;

                return TransformWorldToHClip(positionWS);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionCS = GetShadowCasterPositionCS(positionWS, normalWS);
                return OUT;
            }

            float4 ShadowFrag(Varyings IN) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            float4 DepthFrag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 DepthNormalsFrag(Varyings IN) : SV_Target
            {
                return float4(PackNormalOctRectEncode(normalize(IN.normalWS)), 0, 0);
            }
            ENDHLSL
        }
    }
}
