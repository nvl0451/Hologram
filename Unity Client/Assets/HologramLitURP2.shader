Shader "Custom/HologramLitURP_EmissiveFixed_Psychedelic"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _HologramTint ("Hologram Tint", Color) = (0, 1, 1, 1)
        _Alpha ("Alpha", Range(0,1)) = 1.0
        _ScanlineDensity ("Scanline Density", Float) = 150
        _FlickerSpeed ("Flicker Speed", Float) = 3
        _FractalStrength ("Fractal Strength", Range(0.0, 1.0)) = 0.05
        _FractalScale ("Fractal Scale", Range(0.0, 1.0)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 300

        Pass
        {
            Name "HologramLitURP"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseColor;
            float4 _HologramTint;
            float _Alpha;
            float _ScanlineDensity;
            float _FlickerSpeed;
            float _FractalStrength;
            float _FractalScale;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4 worldPos = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionHCS = TransformWorldToHClip(worldPos.xyz);
                OUT.uv = IN.uv;
                OUT.worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                OUT.worldPos = worldPos.xyz;
                return OUT;
            }

            float rand(float2 co)
            {
                return frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;

                float3 normal = normalize(IN.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float ndotl = saturate(dot(normal, viewDir));

                // Base texture and color application
                float4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float4 color = texColor * _BaseColor;

                // Scanline and flicker effect
                float scanline = sin(IN.uv.y * _ScanlineDensity + time * 10.0) * 0.05;
                float flicker = lerp(0.9, 1.1, rand(float2(time * _FlickerSpeed, IN.uv.x)));

                // Subtle fractal effect (low strength)
                float fractalEffect = sin(IN.uv.x * _FractalScale + time * 2.0) * sin(IN.uv.y * _FractalScale + time * 2.0);
                fractalEffect = fractalEffect * _FractalStrength;  // Subtle fractals

                // Final color with fractals lightly blended in
                float3 finalColor = lerp(color.rgb, _HologramTint.rgb, 0.6);
                finalColor *= (flicker + scanline + fractalEffect);

                // Emission effect (intense glow effect)
                float3 emission = _HologramTint.rgb * 2.0;
                emission *= (flicker + scanline + fractalEffect);

                // Output final color and emission (glow)
                float alpha = color.a * _Alpha;
                return float4(finalColor + emission, alpha);
            }
            ENDHLSL
        }
    }
}
