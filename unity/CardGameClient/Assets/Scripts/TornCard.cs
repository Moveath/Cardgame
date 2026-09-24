using System.Collections;
using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Карта, которую рвут пополам по вертикали. Собрана из двух половинок с рваным краем:
    /// в покое они складываются в целую карту, а при разрыве раскрываются от низа, как
    /// будто карту тянут за верхние углы в разные стороны, и потом разлетаются.
    /// </summary>
    public sealed class TornCard : MonoBehaviour
    {
        // Темп средний: сам разрыв должен читаться, но и ждать его не хочется.
        private const float StrainDuration = 0.3f;
        private const float RipDuration = 0.45f;
        private const float FallDuration = 0.7f;

        /// <summary>Сколько идёт разрыв одной карты от натяга до исчезновения.</summary>
        public const float TearDuration = StrainDuration + RipDuration + FallDuration;

        /// <summary>На сколько градусов половинки раскрываются, пока ещё держатся внизу.</summary>
        private const float RipAngle = 7f;

        private RectTransform _left;
        private RectTransform _right;
        private CanvasGroup _leftGroup;
        private CanvasGroup _rightGroup;
        private Image _leftFibers;
        private Image _rightFibers;
        private float _scale;

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

            torn._left = torn.BuildHalf(font, card, variant, true, out torn._leftGroup, out torn._leftFibers);
            torn._right = torn.BuildHalf(font, card, variant, false, out torn._rightGroup, out torn._rightFibers);
            return torn;
        }

        private RectTransform BuildHalf(Font font, Card card, int variant, bool left, out CanvasGroup group, out Image fibers)
        {
            var go = new GameObject(left ? "Left" : "Right",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(CanvasGroup));
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

            group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;

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
        /// проступают белые волокна. Конец — нижняя перемычка рвётся, и половинки
        /// разлетаются в стороны и падают, растворяясь.
        /// </summary>
        public IEnumerator Tear()
        {
            var rest = Rect.anchoredPosition;

            var t = 0f;
            while (t < StrainDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.Clamp01(t / StrainDuration);
                var tremble = Mathf.Sin(t * 75f) * 1.6f * _scale * k;
                Rect.anchoredPosition = rest + new Vector2(tremble, 0f);
                Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 53f) * 0.8f * k);
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
                SetHalves(2f * _scale * k, 0f, RipAngle * k, 1f);
                SetFibers(Mathf.Clamp01(linear / 0.4f));
                yield return null;
            }

            t = 0f;
            while (t < FallDuration)
            {
                t += Time.deltaTime;
                var linear = Mathf.Clamp01(t / FallDuration);
                var spread = Mathf.SmoothStep(0f, 1f, linear);
                var fall = linear * linear;
                var fade = 1f - Mathf.Clamp01((linear - 0.35f) / 0.65f);
                SetHalves((2f + 46f * spread) * _scale, -150f * _scale * fall, RipAngle + 24f * spread, fade);
                yield return null;
            }

            gameObject.SetActive(false);
        }

        private void SetHalves(float apart, float drop, float angle, float alpha)
        {
            var pivotY = -CardView.Height * _scale * 0.5f + drop;
            _left.anchoredPosition = new Vector2(-apart, pivotY);
            _right.anchoredPosition = new Vector2(apart, pivotY);
            _left.localRotation = Quaternion.Euler(0f, 0f, angle);
            _right.localRotation = Quaternion.Euler(0f, 0f, -angle);
            _leftGroup.alpha = alpha;
            _rightGroup.alpha = alpha;
        }

        private void SetFibers(float alpha)
        {
            var color = new Color(1f, 1f, 1f, alpha);
            _leftFibers.color = color;
            _rightFibers.color = color;
        }
    }
}
