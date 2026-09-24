using System.Collections;
using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Карта, которую рвут пополам по вертикали. Собрана из двух половинок с рваным краем:
    /// в покое они складываются в целую карту, а при разрыве раскрываются от низа, как
    /// будто карту тянут за верхние углы в разные стороны. Порванная так и остаётся лежать
    /// двумя обрывками — концовка партии не исчезает, её видно до новой раздачи.
    /// </summary>
    public sealed class TornCard : MonoBehaviour
    {
        // Темп спокойный: разрыв смакуют, а не проскакивают.
        private const float StrainDuration = 0.45f;
        private const float RipDuration = 0.75f;
        private const float SettleDuration = 0.8f;

        /// <summary>Сколько идёт разрыв одной карты: от натяга до обрывков, легших на стол.</summary>
        public const float TearDuration = StrainDuration + RipDuration + SettleDuration;

        /// <summary>На сколько градусов половинки раскрываются, пока ещё держатся внизу.</summary>
        private const float RipAngle = 7f;

        /// <summary>
        /// Насколько обрывки расходятся в конце, в единицах карты, — по этому числу стол
        /// оставляет место между тузами, чтобы соседние обрывки не наезжали друг на друга.
        /// </summary>
        public const float SettledSpread = 12f;

        /// <summary>Насколько ниже опускается нижний из двух обрывков, в единицах карты.</summary>
        public const float SettledDrop = 26f;

        private RectTransform _left;
        private RectTransform _right;
        private Image _leftFibers;
        private Image _rightFibers;
        private float _scale;
        private int _variant;

        public RectTransform Rect { get; private set; }

        public static TornCard Create(Transform parent, Font font, Card card, float scale, int variant)
        {
            var go = new GameObject("TornCard", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var torn = go.AddComponent<TornCard>();
            torn.Rect = (RectTransform)go.transform;
            torn.Rect.anchorMin = torn.Rect.anchorMax = torn.Rect.pivot = new Vector2(0.5f, 0.5f);
            torn.Rect.sizeDelta = new Vector2(CardView.Width * scale, CardView.Height * scale);
            torn._scale = scale;
            torn._variant = variant;

            torn._left = torn.BuildHalf(font, card, variant, true, out torn._leftFibers);
            torn._right = torn.BuildHalf(font, card, variant, false, out torn._rightFibers);
            return torn;
        }

        private RectTransform BuildHalf(Font font, Card card, int variant, bool left, out Image fibers)
        {
            var go = new GameObject(left ? "Left" : "Right",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            go.transform.SetParent(Rect, false);

            var margin = UiTextures.TearMargin;
            var fullWidth = CardView.Width + 2f * margin;
            var fullHeight = CardView.Height + 2f * margin;

            // Половинка — это карта целиком плюс поля под тень, а видно от неё только
            // свою сторону. Поворачивается она вокруг низа карты посередине: там разрыв
            // заканчивается, и половинки раскрываются от этой точки.
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, margin / fullHeight);
            rect.sizeDelta = new Vector2(fullWidth * _scale, fullHeight * _scale);
            rect.anchoredPosition = new Vector2(0f, -CardView.Height * _scale * 0.5f);

            var mask = go.GetComponent<Image>();
            mask.sprite = UiTextures.TearMask(variant, left);
            mask.raycastTarget = false;
            go.GetComponent<Mask>().showMaskGraphic = false;

            var face = CardView.Create(rect, font, _scale);
            face.ShowFace(card);
            face.SetPlain();

            var fibersGo = new GameObject("Fibers", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fibersGo.transform.SetParent(rect, false);
            var fibersRect = (RectTransform)fibersGo.transform;
            fibersRect.anchorMin = Vector2.zero;
            fibersRect.anchorMax = Vector2.one;
            fibersRect.offsetMin = Vector2.zero;
            fibersRect.offsetMax = Vector2.zero;

            fibers = fibersGo.GetComponent<Image>();
            fibers.sprite = UiTextures.TearFibers(variant);
            fibers.raycastTarget = false;
            fibers.color = new Color(1f, 1f, 1f, 0f);

            return rect;
        }

        /// <summary>Перелёт целой карты к месту, где её порвут: от игрока, мелкой — к центру, крупной.</summary>
        public IEnumerator FlyIn(Vector2 from, Vector2 to, float duration, float delay)
        {
            const float startScale = 0.45f;
            var startRotation = from.x <= to.x ? -14f : 14f;

            Rect.anchoredPosition = from;
            Rect.localScale = new Vector3(startScale, startScale, 1f);
            Rect.localRotation = Quaternion.Euler(0f, 0f, startRotation);

            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                Rect.anchoredPosition = Vector2.Lerp(from, to, k);
                var scale = Mathf.Lerp(startScale, 1f, k);
                Rect.localScale = new Vector3(scale, scale, 1f);
                Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRotation, 0f, k));
                yield return null;
            }

            Rect.anchoredPosition = to;
            Rect.localScale = Vector3.one;
            Rect.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Разрыв в три такта. Натяг — карта дрожит целиком, её тянут, она ещё держится.
        /// Разрыв — половинки раскрываются от низа, щель бежит сверху вниз, по краю
        /// проступают белые волокна. Конец — нижняя перемычка рвётся, и обрывки ложатся
        /// чуть в стороны и чуть вниз, один ниже другого, как упавшие, — и так и остаются.
        /// </summary>
        public IEnumerator Tear()
        {
            var rest = Rect.anchoredPosition;

            var t = 0f;
            while (t < StrainDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.Clamp01(t / StrainDuration);
                var tremble = Mathf.Sin(t * 60f) * 1.6f * _scale * k;
                Rect.anchoredPosition = rest + new Vector2(tremble, 0f);
                Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 43f) * 0.8f * k);
                yield return null;
            }

            Rect.anchoredPosition = rest;
            Rect.localRotation = Quaternion.identity;

            t = 0f;
            while (t < RipDuration)
            {
                t += Time.deltaTime;
                var linear = Mathf.Clamp01(t / RipDuration);
                var k = Mathf.SmoothStep(0f, 1f, linear);
                var apart = 2f * _scale * k;
                SetHalves(apart, 0f, RipAngle * k, apart, 0f, RipAngle * k);
                SetFibers(Mathf.Clamp01(linear / 0.4f));
                yield return null;
            }

            // Какой обрывок ляжет ниже — от варианта разрыва: у соседних тузов по-разному,
            // иначе ряд выглядел бы отпечатанным под копирку.
            var lowLeft = _variant % 2 == 0;
            var leftDrop = (lowLeft ? SettledDrop : SettledDrop * 0.45f) * _scale;
            var rightDrop = (lowLeft ? SettledDrop * 0.45f : SettledDrop) * _scale;
            var leftAngle = lowLeft ? 10f : 5f;
            var rightAngle = lowLeft ? 5f : 10f;

            t = 0f;
            while (t < SettleDuration)
            {
                t += Time.deltaTime;
                var k = EaseOutBack(Mathf.Clamp01(t / SettleDuration));
                var apart = Mathf.LerpUnclamped(2f, SettledSpread, k) * _scale;
                SetHalves(apart, -leftDrop * k, Mathf.LerpUnclamped(RipAngle, leftAngle, k),
                    apart, -rightDrop * k, Mathf.LerpUnclamped(RipAngle, rightAngle, k));
                yield return null;
            }

            SetHalves(SettledSpread * _scale, -leftDrop, leftAngle, SettledSpread * _scale, -rightDrop, rightAngle);
        }

        /// <summary>Мягкая посадка с небольшим перелётом: обрывок падает и чуть оседает.</summary>
        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.2f;
            var u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        private void SetHalves(float leftApart, float leftDrop, float leftAngle,
            float rightApart, float rightDrop, float rightAngle)
        {
            var pivotY = -CardView.Height * _scale * 0.5f;
            _left.anchoredPosition = new Vector2(-leftApart, pivotY + leftDrop);
            _right.anchoredPosition = new Vector2(rightApart, pivotY + rightDrop);
            _left.localRotation = Quaternion.Euler(0f, 0f, leftAngle);
            _right.localRotation = Quaternion.Euler(0f, 0f, -rightAngle);
        }

        private void SetFibers(float alpha)
        {
            var color = new Color(1f, 1f, 1f, alpha);
            _leftFibers.color = color;
            _rightFibers.color = color;
        }
    }
}
