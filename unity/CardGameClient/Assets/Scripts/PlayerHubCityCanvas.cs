using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Город — свой холст (2026-10-10, оптимизация для телефона). Лента улицы и все залы (`City camera`) — вложенный
    /// Canvas: их движение пересобирает только его, а не всё меню. Под непрозрачным окном — почта и друзья таверны,
    /// стол гадалки, стол Тузяры — город с затемнением не рисуется вовсе: его всё равно не видно, а видеокарта телефона
    /// тратила на него половину кадра. Пока окно проявляется, город виден, как раньше. Нажатия по городу ловит
    /// `City swipe` рядом с лентой, поэтому своему холсту города обработчик нажатий не нужен.
    /// У таблички стола затемнение зала — рамкой вокруг плотной середины доски, а не под ней целиком (50 → 60 кадров);
    /// под краями доски и вокруг она та же. Прозрачное затемнение (шатёр гадалки) не рисуется вовсе.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private Canvas _cityCanvas;
        // Затемнение зала у таблички стола — ещё три полосы рядом с _cityDim: вместе рамка вокруг середины доски.
        private Image[] _cityDimRing;
        private Rect _cityDimHole;
        // Насколько середина доски отступает от её краёв (скругления и рамка доски полупрозрачны).
        private const float PlaqueSolidInset = 32;

        private void BuildCityCanvas()
        {
            if (_cityRig == null || _cityCanvas != null) return;
            _cityCanvas = _cityRig.gameObject.AddComponent<Canvas>();
            // Небо под городом на телефоне не рисуется: город закрывает экран всегда (QA — CheckCityCovers), а слой
            // во весь экран видеокарте телефона дорог. На ПК — как было.
            var sky = _cityArea.GetComponent<Image>();
            if (sky != null && Application.isMobilePlatform) sky.enabled = false;
            if (_cityDim != null) _cityDim.canvasRenderer.cullTransparentMesh = true;
        }

        /// <summary>На странице стола — затемнение рамкой вокруг плотной середины таблички; иначе — во весь экран.</summary>
        private void FitCityDim()
        {
            if (_cityDim == null || _stage == null) return;
            var dim = _cityDim.rectTransform;
            var hole = IsTablePage(_page) && _cityDim.enabled && _cityDim.color.a > 0;
            if (_cityDimRing != null)
                foreach (var part in _cityDimRing)
                    if (part.enabled != hole) part.enabled = hole;
            if (!hole)
            {
                if (_cityDimHole.width > 0) { Stretch(dim); _cityDimHole = Rect.zero; }
                return;
            }
            if (_cityDimRing == null)
            {
                _cityDimRing = new Image[3];
                for (var i = 0; i < 3; i++)
                {
                    var part = MakeRect(_cityArea, "City dim ring " + i, 0, 0, 0, 0).gameObject.AddComponent<Image>();
                    part.raycastTarget = false;
                    part.canvasRenderer.cullTransparentMesh = true;
                    part.transform.SetSiblingIndex(dim.GetSiblingIndex() + 1);
                    _cityDimRing[i] = part;
                }
            }
            // Середина доски (координаты сцены 1600×900, y вниз) → координаты фона.
            const float left = 1600 - PlaqueMargin - PlaqueWidth;
            Vector2 Local(float x, float y) => _cityArea.InverseTransformPoint(_stage.TransformPoint(new Vector3(x - 800, 450 - y)));
            var low = Local(left + PlaqueSolidInset, PlaqueTop + 820 - PlaqueSolidInset);
            var high = Local(left + TablePanelWidth - PlaqueSolidInset, PlaqueTop + PlaqueSolidInset);
            var area = _cityArea.rect;
            low = Vector2.Max(low, area.min);
            high = Vector2.Min(high, area.max);
            foreach (var part in _cityDimRing) part.color = _cityDim.color;
            var key = Rect.MinMaxRect(low.x, low.y, Mathf.Max(low.x + 1, high.x), high.y);
            if (key == _cityDimHole) return;
            _cityDimHole = key;
            void Place(RectTransform rect, Vector2 from, Vector2 to)
            {
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.offsetMin = from - area.min;
                rect.offsetMax = Vector2.Max(from, to) - area.min;
            }
            Place(dim, area.min, new Vector2(low.x, area.yMax));
            Place(_cityDimRing[0].rectTransform, new Vector2(high.x, area.yMin), area.max);
            Place(_cityDimRing[1].rectTransform, new Vector2(low.x, high.y), new Vector2(high.x, area.yMax));
            Place(_cityDimRing[2].rectTransform, new Vector2(low.x, area.yMin), new Vector2(high.x, low.y));
        }

        /// <summary>Город закрыт целиком: окно поверх уже непрозрачно.</summary>
        private bool CityCovered => MailShown && _mailGroup.alpha >= .999f || FriendsShown && _friendsGroup.alpha >= .999f
            || FortuneShown || BankShown && _bankLoanGroup != null && _bankLoanGroup.alpha >= .999f;

        /// <summary>Каждый кадр: город рисуется, только когда его видно.</summary>
        private void UpdateCityCanvas()
        {
            if (_cityCanvas == null) return;
            var draw = !CityCovered;
            FitCityDim();
            if (_cityCanvas.enabled == draw) return;
            _cityCanvas.enabled = draw;
            // Затемнение поверх города — тоже во весь экран; небо под ним на телефоне выключено совсем.
            if (_cityDim != null) _cityDim.enabled = draw;
            var sky = _cityArea != null ? _cityArea.GetComponent<Image>() : null;
            if (sky != null && !Application.isMobilePlatform) sky.enabled = draw;
        }
    }
}
