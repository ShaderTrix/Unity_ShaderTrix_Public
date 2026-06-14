Shader "Hidden/MRTDownSample"
{
    SubShader
    {
        Tags {"RenderType"="Opaque"}
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BlitTexture);SAMPLER(sampler_BlitTexture);
            int _BlockSize;

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f Vert(uint id : SV_VertexID)
            {
                v2f o;
                o.uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(o.uv * 2 - 1, 0, 1);
                return o;
            }

            float4 Frag(v2f i) : SV_Target
            {                
                float2 uv = float2(i.uv.x, 1 - i.uv.y);
                float2 floorUV = floor(uv * _BlockSize)/_BlockSize;
                float rawDepth = SampleSceneDepth(uv);
                float depth01 = Linear01Depth(rawDepth,_ZBufferParams);
                float4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture, uv);
                // if(col.x < depth01)
                //     discard;
                return col.a;
            }
            ENDHLSL
        }
    }
}
