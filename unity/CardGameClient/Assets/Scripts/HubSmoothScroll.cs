using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Колесо плавно ведёт вертикальный список к цели; перетаскивание остаётся обычным ScrollRect.</summary>
    public sealed class HubSmoothScroll : ScrollRect
    {
        private const float WheelSmoothTime = .16f;
        private bool _wheelMoving, _pointerDragging;
        private float _wheelTarget, _wheelVelocity;

        public override void OnScroll(PointerEventData data)
        {
            if (!IsActive() || _pointerDragging) return;
            if (!vertical || horizontal) { base.OnScroll(data); return; }
            Canvas.ForceUpdateCanvases();
            var delta = -data.scrollDelta.y;
            if (Mathf.Abs(data.scrollDelta.x) > Mathf.Abs(delta)) delta = data.scrollDelta.x;
            if (Mathf.Abs(delta) < .001f) return;

            var current = content.anchoredPosition.y;
            // При смене направления сразу слушаем колесо, не доезжаем сначала к старой цели.
            if (!_wheelMoving || delta * (_wheelTarget - current) < 0)
            {
                _wheelTarget = current;
                _wheelVelocity = 0;
            }
            base.StopMovement();
            _wheelTarget = Mathf.Clamp(_wheelTarget + delta * scrollSensitivity, 0, WheelLimit());
            _wheelMoving = true;
        }

        private float WheelLimit() => Mathf.Max(0, content.rect.height -
            (viewport != null ? viewport.rect.height : ((RectTransform)transform).rect.height));

        protected override void LateUpdate()
        {
            AdvanceWheel(Time.unscaledDeltaTime);
            // ScrollRect обновляет полосу прокрутки и onValueChanged, а при перетаскивании — свою инерцию.
            base.LateUpdate();
        }

        private void AdvanceWheel(float deltaTime)
        {
            if (!_wheelMoving || content == null || deltaTime <= 0) return;
            base.StopMovement();
            _wheelTarget = Mathf.Clamp(_wheelTarget, 0, WheelLimit());
            var position = content.anchoredPosition;
            position.y = Mathf.SmoothDamp(position.y, _wheelTarget, ref _wheelVelocity,
                WheelSmoothTime, Mathf.Infinity, deltaTime);
            if (Mathf.Abs(position.y - _wheelTarget) < .1f && Mathf.Abs(_wheelVelocity) < 1)
            {
                position.y = _wheelTarget;
                _wheelMoving = false;
                _wheelVelocity = 0;
            }
            SetContentAnchoredPosition(position);
        }

        public override void StopMovement()
        {
            _wheelMoving = false;
            _wheelVelocity = 0;
            base.StopMovement();
        }

        public override void OnInitializePotentialDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) StopMovement();
            base.OnInitializePotentialDrag(data);
        }

        public override void OnBeginDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left && IsActive())
            {
                StopMovement();
                _pointerDragging = true;
            }
            base.OnBeginDrag(data);
        }

        public override void OnEndDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) _pointerDragging = false;
            base.OnEndDrag(data);
        }

        protected override void OnDisable()
        {
            StopMovement();
            _pointerDragging = false;
            base.OnDisable();
        }
    }
}
