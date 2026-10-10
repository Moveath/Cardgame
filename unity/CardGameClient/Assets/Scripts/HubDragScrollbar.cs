using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>A scrollbar takes control from a pending smooth wheel movement until released.</summary>
    public sealed class HubDragScrollbar : Scrollbar
    {
        public HubSmoothScroll ScrollView { get; set; }

        public override void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
                ScrollView?.BeginScrollbarDrag();
            base.OnPointerDown(data);
        }

        public override void OnBeginDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
                ScrollView?.BeginScrollbarDrag();
            base.OnBeginDrag(data);
        }

        public override void OnPointerUp(PointerEventData data)
        {
            base.OnPointerUp(data);
            if (data.button == PointerEventData.InputButton.Left) ScrollView?.EndScrollbarDrag();
        }

        protected override void OnDisable()
        {
            ScrollView?.EndScrollbarDrag();
            base.OnDisable();
        }
    }
}
