using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGameClient
{
    /// <summary>
    /// Прозрачный слой между городом и меню: перетаскивание по пустому месту листает районы,
    /// нажатие без перетаскивания — по месту в городе (здание, лестница, замок).
    /// Кнопки перетаскивание не ловят, поэтому свайп, начатый на кнопке, город не двигает.
    /// <see cref="Hovered"/> — указатель над городом, а не над кнопкой поверх него: только тогда
    /// место под мышью подсвечивается.
    /// </summary>
    public sealed class HubCitySwipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        public Action<PointerEventData> Began, Moved, Ended, Clicked;
        public bool Hovered { get; private set; }

        public void OnBeginDrag(PointerEventData eventData) => Began?.Invoke(eventData);
        public void OnDrag(PointerEventData eventData) => Moved?.Invoke(eventData);
        public void OnEndDrag(PointerEventData eventData) => Ended?.Invoke(eventData);
        // Модуль ввода шлёт «клик» и после перетаскивания, если его начали на этом же слое, —
        // такие отсеивает получатель по eventData.dragging.
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(eventData);
        public void OnPointerEnter(PointerEventData eventData) => Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => Hovered = false;
        private void OnDisable() => Hovered = false;
    }
}
