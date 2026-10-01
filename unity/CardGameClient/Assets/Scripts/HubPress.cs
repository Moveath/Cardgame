using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGameClient
{
    /// <summary>Кнопка меню чуть проседает под пальцем — как в мобильных играх.</summary>
    public sealed class HubPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private Vector3 _normal = Vector3.one;
        private bool _down;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_down)
            {
                _normal = transform.localScale;
                _down = true;
            }
            transform.localScale = _normal * 0.95f;
        }

        public void OnPointerUp(PointerEventData eventData) => Release();
        public void OnPointerExit(PointerEventData eventData) => Release();
        private void OnDisable() => Release();

        private void Release()
        {
            if (!_down) return;
            _down = false;
            transform.localScale = _normal;
        }
    }
}
