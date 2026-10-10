using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGameClient
{
    /// <summary>
    /// Игра на телефоне (2026-10-10; замеры на OPPO Reno5 Lite, экран 2400×1080: было 17 кадров в секунду, видеокарта —
    /// 110 мс на кадр, процессор — 3–7 мс). На ПК ничего не меняет.
    /// - 60 кадров в секунду: Unity на телефоне по умолчанию держит 30.
    /// - Без пост-обработки камеры: вся игра — интерфейс поверх неё (Screen Space Overlay), свечение, виньетка
    ///   и тонирование камеры рисуются под ним впустую. Без них — 35 кадров.
    /// - Камера рисует прямо в экран: масштаб отрисовки URP 1 и без HDR. С масштабом 0,8 и HDR она гнала пустую сцену
    ///   через промежуточную текстуру и лишний проход — 12 мс видеокарты из 30 на площади.
    /// - Отрисовка не выше <see cref="MaxRenderHeight"/> строк, телефон сам растягивает кадр на весь экран:
    ///   2400×1080 с несжатыми картинками видеокарта не тянет. 810 строк — 58–60 кадров (864 — 50, 918 — 46).
    /// </summary>
    internal static class MobilePerformance
    {
        // Сцена меню — 1600×900: 810 строк — она в 0,9 своего размера, картинки и буквы ещё чёткие.
        private const int MaxRenderHeight = 810;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (!Application.isMobilePlatform) return;
            Application.targetFrameRate = 60;
            var shortSide = Mathf.Min(Screen.width, Screen.height);
            if (shortSide > MaxRenderHeight)
            {
                var scale = (float)MaxRenderHeight / shortSide;
                Screen.SetResolution(Mathf.RoundToInt(Screen.width * scale), Mathf.RoundToInt(Screen.height * scale), true);
            }
            SceneManager.sceneLoaded += (scene, mode) => NoPostProcessing();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            if (Application.isMobilePlatform) NoPostProcessing();
        }

        // Через имя компонента URP, а не его тип: скрипты собираются и в проекте проверки без URP (qa-project).
        private static void NoPostProcessing()
        {
            foreach (var camera in Camera.allCameras)
            {
                var data = camera.GetComponent("UniversalAdditionalCameraData");
                data?.GetType().GetProperty("renderPostProcessing")?.SetValue(data, false);
            }
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            pipeline?.GetType().GetProperty("renderScale")?.SetValue(pipeline, 1f);
            pipeline?.GetType().GetProperty("supportsHDR")?.SetValue(pipeline, false);
        }
    }
}
