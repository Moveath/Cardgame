Shader "UI/GovnoStickerCutout"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _KeyThreshold ("Magenta Threshold", Range(0, 0.5)) = 0.05
        _KeySoftness ("Magenta Softness", Range(0.01, 1)) = 0.73
        _Despill ("Magenta Edge Cleanup", Range(0, 1)) = 1
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
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

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
            Name "StickerCutout"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                half4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            half _KeyThreshold;
            half _KeySoftness;
            half _Despill;

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;

                // Match uGUI's pixel-correct RectMask2D softness.
                float2 pixelSize = output.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = half4(input.vertex.xy * 2 - clipRect.xy - clipRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                half4 source = tex2D(_MainTex, input.uv) + _TextureSampleAdd;

                // Both red and blue must exceed green. White coats, blue clothing,
                // brown mud and neutral outlines therefore keep their original alpha.
                half magenta = saturate(min(source.r, source.b) - source.g);
                half key = smoothstep(_KeyThreshold,
                    _KeyThreshold + max(_KeySoftness, 0.001h), magenta);

                // Unmix the magenta backing separately from the smoothed alpha.
                // Using the smoothed key for RGB would turn dark edge pixels green.
                half3 clean = saturate((source.rgb - half3(magenta, 0, magenta))
                    / max(1 - magenta, 0.001h));
                half cleanup = _Despill * smoothstep(0.01h, 0.1h, magenta);
                source.rgb = lerp(source.rgb, clean, cleanup);
                source.a *= 1 - key;

                // Keying precedes tint so fades and recolouring do not expose the backing.
                half4 result = source * input.color;
                #ifdef UNITY_UI_CLIP_RECT
                half2 clipMask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                result.a *= clipMask.x * clipMask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001h);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
