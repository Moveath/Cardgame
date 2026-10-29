using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Фонтан вплотную (2026-10-24, замысел автора: «не камера приближается, а будто подходишь и встаёшь у бортика»).
    /// Подход без ролика: камера «идёт» по площади — фонтан растёт, как при шаге к нему (размер ~ 1/расстояние: сначала
    /// медленно, у самого бортика быстро), разгон и плавная остановка, лёгкое покачивание шагов и тень по краям, пока
    /// идёшь. На второй половине пути улица перетекает в крупный план `UI/Hub/fountain-close` (исходник
    /// `screens/references/city/fountain-close-v1.png` — перерисовка вида с улицы, `fountain-base.png`): он появляется ровно там, где статуя-трефа на улице, того же
    /// размера и с мягким краем (<see cref="HubWalkFeather"/>), и доезжает до места. «ОТОЙТИ» — тот же путь назад.
    /// Состояния — уличный подход мест (_visit, _visitPhase): «Назад», Esc и вкладки работают как раньше.
    /// Монетка, брызги и бонус — потом.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string FountainId = "fountain", FountainClose = "fountain-close";
        // Секунды подхода и отхода.
        private const float FountainWalkIn = 2.1f, FountainWalkOut = 1.5f;
        // Статуя-трефа: середина и ширина — в пикселях картинки площади (card-city, 1672×941) и крупного плана (16:9;
        // у широкой 21:9 — со сдвигом на половину лишней ширины).
        private static readonly Vector2 FountainStreetStatue = new Vector2(182, 498), FountainCloseStatue = new Vector2(832, 143);
        private const float FountainStreetStatueWidth = 104, FountainCloseStatueWidth = 345, FountainClosePicture = 1672;
        // Трефа на среднем ярусе — вторая точка совмещения: крупный план нарисован чуть ниже и шире, пока картинки
        // сменяются, он растянут по высоте так, чтобы ярус совпал с уличным, и к концу пути распрямляется.
        private const float FountainStreetTileY = 665, FountainCloseTileY = 655;
        // Смена картинок — на этой доле пути, на ходу, чтобы двоение мелькнуло, а не стояло.
        private const float FountainSwapFrom = .6f, FountainSwapTo = .76f;
        // Шагов в секунду и высота покачивания (доля высоты экрана); разгон и остановка — доли пути.
        private const float FountainSteps = 2.1f, FountainBob = .006f, FountainSpeedUp = .22f, FountainSlowDown = .38f;

        private RectTransform _fountainLayer;
        private Image _fountainClose, _fountainShade;
        private HubWalkFeather _fountainFeather;
        private bool _fountainMissing;

        private bool FountainVisit => _visit >= 0 && _visit < CityPlaces.Length && CityPlaces[_visit].Id == FountainId;
        private float VisitInTime => FountainVisit ? FountainWalkIn : VisitIn;
        private float VisitOutTime => FountainVisit ? FountainWalkOut : VisitOut;

        /// <summary>Каждый кадр: у фонтана держит крупный план (и при смене размера окна), ушли — прячет его.</summary>
        private void UpdateFountain()
        {
            var on = FountainVisit && _visitPhase != 0;
            if (_fountainLayer != null && !on && _fountainLayer.gameObject.activeSelf) _fountainLayer.gameObject.SetActive(false);
            if (on && _visitPhase == 2) FrameFountainWalk(1);
        }

        private bool BuildFountainLayer()
        {
            if (_fountainLayer != null) return true;
            if (_fountainMissing || _cityArea == null || _cityRig == null) return false;
            var sprite = Resources.Load<Sprite>("UI/Hub/" + FountainClose);
            if (sprite == null) { _fountainMissing = true; return false; }
            // Над городом, под всем остальным: контур места, вывески и кнопки остаются как были.
            _fountainLayer = MakeRect(_cityArea, "Fountain close-up", 0, 0, 0, 0);
            Stretch(_fountainLayer);
            _fountainLayer.SetSiblingIndex(_cityRig.GetSiblingIndex() + 1);
            _fountainClose = new GameObject("Fountain", typeof(RectTransform)).AddComponent<Image>();
            _fountainClose.rectTransform.SetParent(_fountainLayer, false);
            _fountainClose.rectTransform.anchorMin = _fountainClose.rectTransform.anchorMax = _fountainClose.rectTransform.pivot = new Vector2(.5f, .5f);
            _fountainClose.sprite = sprite;
            _fountainClose.raycastTarget = false;
            HubScenery.Apply(_fountainClose);
            _fountainFeather = _fountainClose.gameObject.AddComponent<HubWalkFeather>();
            _fountainShade = new GameObject("Walk shade", typeof(RectTransform)).AddComponent<Image>();
            _fountainShade.rectTransform.SetParent(_fountainLayer, false);
            _fountainShade.rectTransform.anchorMin = _fountainShade.rectTransform.anchorMax = _fountainShade.rectTransform.pivot = new Vector2(.5f, .5f);
            _fountainShade.sprite = DiveVignette();
            _fountainShade.raycastTarget = false;
            _fountainLayer.gameObject.SetActive(false);
            return true;
        }

        /// <summary>Доля пройденного пути за долю времени <paramref name="t"/>: разгон, ровный шаг, плавная остановка.</summary>
        private static float FountainWalk(float t)
        {
            const float a = FountainSpeedUp, b = FountainSlowDown;
            var total = a / 2 + (1 - a - b) + b / 2;
            t = Mathf.Clamp01(t);
            var s = t < a ? t * t / (2 * a) : t < 1 - b ? a / 2 + t - a : total - (1 - t) * (1 - t) / (2 * b);
            return s / total;
        }

        /// <summary>Скорость шага 0…1 в долю времени <paramref name="t"/> (для покачивания и тени).</summary>
        private static float FountainPace(float t)
            => t < FountainSpeedUp ? t / FountainSpeedUp : t < 1 - FountainSlowDown ? 1 : Mathf.Clamp01((1 - t) / FountainSlowDown);

        /// <summary>Кадр подхода: <paramref name="t"/> 0 — улица, 1 — у бортика (назад — тот же путь, камера пятится).</summary>
        private void FrameFountainWalk(float t)
        {
            var place = CityPlaces[_visit];
            // Пелена входа в здания фонтану не нужна (её включает начало подхода).
            if (_diveShade != null && _diveShade.gameObject.activeSelf) _diveShade.gameObject.SetActive(false);
            if (_diveVignette != null && _diveVignette.gameObject.activeSelf) _diveVignette.gameObject.SetActive(false);
            if (!BuildFountainLayer())
            {
                AimCity(1 + (VisitZoom - 1) * Mathf.SmoothStep(0, 1, t), t, VisitFocus());
                return;
            }
            if (!_fountainLayer.gameObject.activeSelf) _fountainLayer.gameObject.SetActive(true);
            var area = _cityArea.rect.size;
            var sprite = _fountainClose.sprite.rect.size;
            var cover = Mathf.Max(area.x / sprite.x, area.y / sprite.y);
            var size = sprite * cover;
            var unit = cover;
            // Широкий экран режет крупный план сверху и снизу: верх (статуя) остаётся, срезается ближний бортик.
            var rest = new Vector2(0, -(size.y - area.y) / 2);
            var statue = FountainCloseStatue + new Vector2((sprite.x - FountainClosePicture) / 2, 0);
            var statueOffset = new Vector2(statue.x - sprite.x / 2, sprite.y / 2 - statue.y) * unit;
            var final = rest + statueOffset;

            var focus = PlaceScreenPoint(place, FountainStreetStatue);
            var streetWidth = Mathf.Abs(PlaceScreenPoint(place, FountainStreetStatue + new Vector2(FountainStreetStatueWidth, 0)).x - focus.x);
            var grow = Mathf.Clamp(FountainCloseStatueWidth * unit / Mathf.Max(1, streetWidth), 1.5f, 6);
            var p = FountainWalk(t);
            // Видимый размер — обратно расстоянию: шаг к фонтану сначала почти не заметен, у бортика — сильно.
            var growth = 1 / (1 - p * (1 - 1 / grow));
            var turn = Mathf.Lerp(p, (growth - 1) / (grow - 1), .5f);
            var target = Vector2.Lerp(focus, final, turn);

            // Покачивание шагов: голова чуть опускается на каждом шаге и качается из стороны в сторону через шаг.
            var seconds = t * (_visitPhase == 3 ? FountainWalkOut : FountainWalkIn);
            var phase = Mathf.PI * FountainSteps * seconds;
            var pace = FountainPace(t);
            var bob = new Vector2(Mathf.Sin(phase) * .6f, Mathf.Abs(Mathf.Sin(phase)) - .64f) * (FountainBob * area.y * pace);

            var offset = target - focus * growth + bob;
            var reach = CityHeight * CityScale() * growth / 2 - area.y / 2;
            offset.y = Mathf.Clamp(offset.y, -reach, reach);
            _cityRig.localScale = new Vector3(growth, growth, 1);
            _cityRig.anchoredPosition = offset;
            var onScreen = focus * growth + offset;

            // Крупный план — там же, где статуя на улице, и того же размера.
            var k = growth / grow;
            var streetTall = (FountainStreetTileY - FountainStreetStatue.y) / FountainStreetStatueWidth;
            var closeTall = (FountainCloseTileY - FountainCloseStatue.y) / FountainCloseStatueWidth;
            var stretch = Mathf.Lerp(streetTall / closeTall, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(FountainSwapTo, 1, p)));
            var rect = _fountainClose.rectTransform;
            rect.sizeDelta = size;
            rect.localScale = new Vector3(k, k * stretch, 1);
            rect.anchoredPosition = onScreen - new Vector2(statueOffset.x * k, statueOffset.y * k * stretch);
            var color = _fountainClose.color;
            color.a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(FountainSwapFrom, FountainSwapTo, p));
            _fountainClose.color = color;
            _fountainFeather.Feather = Mathf.Lerp(.3f, 0, Mathf.InverseLerp(FountainSwapTo, 1, p));

            // Тень по краям, пока идёшь: взгляд — на фонтан; встал — тень уходит.
            var shade = _fountainShade.rectTransform;
            var span = Mathf.Max(area.x, area.y) * 4.5f;
            shade.sizeDelta = new Vector2(span, span);
            shade.anchoredPosition = onScreen * .4f;
            _fountainShade.color = new Color(DiveDark.r, DiveDark.g, DiveDark.b, .35f * pace);
        }
    }
}
