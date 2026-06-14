Shader "Hidden/SampleDepthShader"
{
    SubShader
    {
        Tags{ "RenderPipeline"="UniversalPipeline" }   
        Pass
        {
            ZTest Always ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            //we are using TEXTURE2D_X because URP gives us MSAA which means there are 4 samples per pixel
            //depth cant be fetched from 4 different pixel values hence we do TEXTURE2D_X 
            //using this Unity automatically binds depth to this
            TEXTURE2D_X(_CameraDepthTexture); 
            SAMPLER(sampler_CameraDepthTexture);

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }
            float Frag(Varyings i) : SV_Target
            {
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,sampler_CameraDepthTexture,i.uv);
                float eye = LinearEyeDepth(raw, _ZBufferParams);
                return saturate(eye / _ProjectionParams.z);
                // return eye * 0.2;
            }
            ENDHLSL
        }
    }
}
