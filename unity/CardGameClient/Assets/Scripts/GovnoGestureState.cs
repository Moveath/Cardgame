using System;

namespace CardGameClient
{
    internal enum GovnoGestureAction { None, Tap, SwipeUp }

    internal readonly struct GovnoGestureDecision
    {
        public GovnoGestureAction Action { get; }
        public int Slot { get; }

        public GovnoGestureDecision(GovnoGestureAction action, int slot = -1)
        {
            Action = action;
            Slot = slot;
        }
    }

    /// <summary>Pointer decisions in logical hand coordinates, independent of the Unity event system.</summary>
    internal sealed class GovnoGestureState
    {
        private const float TapDistance = 14f;
        private const float SwipeDistance = 60f;
        private const float UpwardBias = 1.2f;

        private bool _canTap;
        private int _pointerId, _token, _startSlot;
        private float _startX, _startY;

        public bool IsCaptured { get; private set; }
        public bool HasPointer(int pointerId) => IsCaptured && _pointerId == pointerId;

        public bool Begin(int pointerId, int token, bool canInteract, int slot, float x, float y)
        {
            if (IsCaptured || !canInteract) return false;
            IsCaptured = _canTap = true;
            _pointerId = pointerId;
            _token = token;
            _startSlot = slot;
            _startX = x;
            _startY = y;
            return true;
        }

        public GovnoGestureDecision Track(int pointerId, int token, bool canInteract,
            float x, float y, bool released)
        {
            if (!HasPointer(pointerId)) return default;
            if (!canInteract || token != _token)
            {
                Cancel();
                return default;
            }

            var dx = x - _startX;
            var dy = y - _startY;
            var upward = dy > Math.Abs(dx) * UpwardBias;
            if (dx * dx + dy * dy > TapDistance * TapDistance)
            {
                // A drag cannot turn back into a card tap when the pointer returns.
                _canTap = false;
                if (!upward)
                {
                    Cancel();
                    return default;
                }
            }
            if (upward && dy >= SwipeDistance)
            {
                Cancel();
                return new GovnoGestureDecision(GovnoGestureAction.SwipeUp);
            }
            if (!released) return default;

            var decision = _canTap && _startSlot >= 0
                ? new GovnoGestureDecision(GovnoGestureAction.Tap, _startSlot) : default;
            Cancel();
            return decision;
        }

        public void Cancel()
        {
            IsCaptured = _canTap = false;
            _startSlot = -1;
        }
    }
}
