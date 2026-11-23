using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class DurakTableController
    {
        private Text _clubTrumpSuit, _clubTrumpName, _clubDiscardEmpty;
        private Image _clubTrumpMedal, _clubDeckSlot, _clubDiscardSlot;
        private int _clubDeckLevel = -1;
        // Углы верхней карты на картинках deck-1..3, доли от левого нижнего угла: левый низ, левый верх, правый верх, правый низ.
        private static readonly Vector2[][] ClubDeckTop =
        {
            new[] { new Vector2(.025f, .309f), new Vector2(.382f, .981f), new Vector2(.978f, .895f), new Vector2(.755f, .207f) },
            new[] { new Vector2(.026f, .378f), new Vector2(.311f, .983f), new Vector2(.977f, .901f), new Vector2(.774f, .297f) },
            new[] { new Vector2(.025f, .445f), new Vector2(.299f, .984f), new Vector2(.979f, .907f), new Vector2(.75f, .363f) },
        };
        private InputField _clubChatInput;
        private Button _clubChatSend;
        private Text _clubChatLog;
        private int _clubProfileIndex = -1;
        private Text _clubProfileRole;
        private readonly List<ClubTableMessage> _clubMessages = new List<ClubTableMessage>();
        private sealed class ClubTableMessage { public int Sender; public string Text; }
        private static readonly Vector2 ClubMiddle = Vector2.one * .5f;
        // Оранжевый без жёлтого: иначе атака сливается с золотом рамки и обода.
        private static readonly Color ClubAttack = new Color(1f, .42f, .1f);
        private static readonly Color ClubDefense = new Color(.42f, .77f, 1f);
        private const int ClubDiscardShown = 20;
        // Сколько карт у соперника, не видно (автор, 2026-11-05): веер всегда одного размера, чисел на столе нет.
        private const int ClubHeldShown = 5;
        private const float ClubPortraitSize = 128, ClubPortraitY = 66;
        // Стопка стоит левее середины своего места: справа от неё лежит козырь.
        private static readonly Vector2 ClubDeckAt = new Vector2(-44, 0);
        private static readonly Vector2 ClubDeckLabelAt = new Vector2(20, 112);

        private void BuildClubPiles(RectTransform root)
        {
            // Козырь нужен весь кон и не зависит от колоды: крупный значок и название масти у левого края шапки.
            var trumpHud = CreatePanel("Trump emblem", root, new Vector2(0, 1), new Vector2(278, -47), new Vector2(92, 92));
            var glow = ClubShape(trumpHud, "Trump glow", ClubMiddle, Vector2.zero, new Vector2(152, 152), Color.white);
            glow.sprite = UiTextures.RadialGlow(128, new Color(1f, .8f, .35f));
            glow.type = Image.Type.Simple;
            glow.color = new Color(1, 1, 1, .6f);
            _clubTrumpMedal = ClubDurakArt(trumpHud, "trump-spades", ClubMiddle, Vector2.zero, new Vector2(92, 92));
            _clubTrumpSuit = ClubText(trumpHud, "", ClubMiddle, new Vector2(0, 2), new Vector2(62, 62), 48);
            _clubTrumpSuit.font = UiFonts.Symbols;
            _trumpInfo = ClubText(trumpHud, "КОЗЫРЬ", ClubMiddle, new Vector2(112, 18), new Vector2(124, 30), 25, true);
            _clubTrumpName = ClubText(trumpHud, "", ClubMiddle, new Vector2(112, -16), new Vector2(124, 40), 37, true);
            _clubTrumpName.color = new Color(1f, .84f, .4f);

            // Колода и бита лежат на сукне слева и справа (LayoutClubTable). Козырь — поперёк под стопкой и виден почти целиком,
            // число — в подписи: поверх карт ничего не лежит. Контур остаётся только у пустого места.
            var deck = CreatePanel("Trump", root, new Vector2(.5f, 1), Vector2.zero, new Vector2(216, 176));
            _clubDeckSlot = ClubSlot(deck, ClubDeckAt);
            var shadow = ClubShape(deck, "Deck shadow", ClubMiddle, new Vector2(8, -58), new Vector2(236, 92), Color.white);
            shadow.sprite = UiTextures.SoftShadow(128, 72, 28, 20, .48f);
            // A sideways face card and a real stack; no solid rectangle underneath.
            _trumpFrame = CreatePanel("TrumpFrame", deck, ClubMiddle, Vector2.zero, Vector2.zero);
            _trumpCard = CardView.Create(deck, _font, .74f);
            _trumpCard.SetRestTransform(new Vector2(52, -30), -84);
            _deckStackArea = CreatePanel("DeckStack", deck, ClubMiddle, ClubDeckAt, new Vector2(130, 168));
            _deckInfo = ClubText(deck, "КОЛОДА", ClubMiddle, ClubDeckLabelAt, new Vector2(150, 34), 28, true);

            _clubDiscard = CreatePanel("Discard", root, new Vector2(.5f, 1), Vector2.zero, new Vector2(216, 176));
            _clubDiscardSlot = ClubSlot(_clubDiscard, Vector2.zero);
            _clubDiscardCards = CreatePanel("Discard cards", _clubDiscard, ClubMiddle, Vector2.zero, new Vector2(150, 130));
            _clubDiscardText = ClubText(_clubDiscard, "БИТА", ClubMiddle, new Vector2(0, 112), new Vector2(150, 34), 28, true);
            _clubDiscardEmpty = ClubText(_clubDiscard, "", ClubMiddle, Vector2.zero, new Vector2(90, 120), 22, true);
            var mark = ClubText(_clubDiscardEmpty.transform, "♠", ClubMiddle, Vector2.zero, new Vector2(90, 90), 58);
            mark.font = UiFonts.Symbols; mark.color = new Color(.95f, .68f, .4f, .26f);
        }

        /// <summary>Контур пустого места на сукне: золотистая строчка там, где лежала колода или лежит бита (Durak/slot).</summary>
        private Image ClubSlot(Transform parent, Vector2 at)
        {
            var slot = ClubDurakArt(parent, "slot", ClubMiddle, at, new Vector2(132, 168));
            slot.color = new Color(1f, .78f, .5f, .3f);
            return slot;
        }

        private static string ClubSuitName(Suit suit) =>
            suit == Suit.Spades ? "ПИКИ" : suit == Suit.Hearts ? "ЧЕРВИ" : suit == Suit.Diamonds ? "БУБНЫ" : "ТРЕФЫ";

        /// <summary>Карты соперника рубашками: веер раскрыт из-за портрета к центру стола и не выдаёт, сколько их на самом деле.</summary>
        private void ClubHeldCards(Transform seat, int side)
        {
            for (var i = 0; i < ClubHeldShown; i++)
            {
                var back = ClubShape(seat, "Held card", new Vector2(.5f, 1), new Vector2(side * 50, -92), new Vector2(50, 72), Color.white);
                back.sprite = _clubCardBack;
                back.type = Image.Type.Simple;
                back.rectTransform.pivot = new Vector2(.5f, .08f);
                back.rectTransform.localRotation = Quaternion.Euler(0, 0, -side * (14 + 62f * i / (ClubHeldShown - 1)));
                // Тёмная рубашка на тёмном зале: светлая кромка отделяет карты друг от друга.
                var edge = back.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color(1f, .9f, .68f, .6f);
                edge.effectDistance = new Vector2(1, -1);
            }
        }

        private void PaintClubPiles()
        {
            var available = _game.DeckCount > 0;
            PaintClubDeck();
            _trumpCard.gameObject.SetActive(available);
            _trumpFrame.gameObject.SetActive(false);
            _clubDeckSlot.gameObject.SetActive(!available);
            _deckStackArea.parent.Find("Deck shadow").gameObject.SetActive(available);
            _deckInfo.text = available ? "КОЛОДА" : "КОЛОДА\nПУСТА";
            _deckInfo.rectTransform.anchoredPosition = available ? ClubDeckLabelAt : ClubDeckAt;
            // Масть нарисована на медальоне; символ в тексте остаётся невидимым — по нему проверки знают козырь.
            _clubTrumpMedal.sprite = Resources.Load<Sprite>("UI/Hub/Durak/trump-" + _game.Trump.ToString().ToLowerInvariant());
            _clubTrumpSuit.text = CardView.SuitText(_game.Trump);
            _clubTrumpName.text = ClubSuitName(_game.Trump);
            _trumpInfo.text = "КОЗЫРЬ";
            _clubTrumpSuit.color = Color.clear;
        }

        /// <summary>
        /// Колода — картинка нужной толщины (срезы нейтральные, верх пустой) и поверх неё настоящая рубашка в той же перспективе:
        /// скину достаточно одной картинки рубашки. Толщина: до 6, до 18 и больше карт.
        /// </summary>
        private void PaintClubDeck()
        {
            var count = _game.DeckCount;
            var level = count == 0 ? 0 : count <= 6 ? 1 : count <= 18 ? 2 : 3;
            if (level == _clubDeckLevel) return;
            _clubDeckLevel = level;
            Clear(_deckStackArea);
            if (level == 0) return;
            var deck = ClubDurakArt(_deckStackArea, "deck-" + level, new Vector2(.5f, 1), Vector2.zero, Vector2.zero);
            // Ширина общая, опора сверху: верхняя карта стоит на месте, а толщина растёт вниз.
            deck.rectTransform.pivot = new Vector2(.5f, 1);
            deck.rectTransform.anchoredPosition = Vector2.zero;
            deck.rectTransform.sizeDelta = new Vector2(130, 130 * deck.sprite.rect.height / deck.sprite.rect.width);
            var top = ClubShape(deck.transform, "Top card", ClubMiddle, Vector2.zero, Vector2.zero, Color.white);
            Stretch(top.rectTransform);
            top.sprite = _clubCardBack;
            top.type = Image.Type.Simple;
            top.gameObject.AddComponent<DurakCardQuad>().Corners = ClubDeckTop[level - 1];
        }

        private void PaintClubDiscard()
        {
            Clear(_clubDiscardCards);
            _clubDiscardEmpty.gameObject.SetActive(_clubDiscardCount == 0);
            _clubDiscardSlot.gameObject.SetActive(_clubDiscardCount == 0);
            if (_clubDiscardCount == 0) return;
            // Бита — настоящие карты с той же рубашкой, что у всех: любой скин виден в ней сам. Случай постоянный, поэтому
            // лежащие карты не прыгают, новые ложатся сверху, а стопка расползается шире.
            var shadow = ClubShape(_clubDiscardCards, "Discard shadow", ClubMiddle, new Vector2(0, -8), new Vector2(214, 188), Color.white);
            shadow.sprite = UiTextures.SoftShadow(128, 120, 50, 26, .55f);
            shadow.type = Image.Type.Simple;
            var random = new System.Random(11);
            for (var i = 0; i < Mathf.Min(ClubDiscardShown, _clubDiscardCount); i++)
            {
                var turn = (float)random.NextDouble() * Mathf.PI * 2;
                var reach = Mathf.Min(24, 5 + i * 1.4f) * (float)random.NextDouble();
                var card = ClubShape(_clubDiscardCards, "Discard card", ClubMiddle, new Vector2(Mathf.Cos(turn), Mathf.Sin(turn) * .8f) * reach, new Vector2(82, 116), Color.white);
                card.sprite = _clubCardBack;
                card.type = Image.Type.Simple;
                card.rectTransform.localRotation = Quaternion.Euler(0, 0, -40 + (float)random.NextDouble() * 80);
                card.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .55f);
            }
        }

        private string ClubRole(DurakPlayer player)
        {
            if (player.IsOut) return "Вышел";
            if (_game.IsFinished) return _game.LoserIndex == player.Index ? "Остался с картами" : "Завершил партию";
            if (player.Index == _game.DefenderIndex) return "Защищается";
            if (player.Index == _game.AttackerIndex) return "Атакует";
            return DurakTurnOrder.GetActingPlayer(_game) == player.Index ? "Подкидывает" : "";
        }

        private RectTransform CreateClubPortrait(Transform parent, Sprite portrait, string name, Vector2 anchor, Vector2 at, float diameter)
        {
            var ring = ClubShape(parent, "Portrait", anchor, at, Vector2.one * diameter, ClubGold, true);
            var face = ClubShape(ring.transform, "Portrait crop", ClubMiddle, Vector2.zero, Vector2.one * (diameter - 16), ClubInk, true);
            face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            if (portrait != null)
            {
                var art = ClubShape(face.transform, "Face", ClubMiddle, new Vector2(0, -.20f * diameter), new Vector2(1.23f, 1.4f) * diameter, Color.white);
                art.sprite = portrait; art.type = Image.Type.Simple; art.preserveAspect = true;
            }
            else ClubText(face.transform, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1), ClubMiddle, Vector2.zero, Vector2.one * diameter, Mathf.RoundToInt(diameter * .4f));
            ClubArt(ring.transform, "avatar-frame", ClubMiddle, Vector2.zero, Vector2.one * (diameter + 6));
            return ring.rectTransform;
        }

        private RectTransform BuildClubSeatDetails(Transform parent, DurakPlayer player, bool acting)
        {
            var seat = CreatePanel("Seat " + player.Index, parent, ClubMiddle, Vector2.zero, new Vector2(176, 188));
            var top = new Vector2(.5f, 1);
            var centre = new Vector2(0, -ClubPortraitY);
            var isDefender = player.Index == _game.DefenderIndex && !player.IsOut && !_game.IsFinished;
            var isAttacker = player.Index == _game.AttackerIndex && !player.IsOut && !_game.IsFinished;
            var roleColor = isDefender ? ClubDefense : ClubAttack;
            if (isDefender || isAttacker || acting)
            {
                // Мягкое свечение за рамкой вместо сплошного круга (автор, 2026-11-05: круг слишком грубый).
                var halo = ClubShape(seat, "Role glow", top, centre, Vector2.one * 226, Color.white);
                halo.sprite = UiTextures.RadialGlow(128, roleColor, .59f); halo.type = Image.Type.Simple;
                halo.color = new Color(1, 1, 1, acting ? .9f : .55f);
                if (acting) halo.gameObject.AddComponent<DurakSeatLight>().Configure(halo);
            }
            var held = Math.Max(0, player.Hand.Count - RefilledCount(player.Index));
            // Веер — со стороны центра стола: лицо ничем не закрыто, у правых мест всё зеркально.
            var side = player.Index != HumanIndex && ClubSeatPosition(player.Index - 1, _game.Players.Count - 1).x > 0 ? -1 : 1;
            if (player.Index != HumanIndex && !player.IsOut && held > 0) ClubHeldCards(seat, side);
            var ring = CreateClubPortrait(seat, PortraitFor(player.Index), player.Name, top, centre, ClubPortraitSize);
            var hit = ring.GetComponent<Image>(); hit.raycastTarget = true;
            var target = player.Index;
            ring.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowClubPlayerProfile(target));
            var shortName = player.Name.Length > 13 ? player.Name.Substring(0, 12) + "…" : player.Name;
            // Имя — на плашке внахлёст с низом портрета: читается и на сукне, и на золотом ободе.
            var plate = ClubArt(seat, "currency-plate", top, new Vector2(0, -136), new Vector2(168, 40));
            var label = ClubText(plate.transform, player.Index == HumanIndex ? "ВЫ" : shortName, ClubMiddle, new Vector2(0, 1), new Vector2(138, 32), 28, true);
            label.supportRichText = false; label.color = acting ? ActiveGlow : ClubCream;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 18; label.resizeTextMaxSize = 28;
            var roleText = ClubRole(player);
            if (roleText.Length > 0)
            {
                // Роль — на цветной плашке с мечом или щитом: тёмная подпись на телефоне терялась. Значок стоит на плашке, а не на лице.
                var fighting = isAttacker || isDefender;
                var fill = isDefender ? new Color(.09f, .35f, .74f) : isAttacker ? new Color(.78f, .2f, .05f) : new Color(.06f, .03f, .025f, .92f);
                var chip = ClubShape(seat, "Role chip", top, new Vector2(0, -172), new Vector2(172, 34), fill);
                var edge = chip.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color(1f, .9f, .68f, .75f);
                edge.effectDistance = new Vector2(1.5f, -1.5f);
                var role = ClubText(chip.transform, roleText, ClubMiddle, new Vector2(fighting ? 15 : 0, 0), new Vector2(fighting ? 134 : 164, 30), 24);
                role.color = fighting ? Color.white : ClubCream;
                role.fontStyle = FontStyle.Bold;
                role.resizeTextForBestFit = true; role.resizeTextMinSize = 17; role.resizeTextMaxSize = 24;
                if (fighting) ClubDurakArt(seat, isDefender ? "role-defend" : "role-attack", top, new Vector2(-84, -170), new Vector2(56, 56)).preserveAspect = true;
            }
            return seat;
        }

        private void ShowClubPlayerProfile(int index)
        {
            if (_game == null || index < 0 || index >= _game.Players.Count) return;
            var player = _game.Players[index];
            var own = index == HumanIndex;
            var snapshot = PlayerHubRuntime.Current;
            var id = own ? snapshot?.Profile.Id : HubRoster.PlayerIdFor("durak", index, _game.Players.Count);
            var person = own || string.IsNullOrEmpty(id) ? null : snapshot?.People.Find(p => p.Id == id);
            var panel = ClubPopup(own ? "МОЙ ПРОФИЛЬ" : "ПРОФИЛЬ ИГРОКА", 540);
            panel.name = "Player profile " + index;
            CreateClubPortrait(panel, PortraitFor(index), player.Name, new Vector2(.5f, 1), new Vector2(-180, -184), 150);
            var name = ClubText(panel, player.Name, new Vector2(.5f, 1), new Vector2(65, -130), new Vector2(310, 52), 34, true);
            name.supportRichText = false;
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 23; name.resizeTextMaxSize = 34;
            ClubText(panel, own ? "Ваш профиль клуба" : person?.Status ?? "Соперник за столом · бот", new Vector2(.5f, 1), new Vector2(65, -179), new Vector2(310, 54), 22);
            var level = own ? snapshot?.Profile.Level : person?.Level;
            var rating = own ? snapshot?.Profile.Rating : person?.Rating;
            var details = level.HasValue && rating.HasValue ? "Уровень " + level + "\nРейтинг " + rating : "Игрок этой партии";
            ClubText(panel, details, new Vector2(.5f, 1), new Vector2(65, -233), new Vector2(310, 64), 23);
            _clubProfileIndex = index;
            _clubProfileRole = ClubText(panel, "", new Vector2(.5f, 1), new Vector2(0, -308), new Vector2(500, 40), 29, true);
            RefreshClubOpenProfile();
            if (own && snapshot != null)
                ClubText(panel, "Сыграно " + snapshot.Profile.Matches + "   ·   Побед " + snapshot.Profile.Wins, new Vector2(.5f, 1), new Vector2(0, -362), new Vector2(520, 34), 23);
            ClubButton(panel, "К СТОЛУ", new Vector2(.5f, 0), new Vector2(0, 66), new Vector2(260, 70), CloseClubPopup);
            ClubText(panel, "Партия продолжается", new Vector2(.5f, 0), new Vector2(0, 20), new Vector2(460, 28), 20);
        }

        private void RefreshClubOpenProfile()
        {
            if (_clubProfileIndex < 0 || _clubProfileRole == null || _game == null || _clubProfileIndex >= _game.Players.Count) return;
            var player = _game.Players[_clubProfileIndex];
            _clubProfileRole.text = ClubRole(player);
            _clubProfileRole.color = player.Index == _game.DefenderIndex ? ClubDefense : ClubAttack;
        }

        private void BuildClubChat()
        {
            var panel = ClubPopup("ЧАТ СТОЛА", 650);
            ClubText(panel, "Сообщение всему столу", new Vector2(.5f, 1), new Vector2(0, -95), new Vector2(520, 32), 24);
            var viewport = CreatePanel("Messages viewport", panel, new Vector2(.5f, 1), new Vector2(0, -249), new Vector2(530, 234));
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, .12f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            _clubChatLog = ClubText(viewport, "", new Vector2(.5f, 1), Vector2.zero, new Vector2(510, 234), 25);
            _clubChatLog.supportRichText = false; _clubChatLog.alignment = TextAnchor.UpperLeft;
            _clubChatLog.verticalOverflow = VerticalWrapMode.Overflow;
            _clubChatLog.rectTransform.pivot = new Vector2(.5f, 1);
            scroll.content = _clubChatLog.rectTransform; scroll.viewport = viewport;
            RefreshClubChatLog(); scroll.verticalNormalizedPosition = 0;

            var input = ClubShape(panel, "Message input", new Vector2(.5f, 1), new Vector2(-64, -411), new Vector2(402, 64), new Color(.25f, .14f, .09f)); input.raycastTarget = true;
            _clubChatInput = input.gameObject.AddComponent<InputField>(); _clubChatInput.targetGraphic = input;
            _clubChatInput.textComponent = ClubText(input.transform, "", ClubMiddle, Vector2.zero, new Vector2(366, 44), 25);
            _clubChatInput.textComponent.supportRichText = false; _clubChatInput.textComponent.alignment = TextAnchor.MiddleLeft;
            _clubChatInput.characterLimit = 160; _clubChatInput.lineType = InputField.LineType.SingleLine;
            var placeholder = ClubText(input.transform, "Написать всем…", ClubMiddle, Vector2.zero, new Vector2(366, 44), 25);
            placeholder.color = new Color(.8f, .69f, .54f); placeholder.alignment = TextAnchor.MiddleLeft; _clubChatInput.placeholder = placeholder;
            _clubChatSend = ClubButton(panel, "→", new Vector2(.5f, 1), new Vector2(209, -411), new Vector2(110, 66), () => SendClubReaction(_clubChatInput.text));
            _clubChatInput.onValueChanged.AddListener(_ => RefreshClubSocialState());
            var phrases = new[] { "Хороший ход!", "Вот это карта!", "Повезло!", "Реванш?" };
            for (var i = 0; i < phrases.Length; i++)
            {
                var phrase = phrases[i];
                _clubSocialButtons.Add(ClubButton(panel, phrase, new Vector2(.5f, 1), new Vector2(i % 2 == 0 ? -139 : 139, -496 - i / 2 * 76), new Vector2(260, 66), () => SendClubReaction(phrase)));
            }
            _clubSocialHint = ClubText(panel, "", new Vector2(.5f, 0), new Vector2(0, 26), new Vector2(540, 30), 20);
            RefreshClubSocialState();
        }

        private void RefreshClubChatLog()
        {
            if (_clubChatLog == null) return;
            _clubChatLog.text = _clubMessages.Count == 0 ? "Здесь сообщения участников стола.\nНачни разговор!" : string.Join("\n\n", _clubMessages.Select(m => _game.Players[m.Sender].Name + ": " + m.Text));
            _clubChatLog.rectTransform.sizeDelta = new Vector2(510, Mathf.Max(234, _clubChatLog.preferredHeight + 12));
        }
    }

    /// <summary>Кладёт картинку на четыре заданные точки своего прямоугольника: так рубашка ложится на верх нарисованной колоды.</summary>
    [RequireComponent(typeof(Image))]
    internal sealed class DurakCardQuad : BaseMeshEffect
    {
        // Левый низ, левый верх, правый верх, правый низ — доли прямоугольника от левого нижнего угла.
        public Vector2[] Corners;

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || Corners == null || mesh.currentVertCount != 4) return;
            var bottomLeft = UIVertex.simpleVert;
            var topRight = UIVertex.simpleVert;
            mesh.PopulateUIVertex(ref bottomLeft, 0);
            mesh.PopulateUIVertex(ref topRight, 2);
            var rect = graphic.rectTransform.rect;
            mesh.Clear();
            // Сетка вместо двух треугольников: иначе на рисунке рубашки виден излом по диагонали.
            const int columns = 4, rows = 6;
            for (var y = 0; y <= rows; y++)
            for (var x = 0; x <= columns; x++)
            {
                var u = x / (float)columns;
                var v = y / (float)rows;
                var at = Vector2.Lerp(Vector2.Lerp(Corners[0], Corners[3], u), Vector2.Lerp(Corners[1], Corners[2], u), v);
                var vertex = bottomLeft;
                vertex.position = new Vector3(rect.xMin + at.x * rect.width, rect.yMin + at.y * rect.height);
                vertex.uv0 = new Vector4(Mathf.Lerp(bottomLeft.uv0.x, topRight.uv0.x, u), Mathf.Lerp(bottomLeft.uv0.y, topRight.uv0.y, v));
                mesh.AddVert(vertex);
            }
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
            {
                var corner = y * (columns + 1) + x;
                mesh.AddTriangle(corner, corner + columns + 1, corner + columns + 2);
                mesh.AddTriangle(corner, corner + columns + 2, corner + 1);
            }
        }
    }

    internal sealed class DurakSeatLight : MonoBehaviour
    {
        private Image _light;
        public void Configure(Image light) { _light = light; Paint(); }
        private void Update() => Paint();
        private void Paint()
        {
            if (_light == null) return;
            var reduced = PlayerHubRuntime.Current?.Settings.ReducedMotion ?? false;
            _light.color = new Color(1, 1, 1, reduced ? .85f : .73f + .2f * Mathf.Sin(Time.unscaledTime * 3.2f));
        }
    }
}
