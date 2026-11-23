using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    // Presentation and touch controls for the club. DurakGame remains the authority for moves.
    public sealed partial class DurakTableController
    {
        private static readonly Color ClubGold = new Color(.79f, .55f, .25f);
        private static readonly Color ClubCream = new Color(1f, .91f, .73f);
        private static readonly Color ClubInk = new Color(.12f, .065f, .055f);
        private readonly Dictionary<Card, CardView> _clubHand = new Dictionary<Card, CardView>();
        private readonly List<Button> _clubSocialButtons = new List<Button>();
        private Text _clubSocialHint;
        private RectTransform _clubDiscard, _clubDiscardCards, _clubPopup, _clubBubble;
        private RectTransform _clubRoom, _clubTable, _clubTableShadow, _clubStatusPlate;
        private Sprite _clubCardBack;
        private Text _clubCoins, _clubDiscardText;
        private Vector2? _clubLaunch;
        private float _clubReactionAt = -10f;
        private float _clubLineUntil;
        private bool _clubReactionsMuted;
        private Vector2 _clubSize;
        private Rect _clubSafeArea;
        private int _clubDiscardCount;
        private bool ClubReduced => PlayerHubRuntime.Current?.Settings.ReducedMotion ?? false;

        private Image ClubShape(Transform parent, string name, Vector2 anchor, Vector2 at, Vector2 size, Color color, bool circle = false)
        {
            var rect = CreatePanel(name, parent, anchor, at, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = circle ? UiTextures.Circle(128, Color.white) : UiTextures.RoundedRect(64, 64, 18, Color.white);
            image.type = circle ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image ClubArt(Transform parent, string key, Vector2 anchor, Vector2 at, Vector2 size)
        {
            var image = ClubShape(parent, key, anchor, at, size, Color.white);
            image.sprite = Resources.Load<Sprite>("UI/Hub/Hud/" + key);
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            if (key == "plaque-hd") image.pixelsPerUnitMultiplier = 80f / 14f;
            if (key == "currency-plate") image.pixelsPerUnitMultiplier = 109f / size.y;
            return image;
        }

        /// <summary>Картинка стола дурака из UI/Hub/Durak/ (scripts/durak/cut_durak.py и make_room.py).</summary>
        private Image ClubDurakArt(Transform parent, string key, Vector2 anchor, Vector2 at, Vector2 size)
        {
            var image = ClubShape(parent, key, anchor, at, size, Color.white);
            image.sprite = Resources.Load<Sprite>("UI/Hub/Durak/" + key);
            image.type = Image.Type.Simple;
            return image;
        }

        private Text ClubText(Transform parent, string value, Vector2 anchor, Vector2 at, Vector2 size, int fontSize, bool sign = false)
        {
            var text = CreateText(parent, value, fontSize, TextAnchor.MiddleCenter);
            Place(text.rectTransform, anchor, at, size);
            text.fontSize = fontSize; // Canvas scaling already provides the phone hit/read sizes.
            text.color = ClubCream;
            if (sign) text.font = Resources.Load<Font>("Fonts/SignNarrow") ?? _font;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = sign ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
            return text;
        }

        private Button ClubButton(Transform parent, string label, Vector2 anchor, Vector2 at, Vector2 size, Action action)
        {
            var art = ClubArt(parent, "plaque-hd", anchor, at, size);
            art.raycastTarget = true;
            var button = art.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .94f, .8f);
            colors.pressedColor = new Color(.7f, .6f, .5f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            ClubText(art.transform, label, Vector2.one * .5f, Vector2.zero, size - new Vector2(28, 12), 28, true);
            return button;
        }

        private void BuildClubUi()
        {
            var root = (RectTransform)transform;
            _clubSize = root.rect.size;
            var middle = Vector2.one * .5f;
            // Зал и овальный стол на весь экран (2026-11-05, автор: плоский прямоугольник не нравится); место — LayoutClubTable.
            var dark = ClubShape(root, "Club wood", middle, Vector2.zero, root.rect.size, ClubInk);
            Stretch(dark.rectTransform);
            _clubRoom = ClubDurakArt(root, "room", middle, Vector2.zero, root.rect.size).rectTransform;
            var shadow = ClubShape(root, "Table shadow", new Vector2(.5f, 1), Vector2.zero, Vector2.zero, Color.white);
            shadow.sprite = UiTextures.SoftShadow(256, 128, 64, 30, .7f);
            shadow.type = Image.Type.Simple;
            _clubTableShadow = shadow.rectTransform;
            _clubTable = ClubDurakArt(root, "table", new Vector2(.5f, 1), Vector2.zero, Vector2.zero).rectTransform;
            _clubCardBack = Resources.Load<Sprite>("Cards/back");

            ClubButton(root, "В КЛУБ", new Vector2(0, 1), new Vector2(123, -46), new Vector2(190, 72), () => { StopAllCoroutines(); _onExit?.Invoke(); });
            // Чей ход — на табличке посередине шапки, как название окон города.
            _clubStatusPlate = ClubArt(root, "plaque-hd", new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(ClubStatusWidth(root.rect.width), 62)).rectTransform;
            _status = ClubText(root, "", new Vector2(.5f, 1), new Vector2(0, -35), new Vector2(ClubStatusWidth(root.rect.width) - 44, 42), 32, true);
            HubQuestResultLine.Attach(_status, () => _game != null && _game.IsFinished);
            // Шапка говорит только, чей ход (автор, 2026-11-05). Строка под ней живёт для редких событий: поймали на обмане, штраф, итог партии.
            _lastAction = ClubText(root, "", new Vector2(.5f, 1), new Vector2(0, -84), new Vector2(ClubLineWidth(root.rect.width), 32), 25);
            _lastAction.color = new Color(.95f, .84f, .66f);
            _lastAction.resizeTextForBestFit = true; _lastAction.resizeTextMinSize = 19; _lastAction.resizeTextMaxSize = 25;
            var money = ClubArt(root, "currency-plate", new Vector2(1, 1), new Vector2(-318, -46), new Vector2(214, 58));
            var coin = ClubShape(money.transform, "Silver", middle, new Vector2(-80, 0), new Vector2(54, 58), Color.white);
            coin.sprite = HubCurrencyArt.Silver; coin.type = Image.Type.Simple; coin.preserveAspect = true;
            _clubCoins = ClubText(money.transform, "", middle, new Vector2(18, 0), new Vector2(145, 38), 28);
            var settings = ClubArt(root, "button-wood", new Vector2(1, 1), new Vector2(-62, -46), new Vector2(88, 88));
            settings.raycastTarget = true;
            settings.gameObject.AddComponent<Button>().onClick.AddListener(ShowClubSettings);
            ClubArt(settings.transform, "icon-settings", middle, Vector2.zero, new Vector2(53, 53));
            // Чат — круглая кнопка рядом с настройками: левый нижний угол занят своим местом. Значок icon-chat, пока нет — письмо.
            var chat = ClubArt(root, "button-wood", new Vector2(1, 1), new Vector2(-156, -46), new Vector2(88, 88));
            chat.name = "Chat button";
            chat.raycastTarget = true;
            chat.gameObject.AddComponent<Button>().onClick.AddListener(ShowClubSocial);
            ClubArt(chat.transform, Resources.Load<Sprite>("UI/Hub/Hud/icon-chat") != null ? "icon-chat" : "icon-mail", middle, new Vector2(0, 1), new Vector2(55, 44));

            _opponentsArea = CreatePanel("Opponents", root, middle, Vector2.zero, root.rect.size); Stretch(_opponentsArea);
            _tableRow = CreatePanel("Table", root, new Vector2(.5f, 1), new Vector2(0, -TableRowTop), new Vector2(root.rect.width - 460, 230));
            _tableGroup = _tableRow.gameObject.AddComponent<CanvasGroup>();
            var layout = _tableRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            _playerSeatArea = CreatePanel("PlayerSeat", root, new Vector2(.5f, 0), new Vector2(PlayerSeatX(), 124), new Vector2(176, 188));
            _handRow = CreatePanel("Hand", root, new Vector2(.5f, 0), new Vector2(0, HandRowBottom), new Vector2(root.rect.width - 620, 200));

            BuildClubPiles(root);

            // Зелёная и красная таблички сразу говорят, что будет: «бито» — отбой, «беру» — карты к себе.
            _passButton = ClubActionButton(root, "БИТО", "Durak/plate-green", new Vector2(1, 0), new Vector2(-ClubButtonRight, ClubUpperButton), () => _pendingMove = DurakMove.Pass());
            _takeButton = ClubActionButton(root, "БЕРУ", "Hud/play-plate", new Vector2(1, 0), new Vector2(-ClubButtonRight, 100), () => _pendingMove = DurakMove.Take());
            _restartButton = ClubActionButton(root, _hubBankJobMatch ? "К ТУЗЯРЕ" : "ЕЩЁ ПАРТИЯ", "Durak/plate-green", middle, new Vector2(0, -130),
                () => { if (_hubBankJobMatch) _onExit?.Invoke(); else StartNewGame(); });
            _flyLayer = CreatePanel("Flights", root, middle, Vector2.zero, root.rect.size); Stretch(_flyLayer);
            LayoutClubTable();
        }

        /// <summary>Большая кнопка-табличка: красная как «ИГРАТЬ» в городе или зелёная (Durak/plate-green).</summary>
        private Button ClubActionButton(Transform parent, string label, string art, Vector2 anchor, Vector2 at, Action action)
        {
            var size = new Vector2(270, 96);
            var plate = ClubShape(parent, label, anchor, at, size, Color.white);
            plate.sprite = Resources.Load<Sprite>("UI/Hub/" + art);
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 176f / size.y;
            plate.raycastTarget = true;
            plate.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -4);
            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            var colors = button.colors;
            colors.normalColor = new Color(.93f, .93f, .93f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.7f, .7f, .7f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            var text = ClubText(plate.transform, label, Vector2.one * .5f, new Vector2(0, 1), size - new Vector2(48, 20), 40, true);
            text.color = new Color(1f, .95f, .8f);
            text.gameObject.AddComponent<Outline>().effectColor = new Color(.12f, .05f, .02f, .8f);
            return button;
        }

        private static float ClubStatusWidth(float width) => Mathf.Clamp(width - 960, 330, 470);
        private static float ClubLineWidth(float width) => Mathf.Max(420, width - 960);

        /// <summary>Событие держится пару секунд и уходит: дальше в шапке снова только чей ход. Итог партии остаётся.</summary>
        private void FadeClubLine()
        {
            if (_lastAction.text.Length > 0 && !_game.IsFinished && Time.unscaledTime >= _clubLineUntil) _lastAction.text = "";
        }

        // Колода и бита — у края сукна; на широком экране ближе к столу, чтобы подпись не заехала под бокового соперника.
        private float ClubPileX
        {
            get
            {
                var width = ((RectTransform)transform).rect.width;
                return Mathf.Min(width / 2 - 190, Mathf.Max(640, width / 2 - 270));
            }
        }

        // Карты на столе полного размера и ужимаются только в длинном коне. 172 — пара с промежутком, 126 — половина места колоды с зазором.
        private float ClubTableCardScale(int pairs) => Mathf.Min(1f, (ClubPileX - 126) * 2 / (Mathf.Max(1, pairs) * 172f));

        // Дальний край обода — на середине портретов верхнего ряда, ближний уходит за низ экрана: сидишь за столом.
        private const float ClubRimTop = 165, ClubRimMargin = 10, ClubRimBelow = 70, ClubSeatTop = 102;
        // Кнопки действий: правая колонка, «БЕРУ» снизу, «БИТО» и «ПЕРЕВЕСТИ» над ней, в стороне от веера.
        private const float ClubButtonRight = 170, ClubUpperButton = 204;
        // Веер руки — дуга окружности под экраном; крайняя карта наклонена не больше чем на 10°.
        private const float ClubHandRadius = 1700, ClubHandTilt = .175f;

        /// <summary>Где дальний край обода над этой точкой, сверху вниз: места соперников идут по его дуге.</summary>
        private float ClubRimAt(float x)
        {
            var size = ((RectTransform)transform).rect.size;
            var rx = size.x / 2 - ClubRimMargin;
            // Под ободом на картинке ещё и бок стола: сам овал занимает её верх, самое широкое место — на 43 % высоты.
            var ry = (size.y + ClubRimBelow - ClubRimTop) * .43f;
            var k = Mathf.Clamp01(Mathf.Abs(x) / rx);
            return ClubRimTop + ry - ry * Mathf.Sqrt(1 - k * k);
        }

        /// <summary>Под размер экрана: зал накрывает его целиком, стол растягивается, колода и бита лежат на сукне по бокам.</summary>
        private void LayoutClubTable()
        {
            var size = ((RectTransform)transform).rect.size;
            _clubRoom.sizeDelta = new Vector2(16, 9) * Mathf.Max(size.x / 16, size.y / 9);
            var table = new Vector2(size.x - ClubRimMargin * 2, size.y + ClubRimBelow - ClubRimTop);
            _clubTable.sizeDelta = table;
            _clubTable.anchoredPosition = new Vector2(0, -ClubRimTop - table.y / 2);
            _clubTableShadow.sizeDelta = table + new Vector2(90, 90);
            _clubTableShadow.anchoredPosition = _clubTable.anchoredPosition + new Vector2(0, -18);
            // Бита чуть ближе к столу: её подпись стоит над серединой стопки, а не сдвинута, как у колоды.
            var x = ClubPileX;
            var y = -size.y * .58f;
            _deckStackArea.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(-x, y);
            _clubDiscard.anchoredPosition = new Vector2(x - 20, y);
        }

        private Vector2 ClubSeatPosition(int seat, int count)
        {
            var rect = ((RectTransform)transform).rect;
            var end = rect.width / 2 - 126;
            // Равный шаг от края до края: крайние сидят у боков стола, остальные — между ними без провала посередине.
            var x = count == 1 ? 0 : count == 2 ? (seat == 0 ? -1 : 1) * rect.width * .27f : Mathf.Lerp(-end, end, seat / (count - 1f));
            // Портрет сидит за ободом и идёт по его дуге; выше шапки не поднимается. У боков он дальше от обода: ниже лежат колода и бита.
            var side = Mathf.Abs(x) / end;
            return new Vector2(x, -Mathf.Max(ClubSeatTop, ClubRimAt(x) - Mathf.Lerp(48, 80, side * side) - ClubPortraitY));
        }

        private RectTransform BuildClubSeat(Transform parent, DurakPlayer player, bool acting)
        {
            return BuildClubSeatDetails(parent, player, acting);
        }

        private void RefreshClubHand()
        {
            foreach (Transform child in _handRow.Cast<Transform>().Where(t => t.name == "MoveChoice").ToArray())
            { child.SetParent(null); Destroy(child.gameObject); }
            var hand = _game.Players[HumanIndex].Hand;
            var shown = hand.Take(Math.Max(0, hand.Count - RefilledCount(HumanIndex))).ToList();
            shown.Sort(CompareInHand);
            foreach (var card in _clubHand.Keys.Where(c => !shown.Contains(c)).ToArray())
            { var old = _clubHand[card]; old.transform.SetParent(null); Destroy(old.gameObject); _clubHand.Remove(card); }
            var width = Mathf.Min(1300, ((RectTransform)transform).rect.width - 680);
            const float scale = 1.28f;
            var spacing = Mathf.Min(112, (width - CardView.Width * scale - 40) / Mathf.Max(1, shown.Count - 1));
            var mid = (shown.Count - 1) / 2f;
            _handHalfWidth = shown.Count == 0 ? 0 : mid * spacing + CardView.Width * scale / 2;
            var step = Mathf.Min(spacing / ClubHandRadius, ClubHandTilt / Mathf.Max(1, mid));
            for (var i = 0; i < shown.Count; i++)
            {
                var card = shown[i];
                var existed = _clubHand.TryGetValue(card, out var view) && view != null;
                if (!existed) { view = CardView.Create(_handRow, _font, scale); view.ShowFace(card); _clubHand[card] = view; }
                var selected = _choiceCard == card;
                var offset = i - mid;
                var playable = _waitingForHuman && (FindAttackMove(card).HasValue || CountTargets(card) > 0 || FindTransferMove(card).HasValue);
                // Карты лежат на дуге. Те, которыми можно ходить, чуть выглядывают из веера вдоль своей оси, и его край не ломается.
                var angle = offset * step;
                var lift = selected ? ChoiceCardLift : playable ? 12 : 0;
                var position = new Vector2(offset * spacing + Mathf.Sin(angle) * lift, (Mathf.Cos(angle) - 1) * ClubHandRadius + Mathf.Cos(angle) * lift);
                var rotation = selected ? 0 : -angle * Mathf.Rad2Deg;
                var dragging = view.GetComponent<DurakCardDrag>()?.IsDragging ?? false;
                if (!dragging) view.MoveRestTransform(position, rotation, existed && !ClubReduced);
                view.transform.SetSiblingIndex(i);
                if (_waitingForHuman) view.SetClickable(playable ? () => TapClubCard(card) : (Action)null);
                else view.SetPlain();
                view.SetClubSelection(selected, !_waitingForHuman || playable);
                var drag = view.GetComponent<DurakCardDrag>() ?? view.gameObject.AddComponent<DurakCardDrag>();
                drag.Configure(view, playable, () => { _choiceCard = card; _choiceDirty = true; },
                    e => DropClubCard(card, e), () => _choiceDirty = true);
            }
            if (_choiceCard.HasValue && _clubHand.TryGetValue(_choiceCard.Value, out var chosen))
            {
                chosen.transform.SetAsLastSibling();
                var transfer = FindTransferMove(_choiceCard.Value);
                // Перевод бывает только у защиты: «БИТО» в этот момент нет, кнопка встаёт на её место.
                if (transfer.HasValue) CreateChoiceChip("ПЕРЕВЕСТИ", ((RectTransform)transform).rect.width / 2 - ClubButtonRight, ClubUpperButton - HandRowBottom, TransferChipColor, transfer.Value);
            }
        }

        private void TapClubCard(Card card)
        {
            if (!_waitingForHuman || _pendingMove.HasValue) return;
            if (_choiceCard == card)
            {
                var attack = FindAttackMove(card);
                var transfer = FindTransferMove(card);
                var only = attack ?? (CountTargets(card) + (transfer.HasValue ? 1 : 0) == 1 ? transfer ?? FirstTargetMove(card) : null);
                if (only.HasValue) { QueueClubMove(only.Value); return; }
            }
            ToggleMoveChoice(card);
        }

        private void QueueClubMove(DurakMove move)
        {
            if (!_waitingForHuman || _pendingMove.HasValue) return;
            if (move.Type != DurakMoveType.Pass && move.Type != DurakMoveType.Take && _clubHand.TryGetValue(move.Card, out var view))
            {
                var point = transform.InverseTransformPoint(view.Rect.position);
                _clubLaunch = new Vector2(point.x, point.y - ((RectTransform)transform).rect.yMax);
            }
            _choiceCard = null;
            _pendingMove = move;
        }

        private bool DropClubCard(Card card, PointerEventData e)
        {
            if (!_waitingForHuman || _pendingMove.HasValue) return false;
            // Aim at the explicit attack slot; the same legal/cheat move lists power taps and drags.
            for (var slot = 0; slot < _tableRow.childCount; slot++)
            {
                var attack = _tableRow.GetChild(slot).GetComponentInChildren<CardView>();
                if (attack != null && RectTransformUtility.RectangleContainsScreenPoint(attack.Rect, e.position, e.pressEventCamera))
                {
                    var defense = FindTargetMove(card, slot);
                    if (defense.HasValue) { QueueClubMove(defense.Value); return true; }
                }
            }
            if (RectTransformUtility.RectangleContainsScreenPoint(_tableRow, e.position, e.pressEventCamera))
            {
                var attack = FindAttackMove(card);
                if (attack.HasValue) { QueueClubMove(attack.Value); return true; }
            }
            return false;
        }

        private IEnumerator CollectClubCards(int? takenBy)
        {
            yield return new WaitForSeconds(BoutHoldDuration);
            var cards = _tableRow.GetComponentsInChildren<CardView>();
            var to = takenBy.HasValue ? SeatAnchorPosition(takenBy.Value) : ClubTopPoint(_clubDiscard.position);
            var starts = cards.Select(c => ClubTopPoint(c.Rect.position)).ToArray();
            for (var i = 0; i < cards.Length; i++)
            {
                cards[i].SetPlain(); cards[i].transform.SetParent(_flyLayer, true);
                cards[i].Rect.anchorMin = cards[i].Rect.anchorMax = new Vector2(.5f, 1);
                cards[i].Rect.anchoredPosition = starts[i];
            }
            foreach (var shadow in _tableRow.GetComponentsInChildren<Image>()) shadow.enabled = false;
            var duration = ClubReduced ? .12f : .48f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                for (var i = 0; i < cards.Length; i++)
                {
                    var p = Mathf.Clamp01((t - (ClubReduced ? 0 : i * .009f)) / (duration - .1f));
                    var k = Mathf.SmoothStep(0, 1, p);
                    if (!ClubReduced)
                    {
                        cards[i].Rect.anchoredPosition = Vector2.Lerp(starts[i], to, k) + Vector2.up * Mathf.Sin(k * Mathf.PI) * 32;
                        cards[i].Rect.localScale = Vector3.one * Mathf.Lerp(1, .42f, k);
                        cards[i].Rect.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0, -8, k));
                    }
                    cards[i].GetComponent<CanvasGroup>().alpha = 1 - (ClubReduced ? k : Mathf.Clamp01((k - .75f) * 4));
                }
                yield return null;
            }
            foreach (var view in cards) Destroy(view.gameObject);
            if (!takenBy.HasValue) _clubDiscardCount += cards.Length;
            PaintClubDiscard();
        }

        private Vector2 ClubTopPoint(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return new Vector2(local.x, local.y - ((RectTransform)transform).rect.yMax);
        }

        private void RefreshClubControls()
        {
            RefreshClubOpenProfile();
            _clubCoins.text = (PlayerHubRuntime.Current?.Profile.Coins ?? 0).ToString("N0");
            _passButton.gameObject.SetActive(_humanLegalMoves.Any(m => m.Type == DurakMoveType.Pass));
            _takeButton.gameObject.SetActive(_humanLegalMoves.Any(m => m.Type == DurakMoveType.Take));
            // Всё отбито — «бито»; соперник взял и остаётся только подкинуть или отказаться — «пас».
            _passButton.GetComponentInChildren<Text>().text = _game.Table.All(slot => slot.IsDefended) ? "БИТО" : "ПАС";
            if (_game.IsFinished)
            {
                _status.text = !_game.LoserIndex.HasValue ? "НИЧЬЯ" : _game.LoserIndex == HumanIndex ? "РЕВАНШ?" : "ПОБЕДА!";
                _lastAction.text = _game.LoserIndex.HasValue ? "Последним остался: " + _game.Players[_game.LoserIndex.Value].Name : "Все вышли из игры";
                return;
            }
            var actor = DurakTurnOrder.GetActingPlayer(_game);
            _status.color = _waitingForHuman ? ActiveGlow : ClubCream;
            _status.text = _waitingForHuman ? (_game.DefenderIndex == HumanIndex ? "ВАША ЗАЩИТА" : "ВАШ ХОД") : actor.HasValue ? "ХОДИТ " + _game.Players[actor.Value].Name.ToUpperInvariant() : "ЗАВЕРШАЕМ РОЗЫГРЫШ";
        }

        private RectTransform ClubPopup(string title, float height)
        {
            CloseClubPopup();
            var root = (RectTransform)transform;
            _clubPopup = CreatePanel("Club menu", root, Vector2.one * .5f, Vector2.zero, root.rect.size); Stretch(_clubPopup);
            var dismiss = ClubShape(_clubPopup, "Dismiss", Vector2.one * .5f, Vector2.zero, root.rect.size, new Color(0, 0, 0, .52f));
            Stretch(dismiss.rectTransform); dismiss.raycastTarget = true;
            dismiss.gameObject.AddComponent<Button>().onClick.AddListener(CloseClubPopup);
            var panel = ClubShape(_clubPopup, "Menu panel", Vector2.one * .5f, Vector2.zero, new Vector2(600, height), ClubGold);
            panel.raycastTarget = true;
            ClubShape(panel.transform, "Menu inset", Vector2.one * .5f, Vector2.zero, new Vector2(594, height - 6), ClubInk);
            ClubText(panel.transform, title, new Vector2(.5f, 1), new Vector2(-25, -44), new Vector2(470, 50), 32, true);
            ClubButton(panel.transform, "×", new Vector2(1, 1), new Vector2(-42, -42), new Vector2(62, 62), CloseClubPopup);
            return panel.rectTransform;
        }

        private void CloseClubPopup()
        {
            _clubProfileIndex = -1; _clubProfileRole = null;
            if (_clubChatInput != null) _clubChatInput.DeactivateInputField();
            _clubChatInput = null; _clubChatSend = null; _clubChatLog = null;
            _clubSocialButtons.Clear(); _clubSocialHint = null;
            if (_clubPopup == null) return;
            _clubPopup.gameObject.SetActive(false); Destroy(_clubPopup.gameObject); _clubPopup = null;
        }

        private void ShowClubSocial()
        {
            BuildClubChat();
        }

        private void RefreshClubSocialState()
        {
            if (_clubSocialHint == null) return;
            var remaining = Mathf.Max(0, 2 - (Time.unscaledTime - _clubReactionAt));
            foreach (var button in _clubSocialButtons) button.interactable = remaining <= 0;
            if (_clubChatSend != null) _clubChatSend.interactable = remaining <= 0 && !string.IsNullOrWhiteSpace(_clubChatInput.text);
            _clubSocialHint.text = remaining > 0 ? "Следующее сообщение через " + Mathf.CeilToInt(remaining) + " с" : "Общий чат · без личных сообщений";
        }

        private void SendClubReaction(string phrase)
        {
            phrase = (phrase ?? "").Trim();
            if (phrase.Length == 0 || Time.unscaledTime - _clubReactionAt < 2) return;
            if (phrase.Length > 160) phrase = phrase.Substring(0, 160);
            _clubReactionAt = Time.unscaledTime;
            _clubMessages.Add(new ClubTableMessage { Sender = HumanIndex, Text = phrase });
            if (_clubMessages.Count > 30) _clubMessages.RemoveAt(0);
            CloseClubPopup();
            if (_clubBubble != null) { _clubBubble.gameObject.SetActive(false); Destroy(_clubBubble.gameObject); }
            if (_clubReactionsMuted) return;
            // Broadcast line shares the public header; cards and turn information remain visible.
            var width = _lastAction.rectTransform.rect.width;
            _clubBubble = ClubShape(transform, "Table message", new Vector2(.5f, 1), new Vector2(0, -82), new Vector2(width, 34), ClubInk).rectTransform;
            var line = _game.Players[HumanIndex].Name + ": " + phrase;
            var limit = Mathf.Max(16, Mathf.FloorToInt(width / 12));
            if (line.Length > limit) line = line.Substring(0, limit - 1) + "…";
            var text = ClubText(_clubBubble, line, Vector2.one * .5f, Vector2.zero, new Vector2(width, 34), 23);
            text.supportRichText = false;
            StartCoroutine(HideClubReaction(_clubBubble));
        }

        private IEnumerator HideClubReaction(RectTransform bubble)
        {
            yield return new WaitForSecondsRealtime(2.6f);
            if (bubble != null) Destroy(bubble.gameObject);
        }

        private void ShowClubSettings()
        {
            var panel = ClubPopup("НАСТРОЙКИ СТОЛА", 400);
            var settings = PlayerHubRuntime.Current?.Settings;
            ClubButton(panel, "ЗВУК: " + (settings?.Sound != false ? "ВКЛ" : "ВЫКЛ"), new Vector2(.5f, 1), new Vector2(0, -132), new Vector2(500, 66), () => SetClubSetting("sound", !(settings?.Sound ?? true)));
            ClubButton(panel, "МЕНЬШЕ ДВИЖЕНИЯ: " + (ClubReduced ? "ДА" : "НЕТ"), new Vector2(.5f, 1), new Vector2(0, -214), new Vector2(500, 66), () => SetClubSetting("reduced_motion", !ClubReduced));
            ClubButton(panel, "СООБЩЕНИЯ НА СТОЛЕ: " + (_clubReactionsMuted ? "ВЫКЛ" : "ВКЛ"), new Vector2(.5f, 1), new Vector2(0, -296), new Vector2(500, 66), () => { _clubReactionsMuted = !_clubReactionsMuted; if (_clubReactionsMuted && _clubBubble != null) Destroy(_clubBubble.gameObject); ShowClubSettings(); });
            ClubText(panel, "Партия продолжается, пока открыто меню", new Vector2(.5f, 0), new Vector2(0, 32), new Vector2(540, 28), 18);
        }

        private async void SetClubSetting(string key, bool flag)
        {
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "settings_update", Target = key, Flag = flag });
            if (this == null) return;
            if (result.Success && _clubPopup != null) ShowClubSettings();
            else if (!result.Success) SetLastAction(result.Error);
        }

        private void Update()
        {
            if (_game == null || _handRow == null) return;
            RefreshClubSocialState();
            FadeClubLine();
            ApplyClubSafeArea();
            var size = ((RectTransform)transform).rect.size;
            if ((size - _clubSize).sqrMagnitude < 1) return;
            _clubSize = size;
            _tableRow.anchoredPosition = new Vector2(0, -TableRowTop);
            _tableRow.sizeDelta = new Vector2(size.x - 460, 230);
            _clubStatusPlate.sizeDelta = new Vector2(ClubStatusWidth(size.x), 62);
            _status.rectTransform.sizeDelta = new Vector2(ClubStatusWidth(size.x) - 44, 42);
            _lastAction.rectTransform.sizeDelta = new Vector2(ClubLineWidth(size.x), 32);
            LayoutClubTable();
            _tableSignature = null;
            Refresh();
        }

        private void ApplyClubSafeArea()
        {
            if (!Application.isMobilePlatform || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            if (safe == _clubSafeArea || safe.width <= 0 || safe.height <= 0) return;
            _clubSafeArea = safe;
            var root = (RectTransform)transform;
            root.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            root.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            root.offsetMin = root.offsetMax = Vector2.zero;
        }
    }

    internal sealed class DurakCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CardView _view;
        private bool _canDrag, _dragging;
        private Action _begin, _cancel;
        private Func<PointerEventData, bool> _drop;
        private Vector2 _offset, _rest;
        private float _angle;
        public bool IsDragging => _dragging;
        public void Configure(CardView view, bool canDrag, Action begin, Func<PointerEventData, bool> drop, Action cancel)
        { _view = view; _canDrag = canDrag; _begin = begin; _drop = drop; _cancel = cancel; }
        public void OnBeginDrag(PointerEventData e)
        {
            if (!_canDrag) return;
            _dragging = true; e.eligibleForClick = false;
            _view.DisableHover(); _rest = _view.Rect.anchoredPosition; _angle = _view.Rect.localEulerAngles.z;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, e.position, e.pressEventCamera, out var local);
            _offset = _view.Rect.anchoredPosition - local;
            transform.SetAsLastSibling(); _begin();
        }
        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform.parent, e.position, e.pressEventCamera, out var local);
            _view.DisableHover();
            _view.Rect.anchoredPosition = local + _offset;
            _view.Rect.localRotation = Quaternion.identity;
        }
        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (!_drop(e)) { _view.MoveRestTransform(_rest, _angle, true); _cancel(); }
        }
    }
}
