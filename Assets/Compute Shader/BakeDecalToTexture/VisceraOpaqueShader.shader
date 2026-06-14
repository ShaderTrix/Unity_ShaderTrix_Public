Shader "Unlit/VisceraOpaqueShader"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _MetallicSmoothnessTex ("Metallic/Smoothness", 2D) = "white" {}
        _Metallic ("Metallic",float) = 0.5
        _Smoothness ("Smoothness",float) = 0.5
        _AtlasIndex("AtlasID",float) = -1
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

            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);half4 _MainTex_ST;
            TEXTURE2D(_VisceraRuntimeBakedMask);SAMPLER(sampler_VisceraRuntimeBakedMask);
            TEXTURE2D(_MetallicSmoothnessTex);SAMPLER(sampler_MetallicSmoothnessTex);

            float _Metallic,_Smoothness;
            float4 _BaseColor;
            float2 _UVSize;
            float2 _UVOffset;          

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 posH        : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 posWS       : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float2 uv2          : TEXCOORD5;
                float  fogFactor   : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;                
            };
            
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                OUT.posWS     = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.posH      = TransformWorldToHClip(OUT.posWS);
                OUT.normalWS  = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.uv        = TRANSFORM_TEX(IN.uv,_MainTex);
                OUT.uv2        = IN.uv2 * unity_LightmapST.xy + unity_LightmapST.zw;
                OUT.fogFactor = ComputeFogFactor(OUT.posH.z);

                // shadow coord for main light
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.posWS);                
                return OUT;
            }

            float3 DirectLighting(
                float3 N,
                float3 V,
                Light light,
                float3 albedo,
                float metallic,
                float smoothness
            )
            {
                float3 L = normalize(light.direction);
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(N, L));
                float NdotV = saturate(dot(N, V));
                float NdotH = saturate(dot(N, H));
                float VdotH = saturate(dot(V, H));

                float roughness = 1.0 - smoothness;
                float alpha = roughness * roughness;

                // Fresnel (Schlick)
                float3 F0 = lerp(0.04.xxx, albedo, metallic);
                float3 F = F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);

                // Normal Distribution (cheap GGX)
                float alpha2 = alpha * alpha;
                float denom = (NdotH * NdotH) * (alpha2 - 1.0) + 1.0;
                float D = alpha2 / (PI * denom * denom + 1e-5);

                // Geometry (Smith, cheap)
                float k = (alpha + 1.0) * (alpha + 1.0) / 8.0;
                float Gv = NdotV / (NdotV * (1.0 - k) + k);
                float Gl = NdotL / (NdotL * (1.0 - k) + k);
                float G = Gv * Gl;

                float3 specular = (D * G * F) / max(4.0 * NdotL * NdotV, 1e-5);

                float3 kD = (1.0 - F) * (1.0 - metallic);
                float3 diffuse = kD * albedo / PI;

                float3 radiance = light.color * light.distanceAttenuation * NdotL;
                return (diffuse + specular) * radiance;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 atlaUV = _UVSize * IN.uv2 + _UVOffset; 
                float4 mask = SAMPLE_TEXTURE2D(_VisceraRuntimeBakedMask,sampler_VisceraRuntimeBakedMask,atlaUV);
                // clip(mask.a < 1e-02);

                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb * _BaseColor.rgb;
                float4 msSample = SAMPLE_TEXTURE2D(_MetallicSmoothnessTex,sampler_MetallicSmoothnessTex,IN.uv);

                float metallic   = saturate(lerp(msSample.r,mask.a,mask.a) * _Metallic);
                float smoothness = saturate(lerp(msSample.a,mask.a,mask.a) * _Smoothness);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.posWS));

                half3 bakedGI;
                #ifdef LIGHTMAP_ON
                    bakedGI = SampleLightmap(IN.uv2, IN.normalWS);  
                #else
                    bakedGI = 1; 
                #endif   
                
                float3 ambient = SampleSH(N) * albedo * bakedGI;

                Light mainLight = GetMainLight();
                float shadow    = MainLightRealtimeShadow(IN.shadowCoord); // 0 in shadow, ~1 in light

                float3 direct = DirectLighting(N, V, mainLight, albedo, metallic, smoothness) * shadow;
                float3 color = ambient + direct;

                uint lightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < lightCount; i++)
                {
                    Light l = GetAdditionalLight(i, IN.posWS);
                    color += DirectLighting(N, V, mainLight, albedo, metallic , smoothness) * shadow;
                }

                color = MixFog(color, IN.fogFactor);
                    
                return float4(lerp(color.xyz,mask.xyz,mask.a), _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PaintEnvironmentBakeMRT"
            Tags { "LightMode"="PaintEnvironmentBakeMRT" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"       

            struct MRTOutput
            {
                float4 color : SV_Target0;
                float4 uv_Depth : SV_Target1;
                float4 uv_ddx : SV_TARGET2;
                float4 uv_wNorm : SV_TARGET3;
                float4 uv_wPosDDX : SV_TARGET4;
                float4 uv_wPosDDY : SV_TARGET5;
            };
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
            };

            float _AtlasIndex;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float3 wNorm : TEXCOORD2;
                float4 wTangent : TEXCOORD3;
                float3 wPosition : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                // OUT.positionCS = TransformWorldToHClip(posWS);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = posInputs.positionCS;
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normal, IN.tangent);            
                OUT.wNorm = normInputs.normalWS;
                OUT.wTangent.xyz = normInputs.tangentWS;
                OUT.wTangent.w = IN.tangent.w; 
                OUT.wPosition = posInputs.positionWS;
                // OUT.wNorm = TransformObjectToWorldNormal(IN.normal);
                // OUT.wTangent.xyz = TransformObjectToWorldDir(IN.tangent.xyz);
                // OUT.wTangent.w = IN.tangent.w;

                OUT.uv = IN.uv;
                OUT.uv2 = IN.uv2;
                return OUT;
            }

            MRTOutput frag(Varyings IN)
            {
                MRTOutput o;
                o.color = 0;
                o.uv_Depth = float4(IN.uv2.xy,0,asfloat(_AtlasIndex));

                // float mag = length(ddx(IN.uv2.xy)) + length(ddy(IN.uv2.xy));
                // o.uv_ddx = float4(mag.xxx * 50,1);
                o.uv_ddx = float4(ddx(IN.uv2.xy),ddy(IN.uv2.xy));
                o.uv_wNorm = float4(normalize(IN.wNorm) * 0.5 + 0.5, 1.0);
                // o.uv_wTangent = float4(normalize(IN.wTangent.xyz) * 0.5 + 0.5, IN.wTangent.w);
                // o.uv_wPos = float4(IN.wPosition,1.0f);
                o.uv_wPosDDX = float4(ddx(IN.wPosition.xyz),1.0f);
                o.uv_wPosDDY = float4(ddy(IN.wPosition.xyz),1.0f);
                return o;
            }
            ENDHLSL
        }

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
