Shader "Unlit/VisceraDecalShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MetallicSmoothnessTex ("Metallic/Smoothness", 2D) = "white" {}
    }
    SubShader
    {
        Tags{ 
             "RenderPipeline"="UniversalPipeline" 
             "RenderType"="Opaque"
             "Render Queue"="Opaque"}        
        Pass
        {
            Name "UniversalForward"
            Tags {"LightMode"="UniversalForward"}
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 rayPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };
            struct MRTOutput
            {
                float4 color : SV_Target0;
                float4 uv_Depth : SV_Target1;
            };
            TEXTURE2D(_MainTex); SamplerState sampler_MainTex_Repeat_Trilinear_Aniso16;
            TEXTURE2D(_MetallicSmoothnessTex); SamplerState sampler_MetallicSmoothnessTex_Repeat_Trilinear_Aniso16;

            v2f vert (appdata v)
            {
                v2f o;
                o.uv = v.uv;
                float3 worldPos = GetVertexPositionInputs(v.vertex.xyz).positionWS;
				o.vertex = TransformWorldToHClip(worldPos);
				o.rayPos = worldPos - _WorldSpaceCameraPos;
				o.screenPos = ComputeScreenPos (o.vertex);
                return o;
            }

            MRTOutput frag (v2f i)
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;

				float rawDepth = SampleSceneDepth(screenUV);
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

				float3 rayDir = normalize(i.rayPos);
                float3 cameraForward = UNITY_MATRIX_V[2].xyz;
				rayDir /= dot(rayDir, -cameraForward);

				float3 worldPos = _WorldSpaceCameraPos + rayDir * linearDepth;
				float3 objectPos =  mul (unity_WorldToObject, float4(worldPos,1)).xyz;
				clip(0.5 - abs(objectPos));
				objectPos += 0.5;

                float4 s = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex_Repeat_Trilinear_Aniso16, objectPos.xz);
                float rm = SAMPLE_TEXTURE2D(_MetallicSmoothnessTex,sampler_MetallicSmoothnessTex_Repeat_Trilinear_Aniso16, objectPos.xz).x;

                clip(s.a - 0.1);
                MRTOutput o;
                o.color = s;
                o.uv_Depth = s * s.a;
                return o;
            }
            ENDHLSL
        }        
    }
}
