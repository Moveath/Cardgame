using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGameClient
{
    /// <summary>
    /// Место соперника, которое можно выбрать тапом или кликом. Под курсором загорается
    /// обводка, уходит курсор — гаснет, если место не выбрано. Состояние живёт в самом
    /// месте, а не в столе: стол перестраивает места целиком, и новое место само поймает
    /// курсор, если тот над ним.
    /// </summary>
    public sealed class SeatTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private GameObject _outline;
        private Action _onClick;
        private bool _pinned;

        /// <param name="onClick">Null — место сейчас не выбирается, и наведение ничего не подсвечивает.</param>
        public void Configure(GameObject outline, bool pinned, Action onClick)
        {
            _outline = outline;
            _pinned = pinned;
            _onClick = onClick;

            if (_outline != null)
            {
                _outline.SetActive(pinned);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_onClick != null && _outline != null)
            {
                _outline.SetActive(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_outline != null)
            {
                _outline.SetActive(_pinned);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _onClick?.Invoke();
        }
    }
}
