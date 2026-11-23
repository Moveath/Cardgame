using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CardGameClient.ThreeD
{
    /// <summary>Look and zoom only: the camera stays seated and cannot walk around the room.</summary>
    public sealed class TableCameraController : MonoBehaviour
    {
        // Выше — видны лица соперников, ниже — стол. Ниже 38° в кадр попали бы срезы предплечий: тела у рук пока нет.
        /// <summary>Наклон взгляда при посадке, как в Liar's Bar: стол виден до своего края. В нём заданы поза рук и веер (scripts/3d/hands/in_game.py).</summary>
        internal const float StartPitch = 32f;
        private const float MinPitch = 8f, MaxPitch = 38f, MaxYaw = 36f, MaxZoom = .22f;
        // Взгляд догоняет палец: 90 % пути примерно за треть секунды, рывки пальца не доходят до камеры.
        private const float LookSpeed = 8f, ZoomSpeed = 7f;

        public event Action<Vector2> Tapped;

        private Camera _camera;
        private Vector3 _basePosition;
        private float _yaw;
        private float _pitch = StartPitch;
        private float _targetYaw;
        private float _targetPitch = StartPitch;
        private float _targetZoom;
        private float _zoom;
        private Vector2 _pointerStart;
        private Vector2 _lastPointer;
        private bool _pointerActive;
        private bool _pointerStartedOverUi;
        private bool _dragged;
        private bool _touchActive;
        private int _touchId;
        private Vector2 _touchStart;
        private Vector2 _lastPinchVector;
        private bool _pinchActive;

        public void Configure(Camera camera)
        {
            _camera = camera;
            _basePosition = transform.localPosition;
            _yaw = _targetYaw = transform.localEulerAngles.y;
            _pitch = _targetPitch = StartPitch;
            transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void Update()
        {
            if (_camera == null) return;
            ReadMouse();
            ReadTouch();
            _camera.fieldOfView = Table3DLayout.FieldOfView(_camera.aspect);
            var look = Follow(LookSpeed);
            _yaw = Mathf.Lerp(_yaw, _targetYaw, look);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, look);
            _zoom = Mathf.Lerp(_zoom, _targetZoom, Follow(ZoomSpeed));
            transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.localPosition = _basePosition + transform.forward * _zoom;
        }

        private static float Follow(float speed) =>
            (PlayerHubRuntime.Current?.Settings.ReducedMotion ?? false) ? 1f : 1f - Mathf.Exp(-speed * Time.deltaTime);

        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var position = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _pointerActive = true;
                _pointerStart = _lastPointer = position;
                _dragged = false;
                _pointerStartedOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            }
            else if (_pointerActive && mouse.leftButton.isPressed)
            {
                var delta = position - _lastPointer;
                if ((position - _pointerStart).sqrMagnitude > 64f) _dragged = true;
                if (_dragged && !_pointerStartedOverUi) Look(delta, 0.11f);
                _lastPointer = position;
            }
            else if (_pointerActive && mouse.leftButton.wasReleasedThisFrame)
            {
                if (!_dragged && !_pointerStartedOverUi) Tapped?.Invoke(position);
                _pointerActive = false;
            }
            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) _targetZoom = Mathf.Clamp(_targetZoom + scroll * 0.00022f, 0f, MaxZoom);
        }

        private void ReadTouch()
        {
            var screen = Touchscreen.current;
            if (screen == null) return;
            var primary = screen.primaryTouch;
            var primaryPhase = primary.phase.ReadValue();
            if (_touchActive && primary.touchId.ReadValue() == _touchId
                && (primaryPhase == UnityEngine.InputSystem.TouchPhase.Ended
                    || primaryPhase == UnityEngine.InputSystem.TouchPhase.Canceled))
            {
                if (primaryPhase == UnityEngine.InputSystem.TouchPhase.Ended && !_dragged && !_pointerStartedOverUi)
                    Tapped?.Invoke(primary.position.ReadValue());
                _touchActive = false;
            }
            var touches = screen.touches;
            var active = 0;
            TouchControl first = null;
            TouchControl second = null;
            for (var i = 0; i < touches.Count; i++)
            {
                var phase = touches[i].phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None || phase == UnityEngine.InputSystem.TouchPhase.Ended
                    || phase == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                if (active == 0) first = touches[i]; else if (active == 1) second = touches[i];
                active++;
            }

            if (active >= 2 && first != null && second != null)
            {
                var vector = second.position.ReadValue() - first.position.ReadValue();
                if (_pinchActive)
                {
                    var change = vector.magnitude - _lastPinchVector.magnitude;
                    _targetZoom = Mathf.Clamp(_targetZoom + change * 0.0006f, 0f, MaxZoom);
                }
                _lastPinchVector = vector;
                _pinchActive = true;
                _touchActive = false;
                return;
            }

            _pinchActive = false;
            if (active == 0)
            {
                return;
            }

            var touch = first;
            if (touch == null) return;
            var touchPhase = touch.phase.ReadValue();
            var touchPosition = touch.position.ReadValue();
            if (touchPhase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                _touchActive = true;
                _touchId = touch.touchId.ReadValue();
                _touchStart = _lastPointer = touchPosition;
                _dragged = false;
                _pointerStartedOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(_touchId);
            }
            else if (_touchActive && touch.touchId.ReadValue() == _touchId)
            {
                var delta = touchPosition - _lastPointer;
                if ((touchPosition - _touchStart).sqrMagnitude > 100f) _dragged = true;
                if (_dragged && !_pointerStartedOverUi) Look(delta, 0.16f);
                _lastPointer = touchPosition;
                if (touchPhase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    if (!_dragged && !_pointerStartedOverUi) Tapped?.Invoke(touchPosition);
                    _touchActive = false;
                }
            }
        }

        private void Look(Vector2 delta, float sensitivity)
        {
            _targetYaw = Mathf.Clamp(_targetYaw + delta.x * sensitivity, -MaxYaw, MaxYaw);
            _targetPitch = Mathf.Clamp(_targetPitch - delta.y * sensitivity, MinPitch, MaxPitch);
        }
    }
}
