using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Transparent corners do not consume clicks intended for the dimmed table.</summary>
    public sealed class ChestArtworkHitArea : MonoBehaviour, ICanvasRaycastFilter
    {
        private Vector2[] _outline;
        private RectTransform _rect;

        public static void AddBody(Image image)
        {
            // Normalized outline of the combined rear/front artwork, from bottom left.
            Attach(image, new[]
            {
                new Vector2(0.098f, 0.965f), new Vector2(0.902f, 0.965f),
                new Vector2(0.990f, 0.264f), new Vector2(0.974f, 0.127f),
                new Vector2(0.936f, 0.038f), new Vector2(0.065f, 0.038f),
                new Vector2(0.027f, 0.127f), new Vector2(0.010f, 0.264f)
            });
        }

        public static void AddLid(Image image)
        {
            Attach(image, new[]
            {
                new Vector2(0.140f, 0.995f), new Vector2(0.860f, 0.995f),
                new Vector2(0.995f, 0.570f), new Vector2(0.875f, 0.015f),
                new Vector2(0.125f, 0.015f), new Vector2(0.005f, 0.570f)
            });
        }

        private static void Attach(Image image, Vector2[] outline)
        {
            var hitArea = image.gameObject.AddComponent<ChestArtworkHitArea>();
            hitArea._rect = image.rectTransform;
            hitArea._outline = outline;
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (_rect == null || _outline == null || _outline.Length < 3) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, eventCamera, out var local))
                return false;
            var bounds = _rect.rect;
            if (bounds.width <= 0f || bounds.height <= 0f) return false;
            var point = new Vector2((local.x - bounds.xMin) / bounds.width, (local.y - bounds.yMin) / bounds.height);
            var inside = false;
            for (var i = 0; i < _outline.Length; i++)
            {
                var a = _outline[i];
                var b = _outline[(i + 1) % _outline.Length];
                if ((a.y > point.y) == (b.y > point.y)) continue;
                if (point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }
}
