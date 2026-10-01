using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// A small spider that crosses the empty chest's floor once per opening: it comes out
    /// from under the front rim, runs in short bursts with pauses, turns in place and
    /// leaves under the rim again. Six prepared walk frames; the legs step with distance
    /// travelled, not with time, so a pause freezes them. The layer sits below the front
    /// rim artwork, which hides the entry and exit without any fading.
    /// </summary>
    public sealed class ChestSpider : MonoBehaviour, IPointerClickHandler, ICanvasRaycastFilter
    {
        private const int FrameCount = 6;
        private const float BoxSize = 150f;          // frame box, interior units
        private const float FloorSquash = 0.8f;      // the floor is seen at an angle
        private const float Speed = 360f;            // interior units per second while running
        private const float StrideDistance = 8f;     // distance per walk frame
        private const float TurnPerFrame = 14f;      // degrees per walk frame when turning in place
        private const float StartDelay = 0.9f;       // lets the fly go first
        private static readonly Vector2 ShadowOffset = new Vector2(3f, -4f);

        // Route on the floor in interior units, entering and leaving under the front rim.
        // Pauses follow the marked waypoints.
        private static readonly Vector2[] Route =
        {
            new Vector2(-300f, -360f), new Vector2(-285f, -175f), new Vector2(-395f, -25f),
            new Vector2(-320f, 85f), new Vector2(-60f, 100f), new Vector2(230f, 50f),
            new Vector2(355f, -120f), new Vector2(290f, -360f)
        };
        private static readonly bool[] PauseAfter = { false, true, false, true, true, false, true, false };

        private readonly Sprite[] _frames = new Sprite[FrameCount];
        private readonly Sprite[] _shadows = new Sprite[FrameCount];
        private readonly List<Step> _steps = new List<Step>();
        private readonly System.Random _random = new System.Random();
        private Image _body;
        private Image _shadow;
        private int _step;
        private float _stepTime;
        private float _delay;
        private float _walked;
        private float _heading;
        private float _headingAtStepStart;
        private float _lastEased;
        private bool _killed;

        private struct Step
        {
            public bool Pause;
            public Vector2 From, Control, To;
            public float Duration;
            public float TargetHeading; // for pauses: where it faces when the pause ends
        }

        public bool IsRunning => gameObject.activeSelf;

        public static ChestSpider Create(Transform parent)
        {
            var root = new GameObject("Spider running on empty floor", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * BoxSize;
            rect.localScale = new Vector3(1f, FloorSquash, 1f);
            var spider = root.AddComponent<ChestSpider>();
            for (var i = 0; i < FrameCount; i++)
            {
                spider._frames[i] = ChestArt.Load("fx/spider-" + i);
                spider._shadows[i] = ChestArt.Load("fx/spider-shadow-" + i);
            }
            spider._shadow = spider.Layer("Shadow", ShadowOffset);
            spider._body = spider.Layer("Body", Vector2.zero);
            spider._body.raycastTarget = true;
            root.SetActive(false);
            return spider;
        }

        private Image Layer(string name, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * BoxSize;
            rect.anchoredPosition = offset;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        public void Play()
        {
            if (_frames[0] == null) return;
            _killed = false;
            BuildSteps();
            _step = 0;
            _stepTime = 0f;
            _delay = StartDelay;
            _walked = 0f;
            _lastEased = 0f;
            _heading = _headingAtStepStart = Heading(_steps[0]);
            gameObject.SetActive(true);
            Show(_steps[0].From);
        }

        public void Stop()
        {
            gameObject.SetActive(false);
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (_killed || !isActiveAndEnabled || _delay > 0f || _step >= _steps.Count || _body == null)
                return false;
            // The transparent frame corners and the part hidden by the chest's front rim
            // are not targets. Use the rotated sprite coordinates, so the hit follows a turn.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_body.rectTransform,
                screenPoint, eventCamera, out var local)) return false;
            var x = local.x / (BoxSize * 0.29f);
            var y = local.y / (BoxSize * 0.30f);
            if (x * x + y * y > 1f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent,
                screenPoint, eventCamera, out var floor) && floor.y > -235f;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left
                || !IsRaycastLocationValid(eventData.position, eventData.pressEventCamera)) return;
            _killed = true;
            Stop();
            HubMatchReporter.ReportSpiderKilled();
        }

        private void BuildSteps()
        {
            _steps.Clear();
            var mirror = _random.NextDouble() < 0.5 ? -1f : 1f;
            var points = new Vector2[Route.Length];
            for (var i = 0; i < Route.Length; i++)
            {
                var inner = i > 0 && i < Route.Length - 1;
                var jitter = inner ? new Vector2(Range(-30f, 30f), Range(-20f, 20f)) : new Vector2(Range(-40f, 40f), 0f);
                points[i] = new Vector2(Route[i].x * mirror, Route[i].y) + jitter;
            }
            for (var i = 0; i < points.Length - 1; i++)
            {
                var from = points[i];
                var to = points[i + 1];
                var across = new Vector2(-(to - from).y, (to - from).x).normalized;
                var control = (from + to) * 0.5f + across * Range(-45f, 45f);
                var length = ApproximateLength(from, control, to);
                _steps.Add(new Step { From = from, Control = control, To = to, Duration = length / Speed });
                if (PauseAfter[i + 1])
                    _steps.Add(new Step { Pause = true, From = to, To = to, Duration = Range(0.3f, 0.85f) });
            }
            // A pause turns the spider towards the next run while it stands.
            for (var i = 0; i < _steps.Count; i++)
            {
                if (!_steps[i].Pause) continue;
                var step = _steps[i];
                step.TargetHeading = i + 1 < _steps.Count ? Heading(_steps[i + 1]) : _heading;
                _steps[i] = step;
            }
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            if (_delay > 0f)
            {
                _delay -= dt;
                return;
            }
            if (_step >= _steps.Count)
            {
                Stop();
                return;
            }
            var step = _steps[_step];
            _stepTime += dt;
            var t = step.Duration <= 0f ? 1f : Mathf.Clamp01(_stepTime / step.Duration);
            if (step.Pause)
            {
                // Stands still first, then turns in place during the last part of the pause.
                var turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t));
                var heading = Mathf.LerpAngle(_headingAtStepStart, step.TargetHeading, turn);
                _walked += Mathf.Abs(Mathf.DeltaAngle(_heading, heading)) / TurnPerFrame * StrideDistance;
                _heading = heading;
                Show(step.To);
            }
            else
            {
                // Quick start and stop, as spiders move: eased along the curve.
                var eased = t * t * (3f - 2f * t);
                var previous = Bezier(step, _lastEased);
                var position = Bezier(step, eased);
                _walked += (position - previous).magnitude;
                var tangent = BezierTangent(step, eased);
                if (tangent.sqrMagnitude > 1e-4f)
                    _heading = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
                _lastEased = eased;
                Show(position);
            }
            if (t >= 1f)
            {
                _step++;
                _stepTime = 0f;
                _lastEased = 0f;
                _headingAtStepStart = _heading;
            }
        }

        private void Show(Vector2 position)
        {
            var frame = Mathf.FloorToInt(_walked / StrideDistance) % FrameCount;
            ((RectTransform)transform).anchoredPosition = position;
            var rotation = Quaternion.Euler(0f, 0f, _heading);
            _body.sprite = _frames[frame];
            _shadow.sprite = _shadows[frame];
            _body.rectTransform.localRotation = rotation;
            _shadow.rectTransform.localRotation = rotation;
        }

        private static float Heading(Step step)
        {
            var tangent = BezierTangent(step, 0f);
            return Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        }

        private static Vector2 Bezier(Step step, float t)
        {
            var u = 1f - t;
            return u * u * step.From + 2f * u * t * step.Control + t * t * step.To;
        }

        private static Vector2 BezierTangent(Step step, float t) =>
            2f * (1f - t) * (step.Control - step.From) + 2f * t * (step.To - step.Control);

        private static float ApproximateLength(Vector2 from, Vector2 control, Vector2 to)
        {
            var step = new Step { From = from, Control = control, To = to };
            var length = 0f;
            var previous = from;
            for (var i = 1; i <= 12; i++)
            {
                var point = Bezier(step, i / 12f);
                length += (point - previous).magnitude;
                previous = point;
            }
            return length;
        }

        private float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
    }
}
