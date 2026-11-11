// 桌面窗口上屏用的 GPU 通道重排 Shader。
// 输入：相机渲染出的 RT（ARGB32）——Sprites/Default、TMP、本项目的发光 Shader 都已经是预乘 alpha
// 输出：仅做 BGR 通道重排（配合 ARGB32 目标的显存布局，ReadPixels 后正好是 DIB 需要的 BGRA 字节序）
//
// 重要：这里不再把 RGB 乘以 alpha。RT 里的颜色已经由各 Shader 的预乘混合
// （Blend One OneMinusSrcAlpha + 片元 rgb *= a）算好了；再乘一次会把半透明像素压暗、
// 并在边缘产生黑边（神明那批 alpha≈0.21 的对象几乎会消失）。
Shader "Hidden/Haodong/PremultiplyBGRA"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);

                // 输出 b,g,r：ARGB32 的显存布局是 B,G,R,A，取逻辑通道后又转回来，
                // 最终 ReadPixels 读进 RGBA32 纹理时内存正好是 B,G,R,A。
                return float4(c.b, c.g, c.r, c.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
