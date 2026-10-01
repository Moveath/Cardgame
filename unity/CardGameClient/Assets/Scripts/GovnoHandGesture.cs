using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardGameClient
{
    /// <summary>One pointer gesture across the whole hand, including the gaps between cards.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class GovnoHandGesture : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IDragHandler
    {
        public RectTransform[] Cards = Array.Empty<RectTransform>();
        public Action<int> Tap;
        public Action SwipeUp;
        public Func<int> CurrentToken;
        public Func<bool> CanInteract;

        private readonly GovnoGestureState _state = new GovnoGestureState();
        private Camera _pressCamera;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_state.IsCaptured || eventData.button != PointerEventData.InputButton.Left || !InteractionAllowed()) return;
            _pressCamera = eventData.pressEventCamera;
            if (!LocalPoint(eventData.position, out var start)) return;

            var startSlot = -1;
            for (var slot = (Cards == null ? 0 : Cards.Length) - 1; slot >= 0; slot--)
            {
                var card = Cards[slot];
                if (card == null || !card.gameObject.activeInHierarchy
                    || !RectTransformUtility.RectangleContainsScreenPoint(card, eventData.position, _pressCamera)) continue;
                startSlot = slot;
                break;
            }
            _state.Begin(eventData.pointerId, ReadToken(), true, startSlot, start.x, start.y);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // Measure in table coordinates, without the screen-pixel drag threshold.
            eventData.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            TrackMotion(eventData, false);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            TrackMotion(eventData, true);
        }

        /// <summary>Call when pausing or otherwise interrupting a held pointer.</summary>
        public void CancelGesture()
        {
            _state.Cancel();
        }

        private void TrackMotion(PointerEventData eventData, bool released)
        {
            if (!_state.HasPointer(eventData.pointerId)
                || eventData.button != PointerEventData.InputButton.Left) return;
            if (!LocalPoint(eventData.position, out var point))
            {
                CancelGesture();
                return;
            }
            var decision = _state.Track(eventData.pointerId, ReadToken(), InteractionAllowed(),
                point.x, point.y, released);
            // The state clears capture before either callback can change the game phase.
            if (decision.Action == GovnoGestureAction.Tap) Tap?.Invoke(decision.Slot);
            else if (decision.Action == GovnoGestureAction.SwipeUp) SwipeUp?.Invoke();
        }

        private bool InteractionAllowed() => CanInteract != null && CanInteract();
        private int ReadToken() => CurrentToken == null ? 0 : CurrentToken();
        private bool LocalPoint(Vector2 screenPoint, out Vector2 point) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform,
                screenPoint, _pressCamera, out point);

        private void OnDisable() => CancelGesture();
    }
}
