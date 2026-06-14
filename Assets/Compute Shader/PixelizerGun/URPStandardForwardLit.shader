Shader "Custom/URPStandardForwardLit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _Shininess ("Shininess", Range(8,256)) = 32
    }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float3 normal : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float  fog : TEXCOORD3;
            float4 shadowCoord : TEXCOORD4;
        };

        TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
        float4 _BaseColor,_SpecColor;
        float  _Shininess;

                                            
        v2f vertLighting(appdata v)
        {
            v2f o;
            o.positionWS = TransformObjectToWorld(v.vertex.xyz);
            o.vertex = TransformWorldToHClip(o.positionWS);
            o.normal = normalize(TransformObjectToWorldNormal(v.normal));
            o.uv = v.uv;
            o.fog = ComputeFogFactor(o.vertex.z);
            o.shadowCoord = TransformWorldToShadowCoord(o.positionWS);
            return o;
        }

        float3 BlinnPhongSingleLight(float3 N, float3 V, Light light, float3 albedo)
        {
            float3 L = normalize(light.direction);
            float  diff = saturate(dot(N, L));
            float3 H = normalize(L + V);
            float  spec = pow(saturate(dot(N, H)), _Shininess);
            float3 lit = albedo * light.color * diff + _SpecColor.rgb * light.color * spec;
            return lit * light.distanceAttenuation;
        }
        float4 GetShadowCasterPositionCS(float3 positionWS, float3 normal)
        {
            positionWS += normal * 0.005f;
            return TransformWorldToHClip(positionWS);
        }
        float4 fragLighting(v2f i) : SV_Target
        {
            float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _BaseColor;
            float3 N = normalize(i.normal);
            float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

            float3 ambient = SampleSH(N) * albedo;

            Light mainLight = GetMainLight();
            float shadow  = MainLightRealtimeShadow(i.shadowCoord);
            float3 light = BlinnPhongSingleLight(N, V, mainLight, albedo) * shadow;
            float3 color = ambient + light;

            uint lightCount = GetAdditionalLightsCount();
            for (uint j = 0; j < lightCount; j++)
            {
                Light l = GetAdditionalLight(j, i.positionWS);
                color += BlinnPhongSingleLight(N, V, l, albedo);
            }

            color = MixFog(color, i.fog);

            // float4 clip = i.vertex; 
            // float rawDepth = clip.z / clip.w;
            // rawDepth = rawDepth * 0.5 + 0.5; 

            // float depth01 = Linear01Depth(rawDepth, _ProjectionParams);

            float4 finalCol = float4(color, _BaseColor.a);
            return finalCol;
        }
        v2f vertShadow(appdata v)
        {
            v2f o = (v2f)0;
            float3 positionWS = TransformObjectToWorld(v.vertex.xyz);
            o.normal = TransformObjectToWorldNormal(v.normal);
            o.vertex = GetShadowCasterPositionCS(positionWS,o.normal);
            return o;
        }
        float4 fragShadow(v2f i) : SV_Target
        {
            return 0;
        }
    ENDHLSL
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" "RenderQueue"="Opaque"}
        ZWrite On ZTest LEqual Cull Back
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vertLighting
            #pragma fragment fragLighting
            #pragma target 5.0

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile _ _SHADOWS_SOFT
                        
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing        
                                    
            ENDHLSL
        }
    }
}
