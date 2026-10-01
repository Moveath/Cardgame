Shader "CardGame/UI/ChestOuterOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI texture", 2D) = "white" {}
        _RearTex ("Rear alpha", 2D) = "black" {}
        _FrontTex ("Front alpha", 2D) = "black" {}
        _CavityTex ("Filled cavity alpha", 2D) = "black" {}
        _LidTex ("Current lid alpha", 2D) = "black" {}
        _RadiusUV ("Stroke and halo radius", Vector) = (0.04,0.04,0.06,0.06)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; fixed4 color : COLOR; };
            sampler2D _RearTex, _FrontTex, _CavityTex, _LidTex;
            float4 _RadiusUV;
            float4 _ClipRect;

            v2f vert(appdata input)
            {
                v2f output;
                output.local = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half silhouette(float2 uv)
            {
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                half body = max(tex2D(_RearTex, uv).a, tex2D(_FrontTex, uv).a);
                body = max(body, tex2D(_CavityTex, uv).a);
                body = max(body, tex2D(_LidTex, uv).a);
                return smoothstep(0.08, 0.38, body);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                half center = silhouette(input.uv);
                // Filled interior pixels cannot contribute to an outer contour.
                if (center > 0.995) return fixed4(0, 0, 0, 0);
                half stroke = center;
                half halo = center;
                // Sixteen directions make the outer thickness continuous around corners;
                // unlike per-layer UI Outline, wood RGB never darkens the cyan contour.
                [unroll] for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * 0.3926990817;
                    float2 offset = float2(cos(angle), sin(angle));
                    stroke = max(stroke, silhouette(input.uv + offset * _RadiusUV.xy));
                    if ((direction % 2) == 0)
                        halo = max(halo, silhouette(input.uv + offset * _RadiusUV.zw));
                }
                half outer = saturate(stroke - center);
                half alpha = outer + saturate(halo - center) * (1.0 - outer) * 0.18;
                fixed4 result = fixed4(input.color.rgb, alpha * input.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.local.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
