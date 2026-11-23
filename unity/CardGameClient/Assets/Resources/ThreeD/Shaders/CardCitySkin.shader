// Кожа рук для URP. Поверхность, нормали, тени, SSAO и окружающий свет считает стандартный код URP Lit,
// своё только освещение: свет под кожей заходит за край тени и окрашивает его в тёплый красный, мелкий рельеф
// в рассеянном свете мягче, чем в блике, ногти по маске глаже и светлее, а складки на суставах шершавее и не блестят.
// Без этого кожа выглядит пластиком.
Shader "CardCity/Skin"
{
    Properties
    {
        [MainTexture] _BaseMap("Кожа", 2D) = "white" {}
        [MainColor] _BaseColor("Оттенок кожи", Color) = (1, 1, 1, 1)
        _Smoothness("Гладкость", Range(0, 1)) = 0.4
        _BumpScale("Сила рельефа", Float) = 1
        [Normal] _BumpMap("Рельеф", 2D) = "bump" {}

        _NailMask("Маска: R — ногти, G — складки", 2D) = "black" {}
        _CreaseRoughness("Шероховатость складок", Range(0, 1)) = 0.6
        _NailColor("Цвет ногтей", Color) = (0.9, 0.74, 0.68, 1)
        _NailTint("Подкраска ногтя этим цветом", Range(0, 1)) = 0.15
        _NailSmoothness("Гладкость ногтей", Range(0, 1)) = 0.8
        _SubsurfaceColor("Цвет под кожей", Color) = (0.95, 0.35, 0.22, 1)
        _Wrap("Заход света за край тени", Range(0, 1)) = 0.55
        _Scatter("Сила рассеивания", Range(0, 2)) = 1
        _DiffuseSoftness("Мягкость рельефа в рассеянном свете", Range(0, 1)) = 0.45

        // Остальное читает общий код URP Lit.
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _Metallic("Metallic", Range(0, 1)) = 0
        [HideInInspector] _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        [HideInInspector] _OcclusionStrength("Occlusion", Range(0, 1)) = 1
        [HideInInspector] _EmissionColor("Emission", Color) = (0, 0, 0)
        [HideInInspector] _Parallax("Parallax", Range(0.005, 0.08)) = 0.005
        [HideInInspector] _DetailAlbedoMapScale("Detail", Range(0, 2)) = 1
        [HideInInspector] _DetailNormalMapScale("Detail normal", Range(0, 2)) = 1
        [HideInInspector] _ClearCoatMask("Clear coat", Range(0, 1)) = 0
        [HideInInspector] _ClearCoatSmoothness("Clear coat smoothness", Range(0, 1)) = 0
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment SkinPassFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            TEXTURE2D(_NailMask);
            SAMPLER(sampler_NailMask);
            half4 _NailColor;
            half4 _SubsurfaceColor;
            half _NailSmoothness;
            half _NailTint;
            half _Wrap;
            half _Scatter;
            half _DiffuseSoftness;
            half _CreaseRoughness;

            // Один источник: блик по точному рельефу, рассеянный свет по сглаженному, плюс тёплая полоса за краем тени.
            half3 SkinLight(BRDFData brdfData, Light light, half3 normalWS, half3 diffuseNormalWS, half3 viewDirectionWS, half scatter)
            {
                half3 radiance = light.color * light.distanceAttenuation;
                half NdotL = dot(diffuseNormalWS, light.direction);
                half lit = saturate(NdotL);
                half wrapped = saturate((NdotL + _Wrap) / (1.0h + _Wrap));
                half band = saturate(wrapped - lit);
                // Свет под кожей заходит и в край падающей тени, поэтому для него тень чуть мягче. В глубине тени он почти гаснет:
                // при доле 0,35 ладонь, повёрнутая к играющему (хват ребром большого пальца), горела красным пятном.
                half softShadow = lerp(light.shadowAttenuation, 1.0h, 0.15h);
                half3 diffuse = brdfData.diffuse * (lit * light.shadowAttenuation + band * scatter * _SubsurfaceColor.rgb * softShadow);
                half3 specular = 0;
                #ifndef _SPECULARHIGHLIGHTS_OFF
                specular = brdfData.specular * DirectBRDFSpecular(brdfData, normalWS, light.direction, viewDirectionWS)
                    * saturate(dot(normalWS, light.direction)) * light.shadowAttenuation;
                #endif
                return (diffuse + specular) * radiance;
            }

            void SkinPassFragment(
                Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);
                half2 detail = SAMPLE_TEXTURE2D(_NailMask, sampler_NailMask, input.uv).rg;
                half nail = detail.r;
                // Ноготь нарисован в карте кожи (пластина, лунка, светлый край); цвет материала только слегка подкрашивает его.
                surfaceData.albedo = lerp(surfaceData.albedo, _NailColor.rgb, nail * _NailTint);
                // Карта шероховатости: в складках кожа матовая, между ними — мягкий рассеянный блик.
                surfaceData.smoothness *= 1.0h - detail.g * _CreaseRoughness;
                surfaceData.smoothness = lerp(surfaceData.smoothness, _NailSmoothness, nail);

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));
                InitializeBakedGIData(input, inputData);

                BRDFData brdfData;
                InitializeBRDFData(surfaceData, brdfData);
                BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
                half4 shadowMask = CalculateShadowMask(inputData);
                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
                Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
                MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

                half3 diffuseNormal = normalize(lerp(inputData.normalWS, normalize(input.normalWS), _DiffuseSoftness));
                // Ноготь тонкий: свет под ним доходит до розового ложа, но слабее, чем в коже.
                half scatter = _Scatter * (1.0h - 0.5h * nail);

                half3 color = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                    inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                    inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
                // Слои света, как у стандартного Lit: заполняющий свет рук (OwnArms3DView) светит только на руки.
                uint meshRenderingLayers = GetMeshRenderingLayer();
                #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
                #endif
                    color += SkinLight(brdfData, mainLight, inputData.normalWS, diffuseNormal, inputData.viewDirectionWS, scatter);

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                    #endif
                        color += SkinLight(brdfData, light, inputData.normalWS, diffuseNormal, inputData.viewDirectionWS, scatter);
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                    #endif
                        color += SkinLight(brdfData, light, inputData.normalWS, diffuseNormal, inputData.viewDirectionWS, scatter);
                LIGHT_LOOP_END
                #endif

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                color += inputData.vertexLighting * brdfData.diffuse;
                #endif

                outColor = half4(MixFog(color, inputData.fogCoord), 1.0h);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // Нормали для SSAO: без этого прохода затенение в складках между пальцами не работает.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
