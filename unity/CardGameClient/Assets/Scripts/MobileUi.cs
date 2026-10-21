using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Phone controls use their own density; artwork and animation coordinates stay intact.</summary>
    internal static class MobileUi
    {
        // Also used by the isolated editor/device QA. Never persisted in the player's settings.
        public static bool? TestPhone;
        public static bool Phone => TestPhone ?? (Application.isMobilePlatform &&
            (Screen.dpi <= 0 || Mathf.Sqrt((float)Display.main.systemWidth * Display.main.systemWidth +
                (float)Display.main.systemHeight * Display.main.systemHeight) / Screen.dpi < 7.5f));

        public static int TextSize(int size) => Phone && size < 44
            ? Mathf.Max(size, Mathf.RoundToInt(size * 1.22f)) : size;

        public static void ConfigureTableCanvas(CanvasScaler scaler, float height = 820)
        {
            if (!Phone || scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        public static void EnlargeLabels(Transform root)
        {
            if (!Phone || root == null) return;
            foreach (var label in root.GetComponentsInChildren<Text>(true))
            {
                if (label.fontSize >= 44 || string.IsNullOrEmpty(label.text) || label.GetComponent<MobileReadableLabel>() != null) continue;
                label.gameObject.AddComponent<MobileReadableLabel>();
                var original = label.fontSize;
                var rect = label.rectTransform.rect;
                // Use the existing box. Long descriptions keep wrapping; no enlarged label covers a neighbour.
                var bestFit = label.resizeTextForBestFit;
                label.resizeTextForBestFit = false;
                for (var size = TextSize(original); size > original; size--)
                {
                    label.fontSize = size;
                    if (label.preferredHeight <= rect.height + 1 &&
                        (label.horizontalOverflow == HorizontalWrapMode.Wrap || label.preferredWidth <= rect.width + 1)) break;
                    label.fontSize = original;
                }
                if (bestFit) label.resizeTextMaxSize = Mathf.Max(label.resizeTextMaxSize, label.fontSize);
                label.resizeTextForBestFit = bestFit;
            }
        }
    }

    internal sealed class MobileReadableLabel : MonoBehaviour { }
}
