Shader "Custom/Hologram2"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color Tint", Color) = (0, 1, 1, 1)
        _ScanlineDensity ("Scanline Density", Float) = 100
        _FlickerSpeed ("Flicker Speed", Float) = 5
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _ScanlineDensity;
            float _FlickerSpeed;
            float _Alpha;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float rand(float2 co)
            {
                return frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float time = _Time.y;
                float2 uv = i.uv;

                // Sample texture
                fixed4 texColor = tex2D(_MainTex, uv);

                // Scanlines
                float scanline = sin(uv.y * _ScanlineDensity + time * 10) * 0.1;

                // Flicker
                float flicker = lerp(0.85, 1.05, rand(float2(time * _FlickerSpeed, uv.x)));

                // Combine
                fixed4 finalColor = texColor * _Color * flicker;
                finalColor.rgb += scanline;
                finalColor.a *= _Alpha;

                return finalColor;
            }
            ENDCG
        }
    }
}
