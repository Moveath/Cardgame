using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CardGameClient.ThreeD;

namespace CardGameClient
{
    /// <summary>
    /// Поднимает главное меню при запуске игры, не требуя настройки сцены руками.
    /// Временное решение на период прототипа: позже сцену соберём из префабов.
    /// </summary>
    public static class DurakBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            // RuntimeInitializeOnLoadMethod runs once. Subscribe as well so returning from
            // CardTable3D to the empty SampleScene rebuilds the original 2D shell.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureShell();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureShell();

        private static void EnsureShell()
        {
            if (Object.FindAnyObjectByType<CardTable3DBootstrap>() != null)
            {
                return;
            }

            // The isolated 3D table owns its camera and HUD; do not cover it with the legacy shell.
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                if (SceneManager.GetSceneAt(sceneIndex).path == "Assets/Scenes/CardTable3D.unity")
                {
                    return;
                }
            }

            if (Object.FindAnyObjectByType<AppFlowController>() != null)
            {
                return;
            }

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
                var module = eventSystem.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }

            var canvasObject = new GameObject("DurakCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            MobileUi.ConfigureTableCanvas(scaler);

            canvasObject.AddComponent<AppFlowController>();
        }
    }
}
