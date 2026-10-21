// Ролик жителя поверх нарисованного зала (HubClubVideo): кадр видео, прозрачность — из маски того же размера.
// Мягкий край маски сводит ролик с картинкой без шва; UI-трафарет и RectMask2D работают, как у обычной картинки.
Shader "CardCity/HubVideoMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Video", 2D) = "black" {}
        _MaskTex ("Mask", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SceneryTone ("Soft light", Range(0, 1)) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
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
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "HubSceneryTone.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex, _MaskTex;
            fixed4 _Color;
            half _SceneryTone;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                color.rgb = CardCitySceneryTone(color.rgb, _SceneryTone);
                color *= i.color;
                color.a *= tex2D(_MaskTex, i.uv).r;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
