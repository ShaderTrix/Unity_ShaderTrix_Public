Shader "Hidden/PixelizerCubesGraphics"
{
    Properties
    {
        _Metallic("Metallic",Range(0,1)) = 0
        _Glossiness("Smoothness",Range(0,1)) = 0
        _ShadowIntensity("Shadow Intensity",Range(0,1)) = 0.5        
    }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"  
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        cbuffer UnityPerMaterial : register(b0)
        {
            half  _Metallic,_Glossiness,_ShadowIntensity;
        }
        half _Facing;
        struct CubeData
        {
            float4 position;
            float4 scale;  
            float4 color; 
            float2 uv;
            int id;
            float padding;             
        };
        StructuredBuffer<CubeData> _AliveBuffer;

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float3 normal : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float  fog : TEXCOORD3;
            float4 shadowCoord : TEXCOORD4;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        uint hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352d;
            x ^= x >> 15;
            x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        float Hash01(uint x)
        {
            return (hash(x) & 0x00FFFFFF) / 16777216.0;
        }

        float3 HashColor(uint id)
        {
            return float3(
                Hash01(id * 1u),
                Hash01(id * 2u),
                Hash01(id * 3u)
            );
        }

        v2f vert (appdata v,uint id : SV_VertexID,uint instanceID : SV_InstanceID)
        {
            v2f o = (v2f)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v,o);
            
            CubeData data = _AliveBuffer[instanceID];
            float3 cubePos = data.position.xyz;

            float follow = _Facing;

            float3 origRight = float3(1,0,0);
            float3 origUp    = float3(0,1,0);
            float3 origFwd   = float3(0,0,1);

            float3 camForward = normalize(_WorldSpaceCameraPos - cubePos);
            float3 camRight   = normalize(cross(float3(0,1,0), camForward));
            float3 camUp      = normalize(cross(camForward, camRight));

            float3 right = normalize(lerp(origRight, camRight, follow));
            float3 up    = normalize(lerp(origUp,    camUp,    follow));
            float3 fwd   = normalize(lerp(origFwd,   camForward, follow));


            float3 local = v.vertex.xyz * data.scale.xyz;

            float3 vertexWS = (right * local.x) + (up * local.y) + (fwd * local.z);

            float3 worldPos = cubePos + vertexWS;

            o.vertex = TransformWorldToHClip(worldPos);

            float3 ln = v.normal;
            float3 nWS = (right * ln.x) + (up * ln.y) + (fwd * ln.z);            
            o.normal = normalize(nWS);
            o.shadowCoord = TransformWorldToShadowCoord(worldPos);
            o.positionWS = worldPos;
            o.fog = ComputeFogFactor(o.vertex.z);
            o.uv = data.uv;
            o.color = data.color;
            // o.color = float4(HashColor(instanceID), 1);
            return o;
        }
        float4 CalculatePositionCSWithShadowCasterLogic(float3 positionWS, float3 normalWS) 
        {
            float4 positionCS;         
            #ifdef SHADOW_CASTER_PASS
                // From URP's ShadowCasterPass.hlsl
                // If this is the shadow caster pass, we need to adjust the clip space position to account
                // for shadow bias and offset (this helps reduce shadow artifacts)
                positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #endif
            #else
                positionCS = TransformWorldToHClip(positionWS);
            #endif            
            return positionCS;
        }

        float4 CalculateShadowCoord(float3 positionWS, float4 positionCS)
        {
        // Calculate the shadow coordinate depending on the type of shadows currently in use
        #if SHADOWS_SCREEN
            return ComputeScreenPos(positionCS);
        #else
            return TransformWorldToShadowCoord(positionWS);
        #endif
        }
        half4 frag (v2f i) : SV_Target
        {            
            UNITY_SETUP_INSTANCE_ID(i);                   
            // Lighting struct from lighting.hlsl to be filled 
            InputData lightingInput = (InputData)0;
            lightingInput.positionWS = i.positionWS;
            lightingInput.normalWS = NormalizeNormalPerPixel(i.normal); // Renormalize the normal to reduce interpolation errors
            lightingInput.viewDirectionWS = normalize(GetCameraPositionWS() - i.positionWS); // Calculate the view direction
            // lightingInput.shadowCoord = CalculateShadowCoord(i.positionWS, i.vertex); // Calculate the shadow map coord  
                
            float3 col = MixFog(i.color, i.fog).xyz;
            //input data , albedo, metallic, spec, smoothness, occlusion, emission, alpha
            return UniversalFragmentPBR(lightingInput,col * (1 - _ShadowIntensity),_Metallic,col.xxxx,_Glossiness,1 ,col * _ShadowIntensity,1 );                  
            // return float4(col,1);
        }
    ENDHLSL
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" "RenderQueue"="Opaque" "IgnoreProjector" = "true"}
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            Name "ForwardLit"
            Tags{ "LightMode" = "UniversalForward" }
            HLSLPROGRAM            
            #pragma vertex vert
            #pragma fragment frag            
            #pragma target 5.0
            #pragma multi_compile_fwdbase 
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
                                                                         
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags{ "LightMode" = "ShadowCaster" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag            
                                    
            #define SHADOW_CASTER_PASS            
            
            ENDHLSL
        }
    }
}