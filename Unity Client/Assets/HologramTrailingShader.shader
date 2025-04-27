Shader "Custom/HologramTrailShader"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _HologramTint ("Hologram Tint", Color) = (0, 1, 1, 1)
        _Alpha ("Alpha", Range(0,1)) = 1.0
        _TrailIntensity ("Trail Intensity", Range(0.0, 1.0)) = 0.5
        _TrailTime ("Trail Time", Range(0.1, 5.0)) = 2.0  // Time for the trail to fade completely
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }
        LOD 300

        Pass
        {
            Name "HologramTrailPass"
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
                float timeDifference : TEXCOORD3; // Time difference to control fading
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseColor;
            float4 _HologramTint;
            float _Alpha;
            float _TrailIntensity;
            float _TrailTime;  // Time for the trail to fade

            // Calculate the world position and time-based effect for trail
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4 worldPos = mul(unity_ObjectToWorld, IN.positionOS);
                OUT.positionHCS = TransformWorldToHClip(worldPos.xyz);
                OUT.uv = IN.uv;
                OUT.worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                OUT.worldPos = worldPos.xyz;

                // Calculate the time difference between the fragment and the current time
                OUT.timeDifference = _Time.y - frac(_Time.y); // Simplified time tracking
                return OUT;
            }

            // Function to simulate the trail fade effect
            float getTrailAlpha(float timeDifference)
            {
                // We calculate a fade factor based on the time difference. The longer it's been, the more it fades.
                float fadeFactor = saturate((timeDifference / _TrailTime));
                return 1.0 - fadeFactor;  // Fade out over time
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float time = _Time.y;

                // Base texture and color application
                float4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float4 color = texColor * _BaseColor;

                // Fading effect for trail based on the time difference
                float trailAlpha = getTrailAlpha(IN.timeDifference);
                trailAlpha *= _TrailIntensity; // Control the overall trail intensity

                // Apply trail color and effect
                float3 finalColor = lerp(color.rgb, _HologramTint.rgb, trailAlpha);
                finalColor *= trailAlpha;

                // Emission effect (to make the trail glow slightly)
                float3 emission = _HologramTint.rgb * 2.0 * trailAlpha;

                // Final color output with fading trail and emission
                float alpha = color.a * _Alpha * trailAlpha;
                return float4(finalColor + emission, alpha);
            }
            ENDHLSL
        }
    }
}
