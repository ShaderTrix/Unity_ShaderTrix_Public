Shader "Hidden/HizDepthShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"  
    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
        float3 normal : NORMAL;
    };
    struct v2f
    {
        float4 vertex : SV_POSITION;                
        float2 uv : TEXCOORD0;  
        // float2 depth : TEXCOORD1;                         
        float4 positionVS : TEXCOORD1;
    }; 
    TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);float4 _MainTex_TexelSize;
    TEXTURE2D(_CameraDepthTexture);SAMPLER(sampler_CameraDepthTexture);
    TEXTURE2D(_LightTexture);SAMPLER(sampler_LightTexture);

    // float LinearEyeDepth(float rawDepth)
    // {
    //     return Linear01Depth(rawDepth, _ZBufferParams);
    // }

    v2f vert (appdata v)
    {
        v2f o = (v2f)0;
        o.vertex = TransformWorldToHClip(v.vertex.xyz);
        o.uv = v.uv;
        // float rawDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
        // float linearDepth = LinearEyeDepth(rawDepth);
        o.positionVS = ComputeScreenPos(o.vertex);
        return o;
    }
    
    half4 blit (v2f i) : SV_Target
    {
        float lightDepth = _LightTexture.Sample(sampler_LightTexture,i.uv).x;
        float camDepth = _CameraDepthTexture.Sample(sampler_CameraDepthTexture,i.uv).x * 1.8f;
        return float4(camDepth,lightDepth,0,0);
    }
    half4 reduce (v2f i) : SV_Target
    {
        #if SHADER_API_METAL
            int2 xy = (int2) (i.uv * (_MainTex_TexelSize.zw - 1));
            float4 texels[2] = {
                float4(_MainTex.mips[0][xy].rg, _MainTex.mips[0][xy + int2(1, 0)].rg),
                float4(_MainTex.mips[0][xy + int2(0, 1)].rg, _MainTex.mips[0][xy + 1].rg)
            };
        
            float4 r = float4(texels[0].rb, texels[1].rb);
            float4 g = float4(texels[0].ga, texels[1].ga);
        #else
            float4 r = _MainTex.GatherRed(sampler_MainTex, input.uv);
            float4 g = _MainTex.GatherGreen(sampler_MainTex, input.uv);
        #endif
        float minimum = min(min(min(r.x, r.y), r.z), r.w);
        float maximum = max(max(max(g.x, g.y), g.z), g.w);
        return float4(minimum, maximum, 1.0, 1.0);
    }
    ENDHLSL
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Blit"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment blit
            ENDHLSL
        }

        Pass
        {
            Name "Reduce"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment reduce
            ENDHLSL
        }
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Blit"
            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex vert
            #pragma fragment blit
            ENDHLSL
        }

        Pass
        {
            Name "Reduce"
            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex vert
            #pragma fragment reduce
            ENDHLSL
        }
    }
}
