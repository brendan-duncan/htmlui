// Turns a DevTools screencast frame into the texture the runtime expects: premultiplied alpha, first row
// at the top of the page. Blit with GL.sRGBWrite off so the browser's encoded bytes pass through unchanged.
Shader "Hidden/Hiccup/EditorPremultiply"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FlipY ("Flip Y", Float) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float _FlipY;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // LoadImage gives a bottom-up texture; HtmlDocument.TextureIsTopDown promises the opposite.
                float2 uv = float2(i.uv.x, lerp(i.uv.y, 1.0 - i.uv.y, _FlipY));
                fixed4 c = tex2D(_MainTex, uv);
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
