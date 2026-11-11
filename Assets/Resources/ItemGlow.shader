Shader "Custom/ItemGlow"
{
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.35, 0.85, 1.0, 1.0)
        _Intensity ("Glow Intensity", Range(0, 3)) = 1.2
        _Falloff ("Glow Falloff", Range(1, 8)) = 3.2
        _AlphaStrength ("Window Alpha", Range(0, 1)) = 0.6
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 2.4
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _GlowColor;
            float _Intensity;
            float _Falloff;
            float _AlphaStrength;
            float _PulseSpeed;
            float _PulseAmount;

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 以精灵中心为光源的径向衰减
                float d = distance(i.texcoord, float2(0.5, 0.5)) * 2.0;
                half g = pow(saturate(1.0 - d), _Falloff);

                // 轻微呼吸，呈现"发光特效"而不是死色块
                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed);
                g *= pulse;

                half glow = g * _Intensity * i.color.a;
                fixed3 rgb = _GlowColor.rgb * glow;
                fixed a = saturate(g * _AlphaStrength);
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}
