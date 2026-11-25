// Дымчато-хрустальный гранёный стакан для URP (работает на ПК Forward+ и мобильном Forward без OpaqueTexture).
// Реализует именно слегка просвечивающийся дымчатый хрусталь с тяжёлым толстым дном (как на фото-референсе):
//   - Гладкие верхние стенки: мягкая дымчато-шампанская полупрозрачность (слегка просвечивает сукно, виден объём стекла).
//   - Нижние 32 вертикальные грани и 8 пиков «короны»: плотный гранёный хрусталь с контрастом тёмных преломлений и ярких бликов.
//   - Толстое дно (2,15 см): ощутимая массивная хрустальная линза с кольцом преломления по краю дна и лучами от 32 граней
//     (не исчезает в голое красное сукно и не светится белым).
Shader "CardCity/CrystalGlass"
{
    Properties
    {
        _GlassTint("Дымчатый цвет стенок", Color) = (0.43, 0.36, 0.30, 1)
        _BaseTint("Тёплый цвет толстого хрусталя", Color) = (0.54, 0.42, 0.30, 1)
        _RefractDark("Тёмное преломление в гранях", Color) = (0.15, 0.10, 0.08, 1)
        _CenterAlpha("Плотность центра стенки", Range(0.05, 0.65)) = 0.28
        _EdgeAlpha("Плотность края (Френель)", Range(0.30, 0.96)) = 0.82
        _BackWallAlpha("Плотность задней стенки", Range(0.05, 0.55)) = 0.24
        _FloorAlpha("Плотность толстого дна", Range(0.25, 0.90)) = 0.62
        _FresnelPower("Резкость Френеля", Range(1.2, 5.5)) = 2.6
        _FacetBoost("Контраст и блеск граней", Range(0.0, 2.5)) = 1.35
        _SpecularStrength("Сила бликов ламп", Range(0.0, 4.0)) = 1.85
        _EnvReflection("Отражение клуба", Range(0.0, 2.0)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GlassTint;
            half4 _BaseTint;
            half4 _RefractDark;
            half _CenterAlpha;
            half _EdgeAlpha;
            half _BackWallAlpha;
            half _FloorAlpha;
            half _FresnelPower;
            half _FacetBoost;
            half _SpecularStrength;
            half _EnvReflection;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3  normalWS   : TEXCOORD1;
            half4  color      : TEXCOORD2;
            float3 localPos   : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings GlassVert(Attributes IN)
        {
            Varyings OUT;
            UNITY_SETUP_INSTANCE_ID(IN);
            UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

            VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
            VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
            OUT.positionCS = pos.positionCS;
            OUT.positionWS = pos.positionWS;
            OUT.normalWS = half3(nrm.normalWS);
            OUT.color = IN.color;
            OUT.localPos = IN.positionOS.xyz;
            return OUT;
        }

        // Тёплое клубное окружение: две мягкие вертикальные полосы на дымчатом стекле + контраст светлых и тёмных граней.
        half3 SampleClubEnv(half3 R, half facet, half wallMask)
        {
            half horiz = exp(-abs(R.y - 0.10h) * 2.8h);
            half2 rxz = normalize(R.xz + half2(1e-4h, 0));
            half stripA = pow(saturate(dot(rxz, normalize(half2(0.76h, 0.65h)))), 5.0h);
            half stripB = pow(saturate(dot(rxz, normalize(half2(-0.82h, -0.57h)))), 4.5h);
            half3 env = half3(0.95h, 0.86h, 0.75h) * horiz * (stripA * 0.56h + stripB * 0.42h + 0.14h) * wallMask;

            // Мягкий верхний свет люстры (приглушён на плоском дне, чтобы дно было хрустальным, а не белым диском)
            half topLamp = smoothstep(0.45h, 0.96h, R.y) * lerp(0.18h, 0.85h, wallMask);
            env += half3(1.00h, 0.90h, 0.74h) * topLamp * 0.44h;

            // Отсвет бордового сукна и тёплого шампанского хрусталя снизу
            half bottom = saturate(-R.y);
            env += lerp(half3(0.24h, 0.08h, 0.06h), half3(0.62h, 0.46h, 0.30h), facet * wallMask) * bottom * 0.46h;
            return env;
        }

        // Текстура преломлений толстого хрустального дна: светлое кольцо толщины стенки по периметру + мягкие лучи 32 граней.
        void EvalCrystalFloor(float3 localPos, out half floorRing, out half floorRays)
        {
            half r = half(length(localPos.xz));
            half rNorm = saturate(r / 0.036h);
            // Светлое кольцо внутреннего скоса дна (rNorm = 0.72..1.0) — создаёт чёткое ощущение 2-сантиметровой толщины дна!
            floorRing = smoothstep(0.62h, 0.94h, rNorm);
            // Тонкий внутренний кольцевой блик «линзы» дна (punt)
            half innerLens = exp(-pow((rNorm - 0.38h) * 4.5h, 2.0h)) * 0.35h;
            // Мягкие преломления от 32 граней и 8 пиков короны, переходящие в глубокую хрустальную линзу дна
            half ang = half(atan2(localPos.z, localPos.x));
            half rays32 = 0.5h + 0.5h * cos(ang * 32.0h);
            half rays8 = 0.5h + 0.5h * cos(ang * 8.0h + 3.14159h);
            floorRays = (rays32 * 0.22h + rays8 * 0.26h + 0.22h) * smoothstep(0.18h, 0.92h, rNorm) + innerLens;
        }

        half3 EvalCrystalLight(half3 N, half3 V, Light light, half facet, half wallMask, half specScale)
        {
            half3 L = normalize(light.direction);
            half3 H = normalize(L + V);
            half NdotH = saturate(dot(N, H));
            half NdotL = saturate(dot(N, L));

            // На плоском дне оставляем мягкий контролируемый блеск стекла, без белого пересвета
            half floorDamp = lerp(0.26h, 1.0h, wallMask);
            half sharp = pow(NdotH, 220.0h) * 1.85h * floorDamp;
            half gloss = pow(NdotH, 34.0h) * (0.14h + 0.30h * facet) * lerp(0.30h, 1.0h, wallMask);
            half prism = pow(1.0h - saturate(dot(N, V)), 2.0h) * (NdotL * 0.65h + 0.35h) * facet * wallMask * 0.30h;

            half3 radiance = light.color * light.distanceAttenuation;
            return radiance * (sharp + gloss + prism) * specScale;
        }
        ENDHLSL

        // Проход 1: задняя / внутренняя стенка и массивное хрустальное дно (ощущается толщина и глубина стекла)
        Pass
        {
            Name "CrystalBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex GlassVert
            #pragma fragment GlassBackFrag
            #pragma multi_compile_instancing

            half4 GlassBackFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half3 N = -normalize(IN.normalWS);
                half3 V = half3(GetWorldSpaceNormalizeViewDir(IN.positionWS));
                half NdotV = saturate(dot(N, V));
                half fres = pow(1.0h - NdotV, _FresnelPower * 0.90h);

                half wallMask = 1.0h - smoothstep(0.45h, 0.85h, abs(N.y));
                half floorMask = 1.0h - wallMask;
                half facet = saturate(IN.color.r) * wallMask;
                half baseMask = 1.0h - smoothstep(0.015h, 0.048h, half(IN.localPos.y));
                half cutEdge = saturate(length(fwidth(N)) * 1.05h) * saturate(NdotV * 2.2h) * facet;

                half floorRing, floorRays;
                EvalCrystalFloor(IN.localPos, floorRing, floorRays);

                half3 tint = lerp(_GlassTint.rgb, _BaseTint.rgb, max(baseMask * 0.72h, floorMask * 0.80h));
                half3 R = reflect(-V, N);
                half3 env = SampleClubEnv(R, facet, wallMask) * _EnvReflection * (0.24h + 0.45h * fres);

                Light mainLight = GetMainLight();
                half3 spec = EvalCrystalLight(N, V, mainLight, facet, wallMask, _SpecularStrength * 0.34h);

                // На горизонтальном дне видна толща дымчатого хрусталя с кольцом преломления — сукно лишь мягко просвечивает
                half wallAlpha = _BackWallAlpha * (0.45h + 0.55h * fres) + facet * 0.20h + baseMask * wallMask * 0.12h;
                half floorA = _FloorAlpha * (0.58h + 0.34h * floorRing + 0.14h * floorRays);
                half alpha = saturate(lerp(floorA, wallAlpha, wallMask));

                // Дымчатая масса стекла + мягкое тёплое преломление кольца дна и задних граней
                half3 floorBody = lerp(_RefractDark.rgb * 1.45h, _BaseTint.rgb * 1.05h, floorRing * 0.70h + floorRays * 0.35h);
                half3 bodyCol = lerp(floorBody, tint * 0.78h, wallMask) * alpha;
                half3 floorGleam = half3(0.88h, 0.76h, 0.60h) * floorMask * (floorRing * 0.16h + floorRays * 0.10h);

                half3 col = bodyCol + env * (0.20h + 0.48h * fres) * wallMask + spec + half3(0.95h, 0.85h, 0.70h) * cutEdge * 0.22h + floorGleam;
                return half4(col, alpha);
            }
            ENDHLSL
        }

        // Проход 2: передние дымчатые стенки, 32 грани, 8 пиков «короны» и верхняя грань толстого внутреннего дна
        Pass
        {
            Name "CrystalFront"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex GlassVert
            #pragma fragment GlassFrontFrag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing

            half4 GlassFrontFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half3 N = normalize(IN.normalWS);
                half3 V = half3(GetWorldSpaceNormalizeViewDir(IN.positionWS));
                half NdotV = saturate(dot(N, V));

                half fresRaw = pow(1.0h - NdotV, _FresnelPower);
                half fres = 0.05h + 0.95h * fresRaw;

                half wallMask = 1.0h - smoothstep(0.45h, 0.85h, abs(N.y));
                half floorMask = 1.0h - wallMask;
                half facet = saturate(IN.color.r) * wallMask;
                half baseMask = 1.0h - smoothstep(0.015h, 0.050h, half(IN.localPos.y));
                half cutEdge = saturate(length(fwidth(N)) * 1.15h) * saturate(NdotV * 2.4h) * (0.20h + 0.80h * facet) * wallMask;

                half floorRing, floorRays;
                EvalCrystalFloor(IN.localPos, floorRing, floorRays);

                // Контраст преломлений на гранях хрусталя: часть граней отражает тёмный зал, соседние — тёплый свет!
                half3 R = reflect(-V, N);
                half facetContrast = saturate(dot(normalize(N.xz + half2(1e-4h, 0)), normalize(half2(0.55h, 0.83h))) * 0.5h + 0.5h);
                half darkGroove = (1.0h - facetContrast) * facet * 0.75h;

                half3 wallBaseTint = lerp(_GlassTint.rgb, _BaseTint.rgb, baseMask * 0.72h);
                wallBaseTint = lerp(wallBaseTint, _RefractDark.rgb, darkGroove);

                half3 env = SampleClubEnv(R, facet, wallMask) * _EnvReflection;

                Light mainLight = GetMainLight();
                half3 spec = EvalCrystalLight(N, V, mainLight, facet, wallMask, _SpecularStrength);

                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    spec += EvalCrystalLight(N, V, addLight, facet, wallMask, _SpecularStrength * 0.72h);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, IN.positionWS);
                    spec += EvalCrystalLight(N, V, addLight, facet, wallMask, _SpecularStrength * 0.72h);
                LIGHT_LOOP_END
                #endif

                // Плотность дымчатого стекла:
                // - верхняя стенка: слегка просвечивает (0.26 в центре -> 0.80 на краю, ощущается цельное стекло!)
                // - нижняя огранка (32 грани + 8 пиков): плотнее (0.55..0.88), как толстый гранёный хрусталь
                // - внутреннее дно: плотное дымчато-хрустальное (0.48..0.72), дно чётко читается, а не пропадает!
                half wallAlpha = lerp(_CenterAlpha, _EdgeAlpha, fresRaw)
                    + facet * 0.28h * _FacetBoost + cutEdge * 0.14h + baseMask * wallMask * 0.16h;
                half floorA = _FloorAlpha * (0.48h + 0.36h * floorRing + 0.16h * floorRays);
                half alpha = saturate(lerp(floorA, wallAlpha, wallMask));

                // Тело дымчатого стекла + тёплое массивное дно с кольцом толщины
                half3 floorTint = lerp(_RefractDark.rgb * 1.65h, _BaseTint.rgb * 1.25h, floorRing * 0.65h + floorRays * 0.45h);
                half3 bodyTint = lerp(floorTint, wallBaseTint, wallMask) * (alpha * 0.90h);

                // Мягкий матово-дымчатый обод по силуэту стенки (как у стакана на фото!)
                half3 rimGlow = half3(0.84h, 0.76h, 0.68h) * fresRaw * wallMask * 0.32h;
                // Хрустальный отблеск внутреннего края дна (где стенка переходит в толстое дно)
                half3 floorEdgeGlow = half3(0.95h, 0.84h, 0.68h) * floorMask * (floorRing * 0.22h + floorRays * 0.14h);
                half3 reflection = env * (fres * 0.78h + facet * 0.30h * _FacetBoost + 0.08h * wallMask);
                half3 crystalEdge = half3(1.00h, 0.91h, 0.76h) * cutEdge * 0.62h * _FacetBoost;

                half3 col = bodyTint + rimGlow + floorEdgeGlow + reflection + spec + crystalEdge;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
