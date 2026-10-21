using System;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Почта таверны «Червовая дама» — переписка с друзьями (2026-10-10, решение автора): страницы `chats` и `chat:<id>`.
    /// Зал таверны темнеет, поверх — открытый письменный ящик (`UI/Hub/tavern-mail-case`; лист
    /// `screens/references/city/tavern-mail-sheet.png`, промпт — `tavern-mail-prompt.txt`, нарезка — `scripts/tavern/cut_mail.py`).
    /// Слева на коже — друзья бумажными карточками: лицо, имя, последняя строчка, новые письма — печать-сердце с числом.
    /// Справа на пергаменте — переписка облачками (друг слева, ты справа), готовые фразы, строка с пером и печать-сердце
    /// «Отправить». Выбор друга меняет страницу на месте, без истории: «НАЗАД», «Назад» и Esc выходят туда, откуда пришли
    /// (в зал таверны, к карточке друга). Открытая переписка — письма прочитаны (`chat_read`); непрочитанные видны письмами
    /// в почтовых ячейках зала (несколько или полные ячейки), на вкладке «Друзья» и в приветствии Дамы.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        // Ящик (пиксели картинки 1476×642): кожа слева — друзья, пергамент справа — переписка. На экране — во всю ширину
        // с полями по 30, но не выше 760 из 900: сверху заголовок и «НАЗАД».
        private const float MailCaseWidth = 1476, MailCaseHeight = 642, MailCaseMaxHeight = 760, MailCaseY = -35;
        private static readonly Rect MailLeather = Rect.MinMaxRect(84, 70, 526, 576);
        private static readonly Rect MailParchment = Rect.MinMaxRect(657, 73, 1378, 566);
        private static float MailRowHeight => MobileUi.Phone ? 104 : 90;
        private const float MailRowGap = 8, MailBubbleWidth = 470, MailPad = 24;
        private static readonly string[] MailPhrases = { "Сыграем?", "Хорошая игра!", "Реванш?" };
        private static readonly Color MailMine = new Color(1f, .87f, .83f), MailMuted = new Color(.45f, .32f, .2f),
            MailCream = new Color(1f, .91f, .74f);

        private RectTransform _mailLayer, _mailCase, _mailFriends, _mailFriendsView, _mailFriendsEmpty, _mailMessages, _mailMessagesView,
            _mailHeader, _mailWrite, _mailFirst, _mailNote;
        private CanvasGroup _mailGroup;
        private Image _mailBackdrop, _mailPortrait, _tavernMailFew, _tavernMailMany;
        private Text _mailName, _mailNoteText, _mailFirstText;
        private InputField _mailInput;
        private Button _mailProfile, _mailInvite, _mailNoteButton;
        private Action _mailNoteAction;
        private string _mailShownFriend;
        private int _mailShownCount = -1;
        private bool _mailEscapeHeld, _mailReading;
        private float _mailFade;
        private HubDragScrollbar _mailFriendsScrollbar;

        private static bool IsMailPage(string page) => page == "chats" || page.StartsWith("chat:", StringComparison.Ordinal);
        private bool MailShown => IsMailPage(_page) && _mailLayer != null && _mailLayer.gameObject.activeSelf;

        /// <summary>Друзья в почте: сначала те, с кем переписка свежее.</summary>
        private HubPerson[] MailFriends() => _state.People.Where(person => _state.Friends.Contains(person.Id))
            .OrderByDescending(person => MailLast(person.Id)?.CreatedAt ?? 0).ThenBy(person => person.Name).ToArray();

        private HubChatMessage MailLast(string id) => _state.Chats.LastOrDefault(message => message.ConversationId == id);

        private int MailUnread(string id) => _state.Chats.Count(message => message.ConversationId == id && message.Unread
            && message.FromId != _state.Profile.Id);

        /// <summary>Непрочитанные письма друзей: сколько и от кого (по порядку прихода).</summary>
        private (int Count, string[] From) MailUnreadAll()
        {
            if (_state == null) return (0, new string[0]);
            var letters = _state.Chats.Where(message => message.Unread && message.FromId != _state.Profile.Id
                && _state.Friends.Contains(message.ConversationId)).ToArray();
            return (letters.Length, letters.Select(message => message.ConversationId).Distinct().ToArray());
        }

        /// <summary>Чья переписка открыта: из страницы `chat:<id>`, а на `chats` — самая свежая.</summary>
        private string MailFriendId()
        {
            if (_state == null) return null;
            if (_page.StartsWith("chat:", StringComparison.Ordinal)) return _page.Substring(5);
            return MailFriends().FirstOrDefault()?.Id;
        }

        private void BuildTavernMail()
        {
            if (_mailLayer == null) BuildMailScene();
            if (!_mailLayer.gameObject.activeSelf)
            {
                // Подход к ячейкам: ящик проявляется поверх зала и чуть наезжает, как газета у киоска.
                _mailEscapeHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
                _mailFade = NewsReduced ? 1 : 0;
                _mailGroup.alpha = _mailFade;
                _mailShownFriend = null;
                _mailShownCount = -1;
                _mailInput.SetTextWithoutNotify("");
            }
            _mailLayer.gameObject.SetActive(true);
            _mailLayer.SetAsLastSibling();
            Title("Почта таверны", "Переписка с друзьями");
            PaintMail();
        }

        /// <summary>Другой друг — та же страница почты (без истории): «Назад» выходит из почты, а не листает друзей.</summary>
        private void SelectMailFriend(string id)
        {
            if (_busy || !MailShown) return;
            _page = "chat:" + id;
            Render();
        }

        private async Task SendMail(string text)
        {
            var id = MailFriendId();
            text = (text ?? "").Trim();
            if (_busy || !MailShown || id == null || text.Length == 0 || !_state.Friends.Contains(id)) return;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "chat_send", Target = id, Text = text });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); return; }
            _state = result.Snapshot;
            if (_page == "chats") _page = "chat:" + id;
            _mailInput.SetTextWithoutNotify("");
            Render();
        }

        private async Task ReadMail(string id)
        {
            if (_mailReading) return;
            _mailReading = true;
            try
            {
                var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "chat_read", Target = id });
                if (this == null) return;
                if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); return; }
                // Печати гаснут сразу, не дожидаясь общей перерисовки по PlayerHubRuntime.Changed.
                _state = result.Snapshot;
                RefreshMailPresentation();
                if (MailShown) PaintMail();
            }
            finally { _mailReading = false; }
        }

        /// <summary>Каждый кадр: проявление, Esc.</summary>
        private void UpdateTavernMail(float deltaTime, bool typing)
        {
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (MailShown && escape && !_mailEscapeHeld) Back();
            _mailEscapeHeld = escape;
            if (!MailShown) return;
            _mailFade = Mathf.MoveTowards(_mailFade, 1, deltaTime / (NewsReduced ? .01f : .3f));
            _mailGroup.alpha = Mathf.Clamp01(Mathf.SmoothStep(0, 1, _mailFade) * 1.4f);
            FitMail();
        }

        /// <summary>
        /// После каждой смены страницы или данных: почта уходит со своей страницей; письма в ячейках зала — по числу
        /// непрочитанных: нет — пусто, до <see cref="TavernMailMany"/> — несколько конвертов, больше — ячейки забиты.
        /// </summary>
        private void RefreshMailPresentation()
        {
            if (_mailLayer != null && !IsMailPage(_page) && _mailLayer.gameObject.activeSelf) _mailLayer.gameObject.SetActive(false);
            if (_tavernMailFew == null) return;
            var unread = MailUnreadAll().Count;
            var few = unread > 0 && unread < TavernMailMany;
            var many = unread >= TavernMailMany;
            if (_tavernMailFew.gameObject.activeSelf != few) _tavernMailFew.gameObject.SetActive(few);
            if (_tavernMailMany.gameObject.activeSelf != many) _tavernMailMany.gameObject.SetActive(many);
        }

        /// <summary>Зал — во весь экран (cover); ящик — по ширине экрана, не выше <see cref="MailCaseMaxHeight"/>.</summary>
        private void FitMail()
        {
            var viewport = FortuneViewport;
            var backdrop = _mailBackdrop.rectTransform;
            backdrop.localScale = Vector3.one * Mathf.Max(viewport.x / backdrop.sizeDelta.x, viewport.y / backdrop.sizeDelta.y);
            var scale = Mathf.Min((viewport.x - 60) / MailCaseWidth, (MobileUi.Phone ? 790 : MailCaseMaxHeight) / MailCaseHeight);
            _mailCase.localScale = Vector3.one * scale * Mathf.Lerp(.94f, 1, Mathf.SmoothStep(0, 1, _mailFade));
        }

        /// <summary>Печать-сердце с числом (новые письма); возвращает надпись числа.</summary>
        private Text MailSeal(Transform parent, string name, Vector2 at, float size, int fontSize)
        {
            var seal = LoanArt(parent, name, "tavern-mail-seal", at, Vector2.one * size, new Color(.7f, .08f, .07f));
            seal.preserveAspect = true;
            var count = LoanText(seal.transform, "Count", "", new Vector2(0, 2), Vector2.one * size, fontSize, Color.white, true);
            var shade = count.gameObject.AddComponent<HubOutline>();
            // Цифра поверх выпуклого сердца — тёмной обводкой, иначе сливается с воском.
            shade.effectColor = new Color(.22f, 0, 0, 1);
            shade.effectDistance = new Vector2(2.2f, -2.2f);
            return count;
        }

        /// <summary>Строка в одну линию: не влезает — обрезается с «…».</summary>
        private static void MailFitLine(Text label, string text)
        {
            label.text = text;
            var width = label.rectTransform.rect.width - 4;
            if (label.preferredWidth <= width) return;
            int low = 0, high = text.Length;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                label.text = text.Substring(0, mid).TrimEnd() + "…";
                if (label.preferredWidth <= width) low = mid;
                else high = mid - 1;
            }
            label.text = text.Substring(0, low).TrimEnd() + "…";
        }

        private static string MailTime(long unix)
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();
            var today = DateTimeOffset.FromUnixTimeSeconds(PlayerHubRuntime.Clock.UnixSeconds).ToLocalTime();
            return time.Date == today.Date ? time.ToString("HH:mm") : time.ToString("dd.MM");
        }

        private static void ClearChildren(RectTransform rect)
        {
            for (var i = rect.childCount - 1; i >= 0; i--)
            {
                var old = rect.GetChild(i).gameObject;
                old.SetActive(false);
                if (Application.isPlaying) Destroy(old);
                else DestroyImmediate(old);
            }
        }

        /// <summary>Листаемая область ящика: маска, прозрачная подложка под палец и содержимое, растущее вниз от верха.</summary>
        private RectTransform MailScroll(string name, Rect area, out RectTransform view)
        {
            Vector2 At(float x, float y) => new Vector2(x - MailCaseWidth / 2, MailCaseHeight / 2 - y);
            view = LoanRect(_mailCase, name + " view", At(area.center.x, area.center.y), area.size);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var content = LoanRect(view, name, Vector2.zero, new Vector2(area.width, 0));
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            var scroll = view.gameObject.AddComponent<HubSmoothScroll>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            scroll.WheelSmoothTime = .3f;
            return content;
        }

        private void BuildMailFriendsScrollbar()
        {
            var scroll = _mailFriendsView.GetComponent<HubSmoothScroll>();
            _mailFriends.sizeDelta -= new Vector2(24, 0);
            _mailFriends.anchoredPosition = new Vector2(-12, 0);
            var track = LoanArt(_mailFriendsView, "Friends scrollbar", null,
                new Vector2(_mailFriendsView.rect.width / 2 - 11, 0), new Vector2(22, _mailFriendsView.rect.height), Color.clear);
            track.raycastTarget = true;
            var rail = LoanArt(track.transform, "Rail", null, Vector2.zero, new Vector2(3, -8), new Color(.25f, .06f, .025f, .55f));
            rail.rectTransform.anchorMin = new Vector2(.5f, 0); rail.rectTransform.anchorMax = new Vector2(.5f, 1);
            var handle = LoanArt(track.transform, "Handle", null, Vector2.zero, Vector2.zero, Color.clear);
            var grip = LoanArt(handle.transform, "Grip", null, Vector2.zero, new Vector2(9, -6), SignGoldBottom);
            grip.rectTransform.anchorMin = new Vector2(.5f, 0); grip.rectTransform.anchorMax = new Vector2(.5f, 1);
            grip.sprite = UiTextures.RoundedRect(32, 64, 5, Color.white); grip.type = Image.Type.Sliced;
            _mailFriendsScrollbar = track.gameObject.AddComponent<HubDragScrollbar>();
            _mailFriendsScrollbar.direction = Scrollbar.Direction.BottomToTop;
            _mailFriendsScrollbar.handleRect = handle.rectTransform;
            _mailFriendsScrollbar.targetGraphic = grip;
            _mailFriendsScrollbar.ScrollView = scroll;
            scroll.verticalScrollbar = _mailFriendsScrollbar;
            // Иначе ScrollRect держит полосу включённой всегда — и при двух-трёх друзьях.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        /// <summary>
        /// Зал под окном таверны — притемнён в самой картинке (×0,26), а не отдельной тёмной вуалью поверх
        /// (2026-10-10, оптимизация: слой во весь экран дорог видеокарте телефона). На вид то же: вуаль 74 % почти чёрного
        /// цвета давала тот же зал ×0,26 и ещё 1–2 % в самых тёмных местах.
        /// </summary>
        private static void DimTavernBackdrop(Image backdrop)
        {
            if (backdrop.sprite != null) backdrop.color = new Color(.26f, .26f, .26f);
        }

        /// <summary>Прозрачная ловушка нажатий во весь экран вместо вуали: город под окном не листается, но и не рисуется.</summary>
        private void TavernBackdropCatcher(Transform layer, string name)
        {
            var catcher = LoanArt(layer, name, null, Vector2.zero, new Vector2(6000, 6000), Color.clear);
            catcher.raycastTarget = true;
            catcher.canvasRenderer.cullTransparentMesh = true;
        }

        private void BuildMailScene()
        {
            _mailLayer = StageLayer("Tavern mail");
            _mailGroup = _mailLayer.gameObject.AddComponent<CanvasGroup>();
            // Тот же зал таверны, притемнённый: почта выглядит одинаково, откуда бы её ни открыли.
            _mailBackdrop = LoanArt(_mailLayer, "Mail tavern hall", TavernHall, Vector2.zero, PictureSize(TavernHall), new Color(.12f, .06f, .03f));
            _mailBackdrop.raycastTarget = true;
            DimTavernBackdrop(_mailBackdrop);
            TavernBackdropCatcher(_mailLayer, "Mail shade");
            var title = LoanText(_mailLayer, "Mail title", "ПОЧТА ТАВЕРНЫ", new Vector2(0, 398), new Vector2(900, 60), 48, Color.white);
            var gold = title.gameObject.AddComponent<HubSignText>();
            gold.Top = SignGoldTop;
            gold.Bottom = SignGoldBottom;
            var titleShadow = title.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(.06f, .03f, 0, .85f);
            titleShadow.effectDistance = new Vector2(2, -3);

            var art = LoanArt(_mailLayer, "Mail case", "tavern-mail-case", new Vector2(0, MailCaseY), new Vector2(MailCaseWidth, MailCaseHeight),
                new Color(.3f, .16f, .07f));
            art.raycastTarget = true;
            _mailCase = art.rectTransform;
            Vector2 At(float x, float y) => new Vector2(x - MailCaseWidth / 2, MailCaseHeight / 2 - y);

            // Кожа: друзья (листаются), внизу — «Найти друзей».
            _mailFriends = MailScroll("Mail friends", Rect.MinMaxRect(MailLeather.xMin + 12, MailLeather.yMin + 14, MailLeather.xMax - 12,
                MailLeather.yMax - 74), out _mailFriendsView);
            BuildMailFriendsScrollbar();
            LoanPaperButton(_mailCase, "Найти друзей", At(MailLeather.center.x, MailLeather.yMax - 38), new Vector2(300, 50),
                () => { _hubFriendsTab = "people"; Navigate("friends"); });
            _mailFriendsEmpty = LoanRect(_mailCase, "Mail no friends", At(MailLeather.center.x, MailLeather.center.y - 30), new Vector2(380, 300));
            LoanArt(_mailFriendsEmpty, "Envelope", "tavern-mail-envelope", new Vector2(0, 50), new Vector2(200, 133), Color.clear).preserveAspect = true;
            LoanText(_mailFriendsEmpty, "Caption", "Друзей пока нет", new Vector2(0, -70), new Vector2(380, 50), 32, MailCream);

            // Пергамент: шапка с другом, переписка, готовые фразы, строка письма.
            var inner = MailParchment.width - 2 * MailPad;
            _mailHeader = LoanRect(_mailCase, "Mail header", At(MailParchment.center.x, MailParchment.yMin + 44), new Vector2(inner, 64));
            _mailPortrait = LoanArt(_mailHeader, "Portrait", null, new Vector2(-inner / 2 + 26, 0), new Vector2(52, 60), Color.clear);
            _mailPortrait.preserveAspect = true;
            _mailName = LoanText(_mailHeader, "Name", "", new Vector2(-inner / 2 + 62 + 210, 0), new Vector2(420, 52), 34, SixBubbleInk, true);
            _mailName.alignment = TextAnchor.MiddleLeft;
            _mailProfile = LoanPaperButton(_mailHeader, "Профиль", new Vector2(inner / 2 - 75, 0), new Vector2(150, 46),
                () => { var id = MailFriendId(); if (id != null) Navigate("friend:" + id); });
            _mailInvite = LoanPaperButton(_mailHeader, "Позвать в комнату", new Vector2(inner / 2 - 150 - 14 - 110, 0), new Vector2(220, 46),
                () => { var id = MailFriendId(); if (id != null) Send("invite_friend", id, _state.CurrentRoomId); });
            LoanArt(_mailHeader, "Rule", null, new Vector2(0, -38), new Vector2(inner, 2), new Color(SixBubbleInk.r, SixBubbleInk.g, SixBubbleInk.b, .4f));

            _mailMessages = MailScroll("Mail letters", Rect.MinMaxRect(MailParchment.xMin + 14, MailParchment.yMin + 88, MailParchment.xMax - 14,
                MailParchment.yMax - 108), out _mailMessagesView);
            _mailFirst = LoanRect(_mailMessagesView, "Mail first letter", Vector2.zero, new Vector2(inner, 240));
            LoanArt(_mailFirst, "Envelope", "tavern-mail-envelope", new Vector2(0, 40), new Vector2(150, 100), Color.clear).preserveAspect = true;
            _mailFirstText = LoanText(_mailFirst, "Caption", "", new Vector2(0, -60), new Vector2(inner - 40, 60), 28, MailMuted);

            _mailWrite = LoanRect(_mailCase, "Mail write", At(MailParchment.center.x, MailParchment.yMax - 58), new Vector2(inner, 104));
            for (var i = 0; i < MailPhrases.Length; i++)
            {
                var phrase = MailPhrases[i];
                LoanPaperButton(_mailWrite, phrase, new Vector2(-inner / 2 + 85 + i * 186, 26), new Vector2(170, MobileUi.Phone ? 48 : 40), () => _ = SendMail(phrase));
            }
            var quill = LoanArt(_mailWrite, "Quill", "tavern-mail-quill", new Vector2(-inner / 2 + 22, -26), new Vector2(46, 52), Color.clear);
            quill.preserveAspect = true;
            var field = LoanArt(_mailWrite, "Letter input", null, new Vector2(-12, -26), new Vector2(inner - 48 - 76 - 24, 48), new Color(1, .99f, .93f, .6f));
            field.sprite = UiTextures.RoundedRect(64, 64, 10, Color.white);
            field.type = Image.Type.Sliced;
            field.raycastTarget = true;
            LoanArt(field.transform, "Underline", null, new Vector2(0, -23), new Vector2(field.rectTransform.sizeDelta.x - 16, 2), SixBubbleInk);
            var fieldSize = field.rectTransform.sizeDelta - new Vector2(28, 4);
            var typed = LoanText(field.transform, "Text", "", Vector2.zero, fieldSize, 27, SixBubbleInk);
            typed.alignment = TextAnchor.MiddleLeft;
            typed.supportRichText = false;
            typed.rectTransform.sizeDelta = fieldSize;
            var hint = LoanText(field.transform, "Placeholder", "Напиши письмо…", Vector2.zero, fieldSize, 27, MailMuted);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.fontStyle = FontStyle.Italic;
            hint.rectTransform.sizeDelta = fieldSize;
            _mailInput = field.gameObject.AddComponent<InputField>();
            _mailInput.targetGraphic = field;
            _mailInput.textComponent = typed;
            _mailInput.placeholder = hint;
            _mailInput.characterLimit = 400;
            _mailInput.lineType = InputField.LineType.SingleLine;
            _mailInput.customCaretColor = true;
            _mailInput.caretColor = SixBubbleInk;
            _mailInput.selectionColor = new Color(.75f, .3f, .25f, .3f);
            _mailInput.onSubmit.AddListener(value => _ = SendMail(value));
            // Печать-сердце запечатывает письмо — отправить.
            var seal = LoanArt(_mailWrite, "Send seal", "tavern-mail-seal", new Vector2(inner / 2 - 36, -26), new Vector2(64, 64), new Color(.7f, .08f, .07f));
            seal.preserveAspect = true;
            seal.raycastTarget = true;
            var send = seal.gameObject.AddComponent<Button>();
            send.targetGraphic = seal;
            var sendColors = send.colors;
            sendColors.highlightedColor = new Color(1, .86f, .82f);
            sendColors.pressedColor = new Color(.75f, .6f, .6f);
            send.colors = sendColors;
            send.onClick.AddListener(() => _ = SendMail(_mailInput.text));
            seal.gameObject.AddComponent<HubPress>();

            // Записка Дамы вместо переписки: писать некому или вы ещё не друзья.
            _mailNote = LoanRect(_mailCase, "Mail note", At(MailParchment.center.x, MailParchment.center.y), new Vector2(inner, 400));
            _mailNoteText = LoanText(_mailNote, "Note", "", new Vector2(0, 60), new Vector2(inner - 60, 180), 32, SixBubbleInk);
            LoanText(_mailNote, "Signature", "— Дама червей", new Vector2(inner / 2 - 170, -45), new Vector2(300, 44), 28, MailMuted).fontStyle = FontStyle.Italic;
            _mailNoteButton = LoanPaperButton(_mailNote, "Найти друзей", new Vector2(0, -125), new Vector2(300, 54), () => _mailNoteAction?.Invoke(), true);

            var back = (RectTransform)CityButton(_mailLayer, "НАЗАД", 24, 24, 170, 56, Back).transform;
            PinToCorner(back, -1);
            FitMail();
            _mailLayer.gameObject.SetActive(false);
        }

        private void PaintMail()
        {
            if (_mailLayer == null || _state == null) return;
            FitMail();
            var friends = MailFriends();
            var id = MailFriendId();
            var person = id == null ? null : _state.People.FirstOrDefault(entry => entry.Id == id);
            var friend = id != null && _state.Friends.Contains(id);
            PaintMailFriends(friends, id);

            _mailHeader.gameObject.SetActive(person != null);
            _mailWrite.gameObject.SetActive(friend);
            _mailMessagesView.gameObject.SetActive(friend);
            _mailNote.gameObject.SetActive(!friend);
            if (person != null)
            {
                _mailPortrait.sprite = AvatarCatalog.Find(person.AvatarId);
                _mailPortrait.color = _mailPortrait.sprite == null ? Color.clear : Color.white;
                _mailInvite.gameObject.SetActive(friend && !string.IsNullOrEmpty(_state.CurrentRoomId));
                // Имя — до кнопок справа: с «Позвать в комнату» места меньше.
                var nameWidth = _mailInvite.gameObject.activeSelf ? 205f : 420f;
                _mailName.rectTransform.sizeDelta = new Vector2(nameWidth, _mailName.rectTransform.sizeDelta.y);
                _mailName.rectTransform.anchoredPosition = new Vector2(-(MailParchment.width - 2 * MailPad) / 2 + 62 + nameWidth / 2, 0);
                MailFitLine(_mailName, person.Name);
            }
            if (!friend)
            {
                ClearChildren(_mailMessages);
                var stranger = person != null;
                _mailNoteText.text = (stranger
                    ? "С " + person.Name + " вы пока не друзья. Добавь друг друга — и пиши сколько хочешь!"
                    : "Писать пока некому. Садись за любой стол — там найдёшь друзей, а письма от них я сложу сюда.").Replace(" —", " —");
                _mailNoteButton.GetComponentInChildren<Text>().text = stranger ? "Профиль" : "Найти друзей";
                _mailNoteAction = stranger ? (Action)(() => Navigate("friend:" + id)) : () => { _hubFriendsTab = "people"; Navigate("friends"); };
                _mailShownFriend = null;
                return;
            }
            PaintMailLetters(id, person);
            if (MailUnread(id) > 0) _ = ReadMail(id);
        }

        private void PaintMailFriends(HubPerson[] friends, string selected)
        {
            ClearChildren(_mailFriends);
            _mailFriendsEmpty.gameObject.SetActive(friends.Length == 0);
            var width = _mailFriends.sizeDelta.x - 12;
            var y = 0f;
            foreach (var person in friends)
            {
                var on = person.Id == selected;
                var paper = LoanArt(_mailFriends, "Letter " + person.Id, null, Vector2.zero, new Vector2(width, MailRowHeight),
                    on ? SixBubblePaper : new Color(.9f, .82f, .66f));
                var row = paper.rectTransform;
                row.anchorMin = row.anchorMax = new Vector2(.5f, 1);
                // Открытая карточка чуть выдвинута к пергаменту и обведена золотом.
                row.anchoredPosition = new Vector2(on ? 6 : -6, -y - MailRowHeight / 2);
                paper.sprite = UiTextures.RoundedRect(64, 64, 14, Color.white);
                paper.type = Image.Type.Sliced;
                paper.raycastTarget = true;
                var rim = paper.gameObject.AddComponent<HubOutline>();
                rim.effectColor = on ? SignGoldBottom : new Color(SixBubbleInk.r, SixBubbleInk.g, SixBubbleInk.b, .55f);
                rim.effectDistance = on ? new Vector2(3, -3) : new Vector2(1.5f, -1.5f);
                var button = paper.gameObject.AddComponent<Button>();
                button.targetGraphic = paper;
                var id = person.Id;
                button.onClick.AddListener(() => SelectMailFriend(id));

                var face = LoanArt(row, "Portrait", null, new Vector2(-width / 2 + 40, 0), new Vector2(62, 72), Color.clear);
                face.sprite = AvatarCatalog.Find(person.AvatarId);
                face.color = face.sprite == null ? Color.clear : Color.white;
                face.preserveAspect = true;
                var unread = MailUnread(person.Id);
                var textWidth = width - 84 - (unread > 0 ? 66 : 16);
                var textX = -width / 2 + 80 + textWidth / 2;
                var name = LoanText(row, "Name", "", new Vector2(textX, 22), new Vector2(textWidth, 46), MobileUi.Phone ? 34 : 30, SixBubbleInk, true);
                name.alignment = TextAnchor.MiddleLeft;
                MailFitLine(name, person.Name);
                var last = MailLast(person.Id);
                var preview = LoanText(row, "Last line", "", new Vector2(textX, -22), new Vector2(textWidth, 42), MobileUi.Phone ? 28 : 22, MailMuted);
                preview.alignment = TextAnchor.MiddleLeft;
                MailFitLine(preview, last == null ? "Начать переписку"
                    : (last.FromId == _state.Profile.Id ? "Ты: " : "") + last.Text.Replace('\n', ' ').Replace('\r', ' '));
                if (unread > 0) MailSeal(row, "Unread seal", new Vector2(width / 2 - 36, 0), 56, 33).text = unread.ToString();
                y += MailRowHeight + MailRowGap;
            }
            _mailFriends.sizeDelta = new Vector2(_mailFriends.sizeDelta.x, Mathf.Max(0, y - MailRowGap));
            var limit = Mathf.Max(0, _mailFriends.rect.height - _mailFriendsView.rect.height);
            _mailFriends.anchoredPosition = new Vector2(-12, Mathf.Clamp(_mailFriends.anchoredPosition.y, 0, limit));
            // Ползунок — в согласии со списком: его значение по умолчанию (0 — низ) уводило список в конец при открытии.
            _mailFriendsScrollbar.SetValueWithoutNotify(limit > 1 ? 1 - _mailFriends.anchoredPosition.y / limit : 1);
            _mailFriendsScrollbar.gameObject.SetActive(limit > 1);
        }

        private void PaintMailLetters(string id, HubPerson person)
        {
            ClearChildren(_mailMessages);
            var me = _state.Profile.Id;
            var letters = _state.Chats.Where(message => message.ConversationId == id).ToArray();
            var width = _mailMessages.sizeDelta.x;
            _mailFirst.gameObject.SetActive(letters.Length == 0);
            _mailFirstText.text = "Напиши первое письмо — " + (person?.Name ?? "друг") + " ответит.";
            var y = 10f;
            foreach (var letter in letters)
            {
                var mine = letter.FromId == me;
                var bubble = LoanArt(_mailMessages, "Letter " + letter.Id, null, Vector2.zero, Vector2.one, mine ? MailMine : SixBubblePaper);
                bubble.sprite = UiTextures.RoundedRect(64, 64, 18, Color.white);
                bubble.type = Image.Type.Sliced;
                var rim = bubble.gameObject.AddComponent<HubOutline>();
                rim.effectColor = SixBubbleInk;
                rim.effectDistance = new Vector2(2, -2);
                var text = LoanText(bubble.transform, "Text", letter.Text.Replace(" —", " —"), Vector2.zero, new Vector2(MailBubbleWidth - 32, 10), MobileUi.Phone ? 34 : 27, SixBubbleInk);
                text.alignment = TextAnchor.MiddleLeft;
                // Ширина — по самой длинной строке, не шире облачка; высота — сколько строк вышло.
                var textWidth = Mathf.Min(MailBubbleWidth - 32, Mathf.Ceil(text.preferredWidth) + 4);
                text.rectTransform.sizeDelta = new Vector2(textWidth, 10);
                var textHeight = Mathf.Max(34, Mathf.Ceil(text.preferredHeight) + 2);
                text.rectTransform.sizeDelta = new Vector2(textWidth, textHeight);
                var size = new Vector2(textWidth + 32, textHeight + 18);
                var rect = bubble.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
                rect.sizeDelta = size;
                var x = mine ? width / 2 - 14 - size.x / 2 : -width / 2 + 14 + size.x / 2;
                rect.anchoredPosition = new Vector2(x, -y - size.y / 2);
                var time = LoanText(_mailMessages, "Time", MailTime(letter.CreatedAt), Vector2.zero, new Vector2(70, 36), MobileUi.Phone ? 23 : 19, MailMuted);
                time.rectTransform.anchorMin = time.rectTransform.anchorMax = new Vector2(.5f, 1);
                time.rectTransform.anchoredPosition = new Vector2(x + (mine ? -1 : 1) * (size.x / 2 + 42), -y - size.y + 16);
                y += size.y + 12;
            }
            _mailMessages.sizeDelta = new Vector2(width, y);
            // Новая переписка или новое письмо — к последнему письму.
            if (_mailShownFriend != id || _mailShownCount != letters.Length)
                _mailMessages.anchoredPosition = new Vector2(0, Mathf.Max(0, y - _mailMessagesView.sizeDelta.y));
            _mailShownFriend = id;
            _mailShownCount = letters.Length;
        }
    }
}
