// Minimal host only for the tiny sign QA project; never copy into the main game.
// The sign implementation itself is copied unchanged from the production project.
using UnityEngine;

namespace CardGameClient
{
    public sealed partial class PlayerHubController : MonoBehaviour
    {
        private const float CityHeight = 941;
        private Font _font;
        private static RectTransform MakeRect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }
    }
}
