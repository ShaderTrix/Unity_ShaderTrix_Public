Shader "Unlit/VertexAnimationTextureGraphics"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PositionTex("Position Texture",2D) = "black"{}
        _NormalTex("Normal Texture",2D) = "white"{}
        _AnimatorLength("Speed",float) = 1        
    }
    SubShader
    {
        Name "ForwardLit"
        Tags{ "LightMode" = "UniversalForward" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag            

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"   

            struct appdata
            {                
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;                
                float3 normal : TEXCOORD1;                
            };

            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);float4 _MainTex_ST;
            TEXTURE2D(_PositionTex);SAMPLER(sampler_PositionTex);float4 _Position_ST;float4 _PositionTex_TexelSize;
            TEXTURE2D(_NormalTex);SAMPLER(sampler_NormalTex);float4 _Normal_ST;
            half _AnimatorLength;

            v2f vert (appdata v,uint id : SV_VertexID)
            {
                //float t = frac((_Time.y - _DeltaTime) / _AnimatorLength);
                float t = frac(_Time.y / _AnimatorLength);
                float x = (id + 0.5) * _PositionTex_TexelSize.x;
                float y = t;
                float4 position = SAMPLE_TEXTURE2D_LOD(_PositionTex,sampler_PositionTex,float2(x,y),0);
                float4 normal = SAMPLE_TEXTURE2D_LOD(_NormalTex,sampler_NormalTex,float2(x,y),0);
                //float4 newPos = lerp(position,normal,clamp(0,1,t/_DeltaTime));

                v2f o;
                o.vertex = GetVertexPositionInputs(position.xyz).positionCS;
                o.normal = GetVertexNormalInputs(normal.xyz).normalWS;
                o.uv = v.uv;                            
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {        
                half diffuse = dot(i.normal,float3(0,1,0)) * 0.5f + 0.5f;
                half4 col = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                return diffuse * col;
            }
            ENDHLSL
        }
    }
}
