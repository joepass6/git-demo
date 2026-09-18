Shader "Custom/NeonPsychedelic2D"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _NeonColor1 ("Neon Color 1", Color) = (0.8, 0.2, 0.8, 1)
        _NeonColor2 ("Neon Color 2", Color) = (0.2, 0.8, 0.8, 1)
        _NeonColor3 ("Neon Color 3", Color) = (0.8, 0.8, 0.2, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 5)) = 2
        _FlowSpeed ("Flow Speed", Range(0, 10)) = 3
        _DistortionAmount ("Distortion", Range(0, 0.1)) = 0.02
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.05)) = 0.01
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 1
        _NoiseScale ("Noise Scale", Range(1, 50)) = 10
        _EffectStrength ("Effect Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
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

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _NeonColor1;
            float4 _NeonColor2;
            float4 _NeonColor3;
            float _GlowIntensity;
            float _FlowSpeed;
            float _DistortionAmount;
            float _ChromaticAberration;
            float _PulseSpeed;
            float _NoiseScale;
            float _EffectStrength;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float val = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                
                for (int i = 0; i < 4; i++)
                {
                    val += amp * noise(p * freq);
                    amp *= 0.5;
                    freq *= 2.0;
                }
                
                return val;
            }

            float3 rainbow(float t)
            {
                return float3(
                    sin(t * UNITY_PI) * 0.5 + 0.5,
                    sin(t * UNITY_PI + UNITY_PI * 0.66) * 0.5 + 0.5,
                    sin(t * UNITY_PI + UNITY_PI * 1.33) * 0.5 + 0.5
                );
            }

            float3 getNeonColor(float2 uv, float time)
            {
                float2 distortedUV = uv;
                distortedUV.x += fbm(uv * _NoiseScale + float2(time * _FlowSpeed, 0)) * _DistortionAmount;
                distortedUV.y += fbm(uv * _NoiseScale + float2(0, time * _FlowSpeed * 0.7)) * _DistortionAmount;
                
                float flow = fbm(distortedUV * _NoiseScale * 0.5 + float2(time * _FlowSpeed * 0.5, time * _FlowSpeed * 0.3));
                float rainbowShift = flow;
                
                float3 color1 = _NeonColor1.rgb;
                float3 color2 = _NeonColor2.rgb;
                float3 color3 = _NeonColor3.rgb;
                
                float3 finalColor = lerp(color1, color2, rainbowShift);
                finalColor = lerp(finalColor, color3, sin(rainbowShift * UNITY_PI * 2));
                
                float3 rainbowOverlay = rainbow(rainbowShift + time * 2) * 0.5;
                finalColor += rainbowOverlay;
                
                float glow = exp(-length(uv - 0.5) * 3) * _GlowIntensity;
                finalColor += glow;
                
                return finalColor;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float time = _Time.y;
                float2 uv = i.uv;
                
                float4 texColor = tex2D(_MainTex, uv);
                
                float2 offsetR = float2(_ChromaticAberration, 0);
                float2 offsetB = float2(-_ChromaticAberration, 0);
                
                float3 neonR = getNeonColor(uv + offsetR, time);
                float3 neonG = getNeonColor(uv, time);
                float3 neonB = getNeonColor(uv + offsetB, time);
                
                float3 neonColor = float3(neonR.r, neonG.g, neonB.b);
                
                float pulse = sin(time * _PulseSpeed) * 0.3 + 0.7;
                neonColor *= pulse;
                
                float3 finalColor = lerp(texColor.rgb, texColor.rgb + neonColor * _EffectStrength, _EffectStrength);
                float finalAlpha = texColor.a * i.color.a;
                
                return float4(finalColor, finalAlpha);
            }
            ENDCG
        }
    }
}
