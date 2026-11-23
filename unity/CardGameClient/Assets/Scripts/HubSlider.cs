using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGameClient
{
    /// <summary>
    /// Ползунок для пальца и мыши (2026-11-01, громкость в окне настроек): нажатие ставит значение сразу, ведение
    /// двигает его, отпускание сообщает итог. Доля 0..1 считается по <see cref="Track"/> между отступами
    /// <see cref="Inset"/> (там, где жёлоб без закруглений); сам компонент можно повесить на область крупнее жёлоба.
    /// </summary>
    public sealed class HubSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Track;
        public float Inset;
        public Action<float> Moved, Released;
        private bool _held;

        public void OnPointerDown(PointerEventData eventData) { _held = true; Move(eventData); }

        public void OnDrag(PointerEventData eventData) { if (_held) Move(eventData); }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_held) return;
            _held = false;
            Released?.Invoke(Share(eventData));
        }

        private void Move(PointerEventData eventData) => Moved?.Invoke(Share(eventData));

        private float Share(PointerEventData eventData)
        {
            var track = Track != null ? Track : (RectTransform)transform;
            var camera = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera;
            if (camera == null)
            {
                // Событие без модуля (проверки) — камера холста, если он не поверх экрана.
                var canvas = GetComponentInParent<Canvas>();
                if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) camera = canvas.rootCanvas.worldCamera;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position, camera, out var local);
            return Mathf.InverseLerp(track.rect.xMin + Inset, track.rect.xMax - Inset, local.x);
        }
    }
}
