using System;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>The Queen's daily spread. The service owns the card and its remaining matches.</summary>
    public sealed partial class PlayerHubController
    {
        private static readonly Color FortuneGold = new Color(1, .85f, .48f);
        private static readonly Color FortuneCream = new Color(1, .95f, .85f);
        private static readonly Color FortuneLilac = new Color(.83f, .68f, 1);
        private RectTransform _fortuneLayer, _fortuneTent, _fortuneTable, _fortuneReading, _fortuneReadingPanel;
        private RectTransform _fortuneReadingViewport, _fortuneReadingContent, _fortuneBadgeLayer, _fortuneTopShade, _fortuneBottomShade;
        private readonly Image[] _fortuneCards = new Image[6];
        private readonly Image[] _fortuneCardGlows = new Image[6];
        private readonly Image[] _fortuneCardShadows = new Image[6];
        private readonly HubTableCardPerspective[] _fortuneCardPerspectives = new HubTableCardPerspective[6];
        private readonly Button[] _fortuneCardButtons = new Button[6];
        private readonly Vector2[] _fortuneCardPositions = new Vector2[6];
        private Button _fortuneBackButton, _fortuneReadLastButton, _fortuneReadingBackButton, _fortuneGamesButton, _fortuneBadgeButton, _fortuneTestButton;
        private Image _fortuneFace, _fortuneBadgeArt;
        private Text _fortuneHeading, _fortuneInstruction, _fortuneStatusText, _fortuneReadingHeader;
        private Text _fortuneReadingTitle, _fortuneQuoteText, _fortuneEffectText, _fortuneRemainingText;
        private Text _fortuneScopeText, _fortunePokerText, _fortuneCooldownText, _fortuneBadgeTitle, _fortuneBadgeDetail;
        private HubSmoothScroll _fortuneReadingScroll;
        private HubDragScrollbar _fortuneReadingScrollbar;
        private bool _fortuneReadingShown, _fortuneReadRequested, _fortuneEscapeHeld, _fortuneNarrow;
        private float _fortuneFlipTime = -1;
        private string _fortuneLayoutKey = "";
        private long FortuneNow => PlayerHubRuntime.Clock.UnixSeconds;
        // Плашка действующего предсказания в правом верхнем углу города — автор убрал (2026-10-09), может вернуть позже:
        // тогда true. Перечитать предсказание можно в шатре кнопкой «МОЯ КАРТА».
        private static readonly bool FortuneCityBadge = false;
        private bool FortuneShown => _page == "fortune" && _fortuneLayer != null && _fortuneLayer.gameObject.activeSelf;
        private bool FortuneWorking => _busy || _fortuneFlipTime >= 0;
        private bool FortuneReduced => _state != null && _state.Settings.ReducedMotion;
        private float FortunePixelsPerUnit => Mathf.Max(.1f, _stage.localScale.x * (_menuCanvas != null ? _menuCanvas.scaleFactor : 1));
        private Vector2 FortuneViewport => ((RectTransform)transform).rect.size / Mathf.Max(.01f, _stage.localScale.x);

        // The tent was redrawn "camera further back" (scripts/city/zoom_out.py): the approved picture is this part of it,
        // in shares of the new one from the top-left. The table and its cards are measured in the approved picture.
        private static readonly Rect FortuneOldArt = new Rect(318f / 2540, 138f / 1102, 1904f / 2540, 826f / 1102);

        // Shared with the entrance floor, so the doorway reveal ends on the same image.
        // Landscape fills the screen like the other interiors; controls sit on shaded bands over the art.
        private static Rect FortuneBackdropRect(Vector2 viewport, float aspect)
        {
            var portrait = viewport.x < viewport.y * 1.25f;
            var height = portrait ? viewport.x / aspect : Mathf.Max(viewport.y, viewport.x / aspect);
            var size = new Vector2(height * aspect, height);
            var center = portrait ? new Vector2(0, (viewport.y - height) / 2) : Vector2.zero;
            return new Rect(center - size / 2, size);
        }

        private void BuildFortunePage()
        {
            if (_fortuneLayer == null) BuildFortuneScene();
            var entering = !_fortuneLayer.gameObject.activeSelf;
            _fortuneLayer.gameObject.SetActive(true);
            _fortuneLayer.SetAsLastSibling();
            if (entering)
            {
                _fortuneFlipTime = -1;
                _fortuneReadingShown = HubFortunes.Find(_state.Fortune.EffectId) != null
                    && (_fortuneReadRequested || !HubFortunes.CanDraw(_state.Fortune, FortuneNow));
                _fortuneEscapeHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
                _fortuneLayoutKey = "";
                _fortuneReadingScroll.StopMovement();
                _fortuneReadingScroll.verticalNormalizedPosition = 1;
            }
            _fortuneReadRequested = false;
            Title("Шатёр гадалки", "Пиковая дама");
            PaintFortune(0);
        }

        private void OpenFortuneReadingRoute()
        {
            if (_busy || GiftShown || QuestsShown || BankShown || _mapOpen || _diveTarget >= 0 || _visitPhase == 1 || _visitPhase == 3) return;
            _fortuneReadRequested = true;
            Navigate("fortune");
        }

        private bool TryFortuneBack()
        {
            if (_page != "fortune") return false;
            if (_fortuneReadingShown) { CloseFortuneReading(); return true; }
            // The result is already saved before animation. Leaving never rolls another card.
            _fortuneFlipTime = -1;
            return false;
        }

        private void ShowFortuneReading()
        {
            if (_page != "fortune" || _state == null || HubFortunes.Find(_state.Fortune.EffectId) == null || _busy) return;
            _fortuneFlipTime = -1;
            _fortuneReadingShown = true;
            _fortuneLayoutKey = "";
            _fortuneReadingScroll.StopMovement();
            _fortuneReadingScroll.verticalNormalizedPosition = 1;
            PaintFortune(0);
        }

        private void CloseFortuneReading()
        {
            if (_busy) return;
            _fortuneReadingShown = false;
            _fortuneReadingScroll.StopMovement();
            PaintFortune(0);
        }

        private async Task ChooseFortuneCardAsync(int index)
        {
            if (!FortuneShown || FortuneWorking || index < 0 || index >= _fortuneCardButtons.Length) return;
            if (!HubFortunes.CanDraw(_state.Fortune, FortuneNow))
            {
                if (_state.Fortune.SelectedCardIndex == index) ShowFortuneReading();
                return;
            }
            if (PlayerHubRuntime.HasPendingResult)
            { Toast("Сначала сохрани результат предыдущей партии."); return; }
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "fortune_draw", Amount = index });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); PaintFortune(0); return; }
            _state = result.Snapshot;
            _fortuneReadingShown = FortuneReduced;
            _fortuneFlipTime = FortuneReduced ? -1 : 0;
            _fortuneLayoutKey = "";
            _fortuneReadingScroll.StopMovement();
            _fortuneReadingScroll.verticalNormalizedPosition = 1;
            Render();
        }

        private void UpdateFortune(float deltaTime, bool typing)
        {
            RefreshFortunePresentation();
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (FortuneShown && escape && !_fortuneEscapeHeld) Back();
            _fortuneEscapeHeld = escape;
            if (!FortuneShown) return;
            if (_fortuneFlipTime >= 0)
            {
                _fortuneFlipTime += deltaTime;
                if (_fortuneFlipTime >= .95f || FortuneReduced) ShowFortuneReading();
            }
            PaintFortune(deltaTime);
        }

        private void RefreshFortunePresentation()
        {
            if (_fortuneLayer != null && _page != "fortune")
            {
                _fortuneLayer.gameObject.SetActive(false);
                _fortuneReadingShown = false;
                _fortuneFlipTime = -1;
            }
            if (FortuneShown && _toastPill != null && _toastPill.gameObject.activeSelf) _toastPill.transform.SetAsLastSibling();
            var active = FortuneCityBadge && _state != null && HubFortunes.Active(_state.Fortune);
            if (active && _fortuneBadgeLayer == null) BuildFortuneBadge();
            if (_fortuneBadgeLayer == null) return;
            var visible = active && _page == "home" && !GiftShown && !QuestsShown && !BankShown && !_mapOpen
                && _diveTarget < 0 && _visitPhase == 0;
            if (visible && !_fortuneBadgeLayer.gameObject.activeSelf) _fortuneBadgeLayer.SetAsLastSibling();
            _fortuneBadgeLayer.gameObject.SetActive(visible);
            if (!visible) return;
            var viewport = FortuneViewport;
            var definition = HubFortunes.Find(_state.Fortune.EffectId);
            _fortuneBadgeArt.sprite = Resources.Load<Sprite>(definition.Resource);
            _fortuneBadgeTitle.text = definition.Name;
            _fortuneBadgeDetail.text = "Осталось: " + _state.Fortune.RemainingMatches + " · читать";
            _fortuneBadgeTitle.fontSize = Mathf.CeilToInt(Mathf.Max(31, 16 / FortunePixelsPerUnit));
            _fortuneBadgeDetail.fontSize = Mathf.CeilToInt(Mathf.Max(26, 14 / FortunePixelsPerUnit));
            var titleHeight = Mathf.Ceil(_fortuneBadgeTitle.preferredHeight) + 6;
            var detailHeight = Mathf.Ceil(_fortuneBadgeDetail.preferredHeight) + 6;
            var badgeHeight = Mathf.Max(120, titleHeight + detailHeight + 26);
            _fortuneBadgeTitle.rectTransform.sizeDelta = new Vector2(338, titleHeight);
            _fortuneBadgeDetail.rectTransform.sizeDelta = new Vector2(338, detailHeight);
            _fortuneBadgeTitle.rectTransform.anchoredPosition = new Vector2(34, (detailHeight + 8) / 2);
            _fortuneBadgeDetail.rectTransform.anchoredPosition = new Vector2(34, -(titleHeight + 8) / 2);
            var badgeRect = _fortuneBadgeButton.GetComponent<RectTransform>();
            badgeRect.sizeDelta = new Vector2(460, badgeHeight);
            badgeRect.anchoredPosition = new Vector2(viewport.x / 2 - 256, viewport.y / 2 - 24 - badgeHeight / 2);
            _fortuneBadgeButton.interactable = !_busy;
        }

        private void PaintFortune(float deltaTime)
        {
            if (_fortuneLayer == null || _state == null || _page != "fortune") return;
            var viewport = FortuneViewport;
            var pixels = FortunePixelsPerUnit;
            var portrait = viewport.x < viewport.y * 1.25f;
            _fortuneNarrow = portrait || viewport.x * pixels < 800;
            var tentSprite = _fortuneTent.GetComponent<Image>().sprite;
            var aspect = tentSprite != null ? tentSprite.rect.width / tentSprite.rect.height : 21f / 9;
            var backdrop = FortuneBackdropRect(viewport, aspect);
            var table = new Rect(backdrop.xMin + FortuneOldArt.x * backdrop.width,
                backdrop.yMax - FortuneOldArt.yMax * backdrop.height, FortuneOldArt.width * backdrop.width, FortuneOldArt.height * backdrop.height);
            _fortuneTent.sizeDelta = backdrop.size;
            _fortuneTent.anchoredPosition = backdrop.center;
            var controlHeight = Mathf.Max(68, 46 / pixels);
            var buttonFont = Mathf.CeilToInt(Mathf.Max(30, 19 / pixels));
            var top = viewport.y / 2;
            var left = -viewport.x / 2;
            _fortuneTopShade.gameObject.SetActive(!portrait);
            _fortuneTopShade.sizeDelta = new Vector2(viewport.x + 4, controlHeight + 90);
            _fortuneTopShade.anchoredPosition = new Vector2(0, top - (controlHeight + 90) / 2);
            FortunePlaceButton(_fortuneBackButton, new Vector2(left + 124, top - 28 - controlHeight / 2), new Vector2(196, controlHeight), buttonFont);
            FortunePlaceButton(_fortuneReadLastButton, new Vector2(-left - 167, top - 28 - controlHeight / 2), new Vector2(282, controlHeight), buttonFont);
            _fortuneHeading.rectTransform.anchoredPosition = new Vector2(0, top - 63);
            _fortuneHeading.rectTransform.sizeDelta = new Vector2(Mathf.Min(840, viewport.x - 570), 78);
            _fortuneInstruction.fontSize = Mathf.CeilToInt(Mathf.Max(32, 18 / pixels));
            var canDraw = HubFortunes.CanDraw(_state.Fortune, FortuneNow);
            var definition = HubFortunes.Find(_state.Fortune.EffectId);
            var hasCard = definition != null;
            var selected = Mathf.Clamp(_state.Fortune.SelectedCardIndex, 0, 5);
            var activeMatch = !string.IsNullOrEmpty(_state.ActiveMatchId) || PlayerHubRuntime.HasPendingResult;
            _fortuneInstruction.text = _fortuneFlipTime >= 0 ? "Судьба уже сделала свой выбор…"
                : canDraw ? "Выбери одну карту. Остальное решит судьба." : "Твоё предсказание уже открыто.";
            _fortuneStatusText.text = activeMatch && canDraw ? "Сначала заверши текущую партию."
                : canDraw && HubFortunes.Active(_state.Fortune) ? "Бесплатно · новая карта заменит действующее предсказание"
                : canDraw ? "БЕСПЛАТНО · ОДНА КАРТА КАЖДЫЕ 24 ЧАСА"
                : "Следующая карта через " + FortuneRemaining(Math.Max(0, _state.Fortune.ReadyAt - FortuneNow));
            // The spread follows the curved front edge of the actual velvet table in the artwork.
            // These are projected cards: the far edge recedes and the short contact shadow stays on the cloth.
            var cardWidth = table.width * .099f;
            var flatCardHeight = cardWidth * 1.5f * .57f;
            var spacing = table.width * .12f;
            for (var i = 0; i < _fortuneCards.Length; i++)
            {
                var across = (i - 2.5f) / 2.5f;
                var sourceY = .815f - .055f * across * across;
                var x = table.center.x + (i - 2.5f) * spacing;
                var y = table.center.y + (.5f - sourceY) * table.height;
                _fortuneCardPositions[i] = new Vector2(x, y);
                var card = _fortuneCards[i];
                var flipping = _fortuneFlipTime >= 0 && i == selected;
                var lift = flipping ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(_fortuneFlipTime / .2f)) : 0;
                var cardHeight = Mathf.Lerp(flatCardHeight, cardWidth * 1.5f, lift);
                card.rectTransform.sizeDelta = new Vector2(cardWidth, cardHeight);
                card.rectTransform.anchoredPosition = _fortuneCardPositions[i];
                card.rectTransform.localScale = Vector3.one;
                card.rectTransform.localRotation = Quaternion.Euler(0, 0, -across * 7 * (1 - lift));
                _fortuneCardPerspectives[i].SetProjection(Mathf.Lerp(.22f, 0, lift), Mathf.Lerp(.70f, 1, lift));
                var showFace = hasCard && i == selected && (!canDraw || flipping) && (!flipping || _fortuneFlipTime >= .42f);
                card.sprite = Resources.Load<Sprite>(showFace ? definition.Resource : "UI/Hub/fortune-card-back");
                card.color = _fortuneFlipTime >= 0 && !flipping ? new Color(.72f, .65f, .79f, .70f) : Color.white;
                _fortuneCardButtons[i].interactable = !FortuneWorking && !activeMatch && (canDraw || hasCard && i == selected);
                var contact = _fortuneCardShadows[i];
                contact.rectTransform.anchoredPosition = _fortuneCardPositions[i] + Vector2.down * (5 + 6 * lift);
                contact.rectTransform.sizeDelta = new Vector2(cardWidth * 1.16f, flatCardHeight * .72f);
                contact.rectTransform.localRotation = Quaternion.Euler(0, 0, -across * 7);
                contact.color = new Color(.008f, .002f, .015f, Mathf.Lerp(.88f, .45f, lift));
                _fortuneCardGlows[i].gameObject.SetActive((hasCard && !canDraw && i == selected) || flipping);
                _fortuneCardGlows[i].rectTransform.sizeDelta = new Vector2(cardWidth + 30, flatCardHeight * .7f + 24);
                _fortuneCardGlows[i].rectTransform.anchoredPosition = _fortuneCardPositions[i];
                _fortuneCardGlows[i].rectTransform.localRotation = Quaternion.Euler(0, 0, -across * 7);
                if (flipping)
                {
                    var flip = Mathf.Clamp01((_fortuneFlipTime - .2f) / .44f);
                    var turn = Mathf.Max(.025f, Mathf.Abs(Mathf.Cos(flip * Mathf.PI)));
                    card.rectTransform.anchoredPosition += Vector2.up * (60 * lift);
                    card.rectTransform.localScale = new Vector3(turn * 1.08f, 1.08f, 1);
                    card.transform.SetAsLastSibling();
                }
            }
            _fortuneStatusText.fontSize = Mathf.CeilToInt(Mathf.Max(29, 17 / pixels));
            _fortuneInstruction.rectTransform.sizeDelta = new Vector2(viewport.x - 90, 80);
            _fortuneStatusText.rectTransform.sizeDelta = new Vector2(viewport.x - 90, 80);
            var instructionHeight = Mathf.Ceil(_fortuneInstruction.preferredHeight) + 6;
            var statusHeight = Mathf.Ceil(_fortuneStatusText.preferredHeight) + 6;
            _fortuneInstruction.rectTransform.sizeDelta = new Vector2(viewport.x - 90, instructionHeight);
            _fortuneStatusText.rectTransform.sizeDelta = new Vector2(viewport.x - 90, statusHeight);
            var captionTop = Mathf.Max(backdrop.yMin - 7, -viewport.y / 2 + 6 + instructionHeight + 1 + statusHeight);
            _fortuneInstruction.rectTransform.anchoredPosition = new Vector2(0, captionTop - instructionHeight / 2);
            _fortuneStatusText.rectTransform.anchoredPosition = new Vector2(0, captionTop - instructionHeight - 1 - statusHeight / 2);
            var captionShade = captionTop + viewport.y / 2 + 40;
            _fortuneBottomShade.gameObject.SetActive(!portrait);
            _fortuneBottomShade.sizeDelta = new Vector2(viewport.x + 4, captionShade);
            _fortuneBottomShade.anchoredPosition = new Vector2(0, -viewport.y / 2 + captionShade / 2);
            _fortuneReadLastButton.gameObject.SetActive(hasCard && !_fortuneReadingShown && _fortuneFlipTime < 0);
            var testing = (Application.isEditor || Debug.isDebugBuild) && !canDraw && !_fortuneReadingShown && _fortuneFlipTime < 0;
            _fortuneTestButton.gameObject.SetActive(testing);
            if (testing) FortunePlaceButton(_fortuneTestButton, new Vector2(-left - 24 - 150, -top + 18 + 25), new Vector2(300, 50),
                Mathf.CeilToInt(Mathf.Max(24, 14 / pixels)));
            _fortuneTestButton.interactable = !FortuneWorking;
            _fortuneReadLastButton.interactable = !FortuneWorking;
            _fortuneBackButton.interactable = !_busy;
            _fortuneReading.gameObject.SetActive(_fortuneReadingShown && hasCard);
            if (_fortuneReadingShown && hasCard)
            {
                _fortuneFace.sprite = Resources.Load<Sprite>(definition.Resource);
                _fortuneReadingTitle.text = definition.Name;
                _fortuneQuoteText.text = "«" + definition.Quote + "»";
                _fortuneEffectText.text = HubFortunes.EffectText(_state.Fortune);
                _fortuneRemainingText.text = HubFortunes.Active(_state.Fortune) ? "Осталось партий: " + _state.Fortune.RemainingMatches
                    : _state.Fortune.Triggered ? "ПРЕДСКАЗАНИЕ СБЫЛОСЬ" : "ДЕЙСТВИЕ ЗАВЕРШЕНО";
                _fortuneRemainingText.color = HubFortunes.Active(_state.Fortune) ? FortuneGold : FortuneLilac;
                _fortuneCooldownText.text = canDraw ? "Новый бесплатный расклад уже доступен у стола."
                    : "Новый бесплатный расклад через " + FortuneRemaining(Math.Max(0, _state.Fortune.ReadyAt - FortuneNow));
                _fortuneGamesButton.GetComponentInChildren<Text>().text = _state.Fortune.EffectId == "new-road" && HubFortunes.Active(_state.Fortune) ? "К НОВОЙ ИГРЕ" : "К ИГРАМ";
                var layoutKey = viewport + "|" + pixels + "|" + _fortuneNarrow + "|" + _state.Fortune.EffectId + "|" + _fortuneRemainingText.text + "|" + _fortuneEffectText.text + "|" + canDraw;
                if (_fortuneLayoutKey != layoutKey)
                {
                    LayoutFortuneReading(viewport, pixels, controlHeight, buttonFont);
                    _fortuneLayoutKey = layoutKey;
                }
                _fortuneReadingBackButton.interactable = _fortuneGamesButton.interactable = !_busy;
            }
            _fortuneBackButton.transform.SetAsLastSibling();
        }

        private static string FortuneRemaining(long seconds)
            => seconds >= 86400 ? seconds / 86400 + " д " + seconds % 86400 / 3600 + " ч"
                : (seconds / 3600).ToString("D2") + ":" + (seconds % 3600 / 60).ToString("D2") + ":" + (seconds % 60).ToString("D2");

        private void GoFromFortuneToGames()
        {
            if (_busy) return;
            var fortune = _state.Fortune;
            Navigate(fortune.EffectId == "new-road" && HubFortunes.Active(fortune) ? "game:" + fortune.TargetGame : "games");
        }

        private float FortuneReadingRow(Text label, float x, float y, float width, int fontSize)
        {
            label.fontSize = fontSize;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, 80);
            var height = Mathf.Ceil(label.preferredHeight) + 8;
            rect.sizeDelta = new Vector2(width, height);
            return y + height + 20;
        }

        private void LayoutFortuneReading(Vector2 viewport, float pixels, float controlHeight, int buttonFont)
        {
            var panelWidth = Mathf.Min(1470, viewport.x - 48);
            var panelHeight = viewport.y - controlHeight - 62;
            _fortuneReadingPanel.sizeDelta = new Vector2(panelWidth, panelHeight);
            _fortuneReadingPanel.anchoredPosition = new Vector2(0, -(controlHeight + 10) / 2);
            _fortuneReadingHeader.fontSize = Mathf.CeilToInt(Mathf.Max(31, 16 / pixels));
            _fortuneReadingHeader.rectTransform.sizeDelta = new Vector2(panelWidth - 92, 80);
            var headerHeight = Mathf.Max(62, Mathf.Ceil(_fortuneReadingHeader.preferredHeight) + 8);
            _fortuneReadingHeader.rectTransform.sizeDelta = new Vector2(panelWidth - 92, headerHeight);
            _fortuneReadingHeader.rectTransform.anchoredPosition = new Vector2(0, panelHeight / 2 - 24 - headerHeight / 2);
            _fortuneCooldownText.fontSize = Mathf.CeilToInt(Mathf.Max(29, 17 / pixels));
            _fortuneCooldownText.rectTransform.sizeDelta = new Vector2(panelWidth - 80, 80);
            var cooldownHeight = Mathf.Max(54, Mathf.Ceil(_fortuneCooldownText.preferredHeight) + 8);
            _fortuneCooldownText.rectTransform.sizeDelta = new Vector2(panelWidth - 80, cooldownHeight);
            // The measured timer sits above the buttons; the scroll viewport ends above it.
            var cooldownBottom = -panelHeight / 2 + 24 + controlHeight + 14;
            _fortuneCooldownText.rectTransform.anchoredPosition = new Vector2(0, cooldownBottom + cooldownHeight / 2);
            var bodyTop = panelHeight / 2 - 24 - headerHeight - 18;
            var bodyBottom = cooldownBottom + cooldownHeight + 18;
            var bodyWidth = panelWidth - 80;
            var viewportHeight = Mathf.Max(120, bodyTop - bodyBottom);
            _fortuneReadingViewport.anchoredPosition = new Vector2(0, (bodyTop + bodyBottom) / 2);
            _fortuneReadingViewport.sizeDelta = new Vector2(bodyWidth, viewportHeight);
            var contentWidth = bodyWidth - 36;
            _fortuneReadingContent.sizeDelta = new Vector2(contentWidth, viewportHeight);
            var faceHeight = _fortuneNarrow ? Mathf.Clamp(viewportHeight * .68f, 300, 580) : Mathf.Min(585, viewportHeight - 20);
            var faceWidth = faceHeight * 2 / 3;
            var faceRect = _fortuneFace.rectTransform;
            faceRect.anchorMin = faceRect.anchorMax = faceRect.pivot = new Vector2(0, 1);
            faceRect.sizeDelta = new Vector2(faceWidth, faceHeight);
            var textX = _fortuneNarrow ? 20 : faceWidth + 72;
            var textWidth = contentWidth - textX - 20;
            faceRect.anchoredPosition = new Vector2(_fortuneNarrow ? (contentWidth - faceWidth) / 2 : 14, -8);
            var y = _fortuneNarrow ? faceHeight + 32 : 6;
            var titleSize = Mathf.CeilToInt(Mathf.Max(_fortuneNarrow ? 48 : 44, 22 / pixels));
            var bodySize = Mathf.CeilToInt(Mathf.Max(_fortuneNarrow ? 44 : 36, 20 / pixels));
            var quoteSize = Mathf.CeilToInt(Mathf.Max(_fortuneNarrow ? 34 : 30, 17 / pixels));
            var scopeSize = Mathf.CeilToInt(Mathf.Max(_fortuneNarrow ? 32 : 27, 16 / pixels));
            y = FortuneReadingRow(_fortuneReadingTitle, textX, y, textWidth, titleSize);
            y = FortuneReadingRow(_fortuneEffectText, textX, y, textWidth, bodySize);
            y = FortuneReadingRow(_fortuneRemainingText, textX, y, textWidth, Mathf.Max(bodySize - 2, 34));
            y = FortuneReadingRow(_fortuneQuoteText, textX, y, textWidth, quoteSize);
            y = FortuneReadingRow(_fortuneScopeText, textX, y, textWidth, scopeSize);
            y = FortuneReadingRow(_fortunePokerText, textX, y, textWidth, scopeSize);
            _fortuneReadingContent.sizeDelta = new Vector2(contentWidth, Mathf.Max(viewportHeight, y + 8, faceHeight + 20));
            _fortuneReadingScrollbar.GetComponent<RectTransform>().sizeDelta = new Vector2(44, viewportHeight);
            _fortuneReadingScrollbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(bodyWidth / 2 - 16, 0);
            _fortuneReadingScrollbar.gameObject.SetActive(_fortuneReadingContent.rect.height > viewportHeight + 2);
            var buttonY = -panelHeight / 2 + 24 + controlHeight / 2;
            var buttonWidth = Mathf.Min(415, (panelWidth - 124) / 2);
            FortunePlaceButton(_fortuneReadingBackButton, new Vector2(-panelWidth / 4, buttonY), new Vector2(buttonWidth, controlHeight), buttonFont);
            FortunePlaceButton(_fortuneGamesButton, new Vector2(panelWidth / 4, buttonY), new Vector2(buttonWidth, controlHeight), buttonFont);
            _fortuneReadingScroll.StopMovement();
            _fortuneReadingScroll.verticalNormalizedPosition = 1;
        }

        private Text FortuneText(Transform parent, string name, string value, Vector2 at, Vector2 size, int fontSize, Color color, bool body = false)
        {
            var label = LoanText(parent, name, value, at, size, fontSize, color);
            if (body) { label.font = _font; label.lineSpacing = 1.08f; }
            return label;
        }

        private Button FortuneButton(Transform parent, string name, string text, Vector2 at, Vector2 size, Action click)
        {
            var image = LoanArt(parent, name, null, at, size, new Color(.14f, .045f, .205f, .97f));
            image.sprite = UiTextures.RoundedRect(96, 64, 14, Color.white);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            var outline = image.gameObject.AddComponent<HubOutline>();
            outline.effectColor = new Color(.84f, .63f, .26f, .95f);
            outline.effectDistance = new Vector2(2, -2);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.93f, .85f, 1);
            colors.pressedColor = new Color(.65f, .49f, .78f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
            FortuneText(image.transform, "Caption", text, Vector2.zero, size - new Vector2(20, 8), 31, FortuneGold);
            return button;
        }

        private static void FortunePlaceButton(Button button, Vector2 at, Vector2 size, int fontSize)
        {
            var rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            var text = button.GetComponentInChildren<Text>();
            text.rectTransform.sizeDelta = size - new Vector2(18, 6);
            text.fontSize = fontSize;
        }

        private void BuildFortuneScene()
        {
            _fortuneLayer = StageLayer("Fortune teller tent");
            var baseColor = LoanArt(_fortuneLayer, "Fortune backdrop", null, Vector2.zero, new Vector2(6000, 6000), new Color(.035f, .007f, .06f));
            baseColor.raycastTarget = true;
            _fortuneTent = LoanArt(_fortuneLayer, "Approved dark fortune tent", "fortune-tent", Vector2.zero, new Vector2(2074, 900), Color.black).rectTransform;
            var night = new Color(.035f, .007f, .06f);
            var topShade = LoanArt(_fortuneLayer, "Fortune top shade", null, Vector2.zero, new Vector2(1600, 160), Color.white);
            topShade.sprite = VerticalGradient(new Color(night.r, night.g, night.b, .82f), new Color(night.r, night.g, night.b, 0));
            _fortuneTopShade = topShade.rectTransform;
            var bottomShade = LoanArt(_fortuneLayer, "Fortune caption shade", null, Vector2.zero, new Vector2(1600, 160), Color.white);
            bottomShade.sprite = VerticalGradient(new Color(night.r, night.g, night.b, 0), new Color(night.r, night.g, night.b, .86f));
            _fortuneBottomShade = bottomShade.rectTransform;
            _fortuneHeading = LoanText(_fortuneLayer, "Fortune page title", "РАСКЛАД ДНЯ", new Vector2(0, 390), new Vector2(840, 76), 43, FortuneGold, true);
            var titleShadow = _fortuneHeading.gameObject.AddComponent<Shadow>(); titleShadow.effectColor = new Color(.02f, 0, .035f, .95f); titleShadow.effectDistance = new Vector2(2, -3);
            _fortuneInstruction = FortuneText(_fortuneLayer, "Choose one fortune instruction", "", new Vector2(0, 320), new Vector2(1360, 70), 32, FortuneCream);
            var hintShadow = _fortuneInstruction.gameObject.AddComponent<Shadow>(); hintShadow.effectColor = Color.black; hintShadow.effectDistance = new Vector2(2, -2);
            _fortuneTable = LoanRect(_fortuneLayer, "Six fortune cards on table", Vector2.zero, new Vector2(1600, 900));
            for (var i = 0; i < _fortuneCards.Length; i++)
            {
                var contact = LoanArt(_fortuneTable, "Fortune card contact shadow " + i, null, Vector2.zero, new Vector2(186, 140), Color.black);
                contact.sprite = UiTextures.RadialGlow(128, Color.white);
                _fortuneCardShadows[i] = contact;
                var glow = LoanArt(_fortuneTable, "Selected fortune glow " + i, null, Vector2.zero, new Vector2(232, 324), new Color(.62f, .36f, 1, .55f));
                glow.sprite = UiTextures.RadialGlow(128, Color.white);
                _fortuneCardGlows[i] = glow;
                var card = LoanArt(_fortuneTable, "Fortune choice " + i, "fortune-card-back", Vector2.zero, new Vector2(186, 278), Color.white);
                card.preserveAspect = false;
                card.raycastTarget = true;
                _fortuneCardPerspectives[i] = card.gameObject.AddComponent<HubTableCardPerspective>();
                var shadow = card.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(.02f, .005f, .035f, .8f); shadow.effectDistance = new Vector2(1.5f, -3);
                var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = card;
                var colors = button.colors; colors.highlightedColor = new Color(1, .96f, .85f); colors.pressedColor = new Color(.8f, .65f, .9f); button.colors = colors;
                var index = i; button.onClick.AddListener(() => _ = ChooseFortuneCardAsync(index));
                _fortuneCards[i] = card; _fortuneCardButtons[i] = button;
            }
            _fortuneStatusText = FortuneText(_fortuneLayer, "Fortune draw availability", "", new Vector2(0, -404), new Vector2(1460, 70), 29, FortuneCream);
            var statusShadow = _fortuneStatusText.gameObject.AddComponent<Shadow>(); statusShadow.effectColor = Color.black; statusShadow.effectDistance = new Vector2(2, -2);
            // ТЕСТ (2026-10-09, по просьбе автора): новая карта сразу, без 24 часов — только в редакторе и отладочной сборке.
            // Убрать вместе с командой `fortune_debug_ready` в сервисе.
            _fortuneTestButton = FortuneButton(_fortuneLayer, "Fortune test new card", "Тест: новая карта", Vector2.zero, new Vector2(300, 50),
                () => { if (!FortuneWorking) Send("fortune_debug_ready"); });
            BuildFortuneReading();
            _fortuneBackButton = FortuneButton(_fortuneLayer, "Fortune exit", "НАЗАД", new Vector2(-675, 388), new Vector2(196, 68), Back);
            _fortuneReadLastButton = FortuneButton(_fortuneLayer, "Read saved fortune", "МОЯ КАРТА", new Vector2(634, 388), new Vector2(282, 68), ShowFortuneReading);
            _fortuneLayer.gameObject.SetActive(false);
        }

        private void BuildFortuneReading()
        {
            _fortuneReading = LoanRect(_fortuneLayer, "Fortune reading", Vector2.zero, new Vector2(1600, 900));
            var dim = LoanArt(_fortuneReading, "Fortune reading shade", null, Vector2.zero, new Vector2(6000, 6000), new Color(.015f, .003f, .03f, .75f));
            dim.raycastTarget = true;
            var panel = LoanArt(_fortuneReading, "Fortune reading panel", null, new Vector2(0, -35), new Vector2(1470, 770), new Color(.07f, .025f, .12f, .98f));
            panel.sprite = UiTextures.RoundedRect(128, 128, 22, Color.white); panel.type = Image.Type.Sliced; panel.raycastTarget = true;
            var edge = panel.gameObject.AddComponent<HubOutline>(); edge.effectColor = new Color(.72f, .51f, .25f); edge.effectDistance = new Vector2(2, -2);
            _fortuneReadingPanel = panel.rectTransform;
            _fortuneReadingHeader = FortuneText(_fortuneReadingPanel, "Fortune reading heading", "ПРЕДСКАЗАНИЕ ПИКОВОЙ ДАМЫ", new Vector2(0, 327), new Vector2(1360, 62), 31, FortuneLilac);
            var viewport = LoanArt(_fortuneReadingPanel, "Fortune reading viewport", null, new Vector2(0, 25), new Vector2(1360, 600), Color.clear);
            viewport.raycastTarget = true;
            _fortuneReadingViewport = viewport.rectTransform;
            _fortuneReadingViewport.gameObject.AddComponent<RectMask2D>();
            _fortuneReadingContent = LoanRect(_fortuneReadingViewport, "Fortune reading content", Vector2.zero, new Vector2(1324, 600));
            _fortuneReadingContent.anchorMin = _fortuneReadingContent.anchorMax = _fortuneReadingContent.pivot = new Vector2(0, 1);
            _fortuneReadingContent.anchoredPosition = Vector2.zero;
            _fortuneFace = LoanArt(_fortuneReadingContent, "Selected fortune face", null, Vector2.zero, new Vector2(360, 540), Color.white);
            _fortuneFace.preserveAspect = true;
            _fortuneReadingTitle = FortuneText(_fortuneReadingContent, "Saved fortune name", "", Vector2.zero, new Vector2(800, 70), 44, FortuneGold);
            _fortuneReadingTitle.font = Resources.Load<Font>("Fonts/SignDisplay") ?? _font;
            _fortuneQuoteText = FortuneText(_fortuneReadingContent, "Queen fortune quote", "", Vector2.zero, new Vector2(800, 100), 30, FortuneLilac, true);
            _fortuneQuoteText.fontStyle = FontStyle.Italic;
            _fortuneEffectText = FortuneText(_fortuneReadingContent, "Exact fortune effect", "", Vector2.zero, new Vector2(800, 160), 36, FortuneCream, true);
            _fortuneRemainingText = FortuneText(_fortuneReadingContent, "Fortune remaining matches", "", Vector2.zero, new Vector2(800, 70), 34, FortuneGold, true);
            _fortuneRemainingText.fontStyle = FontStyle.Bold;
            _fortuneScopeText = FortuneText(_fortuneReadingContent, "Fortune reward scope", "Только обычные награды за завершённые партии.\nСтавки, подарки и награды заданий не меняются.", Vector2.zero, new Vector2(800, 100), 27, new Color(.83f, .79f, .89f), true);
            _fortunePokerText = FortuneText(_fortuneReadingContent, "Fortune poker hand rule", "В покере учитывается каждая завершённая раздача.", Vector2.zero, new Vector2(800, 70), 27, new Color(.83f, .79f, .89f), true);
            foreach (var label in new[] { _fortuneReadingTitle, _fortuneQuoteText, _fortuneEffectText, _fortuneRemainingText, _fortuneScopeText, _fortunePokerText }) label.alignment = TextAnchor.UpperLeft;
            _fortuneReadingScroll = _fortuneReadingViewport.gameObject.AddComponent<HubSmoothScroll>();
            _fortuneReadingScroll.viewport = _fortuneReadingViewport; _fortuneReadingScroll.content = _fortuneReadingContent;
            _fortuneReadingScroll.horizontal = false; _fortuneReadingScroll.vertical = true;
            _fortuneReadingScroll.movementType = ScrollRect.MovementType.Clamped;
            _fortuneReadingScroll.inertia = true; _fortuneReadingScroll.decelerationRate = .10f;
            _fortuneReadingScroll.scrollSensitivity = 40; _fortuneReadingScroll.WheelSmoothTime = .38f;
            var track = LoanArt(_fortuneReadingViewport, "Fortune reading scrollbar", null, new Vector2(662, 0), new Vector2(44, 600), Color.clear);
            track.raycastTarget = true;
            var line = LoanArt(track.transform, "Scrollbar rail", null, Vector2.zero, new Vector2(5, 590), new Color(.64f, .49f, .83f, .25f));
            line.rectTransform.anchorMin = new Vector2(.5f, 0); line.rectTransform.anchorMax = new Vector2(.5f, 1); line.rectTransform.sizeDelta = new Vector2(5, -8);
            var handle = LoanArt(track.transform, "Fortune scrollbar handle", null, Vector2.zero, Vector2.zero, Color.clear);
            handle.raycastTarget = true;
            var grip = LoanArt(handle.transform, "Fortune scrollbar grip", null, Vector2.zero, new Vector2(15, -4), FortuneLilac);
            grip.sprite = UiTextures.RoundedRect(32, 32, 9, Color.white); grip.type = Image.Type.Sliced;
            grip.rectTransform.anchorMin = new Vector2(.5f, 0); grip.rectTransform.anchorMax = new Vector2(.5f, 1);
            _fortuneReadingScrollbar = track.gameObject.AddComponent<HubDragScrollbar>();
            _fortuneReadingScrollbar.direction = Scrollbar.Direction.BottomToTop; _fortuneReadingScrollbar.handleRect = handle.rectTransform;
            _fortuneReadingScrollbar.targetGraphic = grip; _fortuneReadingScrollbar.ScrollView = _fortuneReadingScroll;
            _fortuneReadingScroll.verticalScrollbar = _fortuneReadingScrollbar;
            _fortuneCooldownText = FortuneText(_fortuneReadingPanel, "Fortune cooldown", "", new Vector2(0, -255), new Vector2(1300, 65), 29, FortuneLilac);
            _fortuneReadingBackButton = FortuneButton(_fortuneReadingPanel, "Back to fortune table", "К КАРТАМ", new Vector2(-360, -332), new Vector2(400, 68), CloseFortuneReading);
            _fortuneGamesButton = FortuneButton(_fortuneReadingPanel, "Play after fortune", "К ИГРАМ", new Vector2(360, -332), new Vector2(400, 68), GoFromFortuneToGames);
            _fortuneReading.gameObject.SetActive(false);
        }

        private void BuildFortuneBadge()
        {
            _fortuneBadgeLayer = StageLayer("Active fortune badge layer");
            _fortuneBadgeButton = FortuneButton(_fortuneBadgeLayer, "Read active prediction", "", new Vector2(564, 373), new Vector2(460, 120), OpenFortuneReadingRoute);
            var image = LoanArt(_fortuneBadgeButton.transform, "Active fortune miniature", null, new Vector2(-186, 0), new Vector2(70, 105), Color.white);
            image.preserveAspect = true; _fortuneBadgeArt = image;
            _fortuneBadgeTitle = FortuneText(_fortuneBadgeButton.transform, "Active fortune name", "", new Vector2(34, 25), new Vector2(338, 58), 31, FortuneGold);
            _fortuneBadgeDetail = FortuneText(_fortuneBadgeButton.transform, "Active fortune counter", "", new Vector2(34, -29), new Vector2(338, 49), 26, FortuneCream);
            _fortuneBadgeLayer.gameObject.SetActive(false);
        }
    }
}
