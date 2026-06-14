Shader "Custom/MRTStandardNoForwardLit"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _Tint ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _Shininess ("Shininess", Range(8,256)) = 32
        _EnemyID("EnemyID",float) = 0
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

        struct mrtOutput
        {
            float4 dummy : SV_Target0;
            float4 color : SV_Target1;
            float4 positionWS : SV_Target2;
        };

        TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
        float4 _BaseColor,_Tint,_SpecColor;
        float  _Shininess;
        int _EnemyID;
        int _EnemyDead;
                                            
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
        mrtOutput fragLighting(v2f i) 
        {
            float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _BaseColor;
            
            float3 N = normalize(i.normal.xyz);
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

            float rawDepth = (i.vertex.z / i.vertex.w);   // clip space depth (-1 to +1)
            float depth01  = rawDepth * 0.5 + 0.5;

            float4 finalCol = float4(color, _BaseColor.a);
            mrtOutput o;
            o.dummy = 0;
            o.color = (_EnemyDead == 0) ? float4(finalCol.xyz + _Tint.xyz,1): 0;
            o.positionWS = (_EnemyDead == 0) ? float4(i.positionWS.xyz,_EnemyID):0;
            return o;
        }
    ENDHLSL
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" "RenderQueue"="Opaque"}
        ZWrite On ZTest LEqual Cull Back
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="MRTWrite" }
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
    }
}
