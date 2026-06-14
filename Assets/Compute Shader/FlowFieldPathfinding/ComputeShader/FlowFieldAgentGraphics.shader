Shader "Hidden/FlowFieldAgentGraphics"
{
    Properties
    {
        _Color("Color",color) = (1,1,1,1)
        [NoScaleOffset]_MainTex("MainTexture",2DArray) = "white"{}
        // [NoScaleOffset]_MainTex("MainTexture",2D) = "white"{}
        _Specular("Specluar",Range(0,1)) = 0
        [NoScaleOffset]_SpecularTex("Specular Tex",2D) = "white"{}
        [HDR]_EmiColor("Emission Color",color) = (1,1,1,1)
        [NoScaleOffset]_EmissionTex("Emission Tex",2D) = "white"{}
        _NormalIntensity("Normal Intensity",float) = 1
        [NoScaleOffset]_NormalTex("Normal Tex",2D) = "white"{}
        // _ShadowIntensity("Shadow Intensity",Range(0,1)) = 0.5                        
    }
    HLSLINCLUDE
    #pragma prefer_hlslcc gles
    #pragma exclude_renderers d3d11_9x  
    #pragma multi_compile _ DOTS_INSTANCING_ON

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"  
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    struct Agents
    {
        //16 bytes
        float3 position;
        float speed;
        //16 bytes
        float3 direction;
        float noiseOffset;
        //16 bytes ??
        float3 idleOrigin;   // where idle movement started
        // float alive;
        float frame;
        //16 bytes 
        float3 idleDir; 
        float idleReturnDist;
        //16 bytes 
        float prevState;
        float slidingTime;  
        float gravity; 
        float texRandom;
    };
    StructuredBuffer<Agents> _AgentsBuffer;            

    struct VertexAnimations
    {
        float3 position;
        float3 normals;
    };
    StructuredBuffer<VertexAnimations> _VertexAnimationBuffer;
    
    #define MAX_ANIMATIONS 2          
    cbuffer UnityPerMaterial : register(b0)
    {                
        float4 _Color,_EmiColor;
        half _ShadowIntensity;
        half _Metallic,_Specular,_NormalIntensity;  
        float _TotalFrames;
        float _TotalVertexCount;
    };
    float4x4 _LookAtMatrix;
    float4x4 _Matrix;
    float3 _AgentPosition;
    float _AgentSize;
    float _FrameInterpolation;
    int _NextFrame,_CurrentFrame,_NumOfFrames;
    // TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
    TEXTURE2D_ARRAY(_MainTex);;SAMPLER(sampler_MainTex);
    TEXTURE2D(_EmissionTex);SAMPLER(sampler_EmissionTex);
    TEXTURE2D(_SpecularTex);SAMPLER(sampler_SpecularTex);
    TEXTURE2D(_NormalTex);SAMPLER(sampler_NormalTex);

    float4x4 TransformMatrix(float3 pos,float3 dir,float3 up)
    {
        float3 zAxis = normalize(dir);
        float3 xAxis = normalize(cross(up,zAxis));
        float3 yAxis = normalize(cross(zAxis,xAxis));
        return float4x4(
            xAxis.x, yAxis.x, zAxis.x, pos.x,
            xAxis.y, yAxis.y, zAxis.y, pos.y,
            xAxis.z, yAxis.z, zAxis.z, pos.z,
                    0,       0,       0,      1  
        );
    }
    float4x4 ScaleMatrix(float s)
    {
        return float4x4(
            s,   0.0, 0.0, 0.0,
            0.0, s,   0.0, 0.0,
            0.0, 0.0, s,   0.0,
            0.0, 0.0, 0.0, 1.0
        );
    }
    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        float3 normal : NORMAL;
        float4 tangent : TANGENT;
        uint id : SV_VertexID;
        uint instanceID : SV_InstanceID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };
    struct v2f
    {
        float4 vertex : SV_POSITION;                
        float2 uv : TEXCOORD0;  
        float3 normalWS : TEXCOORD1; 
        float3 tangentWS : TEXCOORD2;  
        float3 bitangentWS : TEXCOORD3; 
        float3 positionWS: TEXCOORD4;
        uint texPos : TEXCOORD5;
        UNITY_VERTEX_INPUT_INSTANCE_ID                           
    }; 
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

    v2f vert (appdata v)
    {
        v2f o = (v2f)0;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_TRANSFER_INSTANCE_ID(v,o);

        _AgentPosition = _AgentsBuffer[v.instanceID].position;
        _Matrix = mul(TransformMatrix(
                        _AgentsBuffer[v.instanceID].position,
                        _AgentsBuffer[v.instanceID].direction,
                        float3(0,1,0)),
                        ScaleMatrix(_AgentSize));                                 
        
                        
        float framef = _AgentsBuffer[v.instanceID].frame;
        uint frames  = (uint) _NumOfFrames;
        uint stride  = (uint) _TotalVertexCount;

        uint current = (uint)floor(framef);
        uint next    = (current + 1u) % max(frames, 1u);  // avoid div by 0
        float t      = frac(framef);

        uint idx0 = current * stride + v.id;
        uint idx1 = next    * stride + v.id;

        float3 pos0 = _VertexAnimationBuffer[idx0].position;
        float3 pos1 = _VertexAnimationBuffer[idx1].position;
        float3 nor0 = _VertexAnimationBuffer[idx0].normals;
        float3 nor1 = _VertexAnimationBuffer[idx1].normals;

        v.vertex.xyz = lerp(pos0, pos1, t);
        v.normal     = lerp(nor0, nor1, t);
        //if we want all frames of vertex first then all frames of normals then we should have 
        //id * _NumOfFrames + _CurrentFrame
        //but here we want vertex,normals then vertex,normals
        //so _CurrentFrame * _TotalVertexCount + id                
                        
        float3 positionWS = mul(_Matrix, float4(v.vertex.xyz, 1.0)).xyz;
        float3 normalWS = normalize(mul((float3x3)_Matrix, v.normal));
        float3 tangentWS = normalize(mul((float3x3)_Matrix, v.tangent.xyz));
        float3 bitangentWS = normalize(cross(normalWS, tangentWS) * v.tangent.w);

        o.vertex = CalculatePositionCSWithShadowCasterLogic(positionWS,normalWS);           
        o.normalWS = normalWS;
        o.positionWS = positionWS;
        o.uv = v.uv;
        o.tangentWS = tangentWS;
        o.bitangentWS = bitangentWS;
        o.texPos = _AgentsBuffer[v.instanceID].texRandom;
        return o;
    }

    half4 frag (v2f i) : SV_Target
    {            
        UNITY_SETUP_INSTANCE_ID(i); 
        
        float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv)) * _NormalIntensity;
        float3x3 TBN = float3x3(normalize(i.tangentWS), normalize(i.bitangentWS), normalize(i.normalWS));
        float3 normalWS = normalize(mul(normalTS, TBN));        

        // Lighting struct from lighting.hlsl to be filled 
        InputData lightingInput = (InputData)0;
        lightingInput.positionWS = i.positionWS;
        lightingInput.normalWS = NormalizeNormalPerPixel(normalWS); // Renormalize the normal to reduce interpolation errors
        lightingInput.viewDirectionWS = normalize(GetCameraPositionWS() - i.positionWS); // Calculate the view direction
        lightingInput.shadowCoord = CalculateShadowCoord(i.positionWS, i.vertex); // Calculate the shadow map coord  
            
        int texIndex = (int)i.texPos;
        // float4 col = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv) * _Color;
        float4 col = SAMPLE_TEXTURE2D_ARRAY(_MainTex, sampler_MainTex, i.uv, texIndex);
        float3 emi = SAMPLE_TEXTURE2D(_EmissionTex,sampler_EmissionTex,i.uv).xyz * _EmiColor.xyz;
        float3 spec = SAMPLE_TEXTURE2D(_SpecularTex,sampler_SpecularTex,i.uv).xyz * _Specular;
        //input data , albedo, metallic, spec, smoothness, occlusion, emission, alpha
        // return UniversalFragmentPBR(lightingInput,col * (1 - _ShadowIntensity),metallic,col.xxxx,gloss,1 ,emi * col * _ShadowIntensity,1 );                  
        return UniversalFragmentPBR(lightingInput,col,0,spec,0,1,emi,1 );                  
    }
    ENDHLSL
    SubShader
    {      
        Tags {
            "RenderPipelone" = " UniversalPipeline"
            "RenderType"="Opaque" 
            "IgnoreProjector" = "true"
        }       
        Pass
        {
            Name "ForwardLit"
            Tags{ "LightMode" = "UniversalForward" }
            Cull Off
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
            // #pragma multi_compile __ FRAME_INTERPOLATION_ON
                                                                         
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
