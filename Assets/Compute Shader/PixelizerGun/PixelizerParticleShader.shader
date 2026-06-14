Shader "Unlit/PixelizerParticleShader"
{
    Properties
    {        
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
                float4 color : COLOR;
                float life : TEXCOORD1;     // normalized particle lifetime (0 → 1)
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 uv : TEXCOORD0;
                float4 color : COLOR;
                float life : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.life = v.life;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float v = 20;
                float2 gridUV = floor(i.uv * v)/v;
                fixed4 col = tex2D(_MainTex, gridUV);

                float2 uv = frac(i.uv * v);
                float cond = (uv.x > 0.05 && uv.y < 0.95 ? 0 : 1);

                clip(col.a - 0.1f);
                float4 finalCol = float4((col.rgb * cond + pow(col.rgb, 1.5)), col.r);
                return pow(finalCol,i.uv.z) * i.color;
            }
            ENDCG
        }
    }
}
