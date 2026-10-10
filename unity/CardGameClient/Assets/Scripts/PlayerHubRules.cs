using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Правила игр клуба (2026-09-30): страница «rules:&lt;id&gt;» открывается знаком «?» — у игры в списке
    /// и в углу таблички у стола. Слева превью, коротко о столе и «Выбрать игру» (или «Вернуться к столу»),
    /// справа — лист правил, который листается. Превью — «UI/Hub/Previews/&lt;id&gt;» (16:9); пока картинки
    /// нет, вместо неё сукно в цвете игры с мастью.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private static readonly string[] HubGames = { "durak", "bluff", "chests", "govno", "poker" };
        // На доске Клуба только его столы; покер открывается через казино.
        private static readonly string[] ClubGames = HubGames.Where(game => game != "poker").ToArray();
        private static readonly Dictionary<string, Sprite> s_gamePreviews = new Dictionary<string, Sprite>();
        // Где был список или лист правил, когда страницу перестроили или к ней вернулись «Назад».
        private readonly Dictionary<string, float> _scrollMemory = new Dictionary<string, float>();

        private static bool IsRulesPage(string page) => page.StartsWith("rules:", System.StringComparison.Ordinal);

        /// <summary>Правила игры по разделам: заголовок и текст; «—» в начале строки — пункт списка.</summary>
        private static (string Heading, string Text)[] GameRules(string game)
        {
            switch (game)
            {
                case "durak":
                    return new[]
                    {
                        ("Цель", "Избавься от всех карт. Последний, у кого они остались, — дурак."),
                        ("Раздача", "Каждому по 6 карт. Нижняя карта колоды лежит открытой — её масть козырная. Первым ходит тот, у кого самый младший козырь."),
                        ("Атака и защита", "Атакующий кладёт карту, защищающийся её бьёт: старшей картой той же масти или любым козырем. Козырь бьют только старшим козырем.\nВ колоде на 54 карты есть джокеры: джокер бьёт любую карту, а сам бьётся только другим джокером."),
                        ("Подкидывание", "Остальные подкидывают карты тех достоинств, что уже лежат на столе. За раздачу — не больше 6 атакующих карт и не больше, чем карт у защищающегося.\n— Отбился — «Бито»: карты уходят в сброс, следующим атакует защищавшийся.\n— Не отбиться — «Забрать»: берёшь всё со стола, а атакует игрок после тебя."),
                        ("Добор", "После раздачи все добирают из колоды до 6 карт: первым — атаковавший, последним — защищавшийся."),
                        ("Конец партии", "Кто остался без карт, когда колода кончилась, выходит из игры. Последний с картами — дурак. Если последние карты ушли одновременно — ничья."),
                        ("Переводной", "Пока ни одна карта не отбита, защищающийся может положить карту того же достоинства — атака уходит следующему, и отбиваться теперь ему."),
                        ("Подкидывают соседи", "Подкидывают не все, а только атакующий и игрок сразу после защищающегося."),
                        ("Шулерство", "Можно сыграть не по правилам — например, побить младшей картой. Заметил обман — нажми на эту карту, пока не сделан следующий ход.\n— Поймал — шулер забирает карту, тянет штрафную и до конца раздачи играет честно.\n— Ошибся — штрафная тебе, и до конца раздачи обвинять нельзя."),
                    };
                case "bluff":
                    return new[]
                    {
                        ("Цель", "Избавься от карт. Проигрывает тот, у кого в конце на руках больше всех тузов."),
                        ("Раздача", "Вся колода раздаётся поровну, джокеров нет."),
                        ("Круг", "Ведущий кладёт рубашкой вверх от 1 до 3 карт и называет ранг: «три дамы». Врать можно только про ранг — сколько карт легло, видят все.\nТузами заявляться нельзя: туз уходит с руки только враньём."),
                        ("Верю или проверю", "Следующий по кругу подкладывает свои 1–3 карты под тот же ранг или проверяет последний пакет.\n— Хоть одна карта не того ранга — соврал: весь котёл забирает тот, кто клал.\n— Всё честно — котёл забирает проверявший.\nНовый круг начинает игрок после того, кто забрал котёл."),
                        ("Каре", "Четыре карты одного ранга сразу уходят из игры. Из тузов каре не бывает."),
                        ("Конец партии", "Скинул все карты — вышел, но если придётся забрать котёл, вернёшься за стол. Партия кончается, когда карты остались у одного игрока или у всех на руках одни тузы. У кого больше тузов — проиграл, поровну — ничья.\nКогда на руках одни тузы и их у тебя не меньше, чем у других, можно сдаться."),
                        ("Перестрелка", "Раз в 2–3 круга ведущему предлагают объявить перестрелку. Правила те же, но кто решится проверить, тот два круга ходит петухом."),
                    };
                case "chests":
                    return new[]
                    {
                        ("Цель", "Собери больше всех сундучков. Сундучок — четыре карты одного ранга, всех четырёх мастей."),
                        ("Раздача", "Вся колода раздаётся, добора нет. Играют от трёх человек."),
                        ("Ход", "1. Выбери соперника и спроси ранг, который есть у тебя: «Вальты есть?»\n2. Есть — угадай, сколько их у него.\n3. Угадал число — назови их масти.\nУгадал всё — забираешь карты и спрашиваешь снова. Ошибся на любом шаге или у соперника нет такого ранга — ход переходит следующему по кругу."),
                        ("Сундучок", "Как только четыре карты ранга собрались у одного игрока, они сами уходят в его сундучок."),
                        ("Пустая рука", "Карты кончились — ты не выбываешь: в свой ход спрашиваешь любой ранг, который ещё не в сундучках."),
                        ("Хитрость", "Соврать о своей руке нельзя — за тебя отвечает игра. Зато можно нарочно ошибиться в догадке и назвать свою масть, чтобы запутать тех, кто запоминает вопросы."),
                        ("Конец партии", "Партия идёт, пока все карты не лягут в сундучки. У кого их больше всех — победил, поровну у всех — ничья."),
                    };
                case "govno":
                    return new[]
                    {
                        ("Цель", "Собери каре — четыре карты одного ранга — и первым хлопни по центру стола. Кто хлопнет последним, теряет жизнь."),
                        ("Раздача", "От 3 до 6 игроков, у каждого по 4 карты. Рангов в колоде столько, сколько игроков: втроём — тузы, короли и дамы, вчетвером добавляются вальты, дальше десятки и девятки."),
                        ("Обмен", "Все одновременно выбирают по карте и отдают соседу слева — коротким нажатием на карту. От соседа справа приходит закрытая карта, в руку она попадает, когда передадут все.\nНа выбор — 2,5 секунды, в «Хаосе» — 1,5, и после обмена карты в руке перемешиваются."),
                        ("Каре и хлопок", "Собрал каре — нажми на центр стола. Остальные хлопают по центру следом, собирать ничего не нужно. Последний теряет жизнь. Хлопнул раньше времени — рука блокируется на секунду."),
                        ("Опоздания", "Не успел выбрать — карта уйдёт сама, это предупреждение. Второе опоздание в раунде — минус жизнь."),
                        ("Жизни", "У каждого 4 жизни: по колено, по пояс, по шею — и утонул. Утонувший выбывает, остальные играют дальше."),
                        ("Конец партии", "Когда за столом остаются двое, побеждают оба."),
                    };
                default:
                    return new[]
                    {
                        ("Цель", "Забери фишки соперников. Банк достаётся лучшей комбинации или тому, кто остался последним."),
                        ("Раздача", "Каждому по 2 закрытые карты, на стол по очереди ложатся 5 общих: флоп — три, тёрн — одна, ривер — одна. Перед каждой стадией — круг ставок."),
                        ("Блайнды и фишки", "У каждого 2 000 фишек. Двое после дилера ставят обязательные ставки: малый блайнд 10 и большой 20."),
                        ("Действия", "— Фолд — сбросить карты.\n— Чек — пропустить, если доплачивать не нужно.\n— Колл — уравнять ставку.\n— Ставка и рейз — поднять.\n— Олл-ин — поставить всё."),
                        ("Вскрытие", "Из своих двух и пяти общих карт собирается лучшая пятёрка. Равные комбинации делят банк."),
                        ("Комбинации", "От старшей к младшей:\n— Роял-флеш — туз, король, дама, вальт и десятка одной масти.\n— Стрит-флеш — пять подряд одной масти.\n— Каре — четыре карты одного ранга.\n— Фулл-хаус — тройка и пара.\n— Флеш — пять одной масти, не подряд.\n— Стрит — пять подряд, разных мастей.\n— Тройка — три карты одного ранга.\n— Две пары.\n— Пара — две карты одного ранга.\n— Старшая карта — если нет ничего другого."),
                        ("Конец партии", "Кончились фишки — выбываешь из-за стола."),
                    };
            }
        }

        private static int MostPlayers(string game)
        {
            var decks = HubGameRules.DeckSizes(game);
            return decks.Select(deck =>
            {
                var settings = HubGameSettings.DefaultFor(game);
                settings.DeckSize = deck;
                return HubGameRules.MaximumPlayers(game, settings);
            }).DefaultIfEmpty(HubGameRules.MaximumPlayers(game, HubGameSettings.DefaultFor(game))).Max();
        }

        private static string PlayersRange(string game) => "От " + HubGameRules.MinimumPlayers(game) + " до " + MostPlayers(game) + " игроков";

        private static string DeckText(string game)
        {
            var decks = HubGameRules.DeckSizes(game).Where(deck => deck > 0).ToArray();
            if (decks.Length == 0) return "По 4 карты на игрока";
            var list = decks.Length == 1 ? decks[0].ToString()
                : string.Join(", ", decks.Take(decks.Length - 1)) + " или " + decks[decks.Length - 1];
            // Слово — по последнему числу: «24, 36, 52 или 54 карты».
            return list + " " + Numbered(decks[decks.Length - 1], "карта", "карты", "карт").Split(' ')[1];
        }

        /// <summary>
        /// Превью игры в золотой рамке: картинка обрезается под рамку, пока её нет — сукно в цвете игры
        /// с мастью и подписью «превью».
        /// </summary>
        private void GamePreview(Transform parent, string game, float x, float y, float w, float h)
        {
            var frame = MakeRect(parent, "Preview frame", x - 6, y - 6, w + 12, h + 12).gameObject.AddComponent<Image>();
            SetPlate(frame, true, 8);
            frame.raycastTarget = false;
            var window = MakeRect(parent, "Preview", x, y, w, h);
            window.gameObject.AddComponent<RectMask2D>();
            var cloth = window.gameObject.AddComponent<Image>();
            var accent = Accent(game);
            cloth.color = new Color(accent.r * .32f, accent.g * .32f, accent.b * .32f);
            cloth.raycastTarget = false;
            // Пустое значение не держим: картинка могла появиться после прошлого Play (домен не перезагружается).
            if (!s_gamePreviews.TryGetValue(game, out var sprite) || sprite == null)
                s_gamePreviews[game] = sprite = Resources.Load<Sprite>("UI/Hub/Previews/" + game);
            if (sprite != null)
            {
                var picture = MakeRect(window, "Preview picture", 0, 0, w, h);
                picture.pivot = picture.anchorMin = picture.anchorMax = new Vector2(.5f, .5f);
                picture.anchoredPosition = Vector2.zero;
                var image = picture.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.raycastTarget = false;
                // Как «cover»: картинка заполняет рамку целиком, лишнее по краям срезает маска.
                var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                return;
            }
            var glow = MakeRect(window, "Felt light", -w * .15f, -h * .35f, w * 1.3f, h * 1.5f).gameObject.AddComponent<Image>();
            glow.sprite = UiTextures.RadialGlow(256, Color.white);
            glow.color = new Color(accent.r * .75f, accent.g * .75f, accent.b * .75f, .8f);
            glow.raycastTarget = false;
            var suit = MakeLabel(window, GameSuit(game), 0, 0, w, h * .82f, Mathf.RoundToInt(h * .5f));
            suit.alignment = TextAnchor.MiddleCenter;
            suit.color = new Color(1, .93f, .78f, .28f);
            var note = MakeLabel(window, "ПРЕВЬЮ", 0, h - 40, w, 32, 17);
            note.font = ClubFont();
            note.color = new Color(ClubMuted.r, ClubMuted.g, ClubMuted.b, .8f);
            note.alignment = TextAnchor.MiddleCenter;
        }

        /// <summary>
        /// Листаемая область клуба: всё, что ниже края, срезается и листается пальцем или колесом. Где
        /// остановились, помнится по <paramref name="key"/>; тонкая золотая полоска справа показывает место.
        /// </summary>
        private RectTransform ClubScroll(string key, float x, float y, float w, float h, float height, float thumbX)
        {
            var viewport = MakeRect(_content, "Scroll " + key, x, y, w, h);
            // Прозрачная подложка ловит перетаскивание и между досками.
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = MakeRect(viewport, "Scroll content", 0, 0, w, Mathf.Max(h, height));
            var scroll = viewport.gameObject.AddComponent<HubSmoothScroll>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .15f;
            // Отпущенный пальцем список катится и плавно замедляется, а не встаёт сразу.
            scroll.decelerationRate = .3f;
            scroll.scrollSensitivity = 36;
            var limit = Mathf.Max(0, height - h);
            _scrollMemory.TryGetValue(key, out var saved);
            content.anchoredPosition = new Vector2(0, Mathf.Clamp(saved, 0, limit));
            if (limit <= 0) return content;
            var track = MakeRect(_content, "Scroll track", thumbX, y + 10, 4, h - 20);
            var line = track.gameObject.AddComponent<Image>();
            line.sprite = UiTextures.RoundedRect(8, 32, 4, Color.white);
            line.type = Image.Type.Sliced;
            line.color = new Color(0, 0, 0, .4f);
            line.raycastTarget = false;
            var length = Mathf.Max(48, (h - 20) * h / height);
            var thumb = MakeRect(track, "Scroll thumb", 0, 0, 4, length);
            var bar = thumb.gameObject.AddComponent<Image>();
            bar.sprite = line.sprite;
            bar.type = Image.Type.Sliced;
            bar.color = ClubGold;
            bar.raycastTarget = false;
            void Place()
            {
                var t = Mathf.Clamp01(content.anchoredPosition.y / limit);
                thumb.anchoredPosition = new Vector2(0, -t * (h - 20 - length));
            }
            Place();
            scroll.onValueChanged.AddListener(_ => Place());
            return content;
        }

        /// <summary>Перед перестройкой страницы — запомнить, где стоят её листаемые области.</summary>
        private void RememberScrolls()
        {
            foreach (var scroll in _content.GetComponentsInChildren<ScrollRect>())
                if (scroll.name.StartsWith("Scroll ", System.StringComparison.Ordinal))
                    _scrollMemory[scroll.name.Substring(7)] = scroll.content.anchoredPosition.y;
        }

        private const float GameRowHeight = 244, GameRowGap = 20, GameRowWidth = 1152;

        /// <summary>Список игр — доски одна под другой: превью, название, коротко о столе, «Выбрать игру» и «?».</summary>
        private void BuildClubGames()
        {
            Title("Игры", "Выбери игру — подойдёшь к её столу · «?» — правила");
            var height = ClubGames.Length * GameRowHeight + (ClubGames.Length - 1) * GameRowGap + 8;
            var list = ClubScroll("games", 0, 0, 1180, 660, height, 1168);
            for (var i = 0; i < ClubGames.Length; i++) GameRow(list, ClubGames[i], 4 + i * (GameRowHeight + GameRowGap));
        }

        private void GameRow(Transform list, string game, float y)
        {
            const float w = GameRowWidth, h = GameRowHeight, text = 414;
            var row = MakeRect(list, "Game row " + game, 0, y, w, h);
            var board = row.gameObject.AddComponent<Image>();
            SetPlate(board, false, 22);
            board.raycastTarget = false;
            var lift = row.gameObject.AddComponent<Shadow>();
            lift.effectDistance = new Vector2(2, -4);
            lift.effectColor = new Color(0, 0, 0, .55f);
            GamePreview(row, game, 28, 22, 356, 200);
            var title = MakeLabel(row, GameTitle(game), text, 18, w - text - 120, 62, 42);
            title.font = ClubTitleFont();
            title.color = ClubGold;
            title.alignment = TextAnchor.MiddleLeft;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 24;
            title.resizeTextMaxSize = 42;
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(.06f, .03f, 0, .85f);
            var blurb = MakeLabel(row, GameBlurb(game), text, 84, w - text - 40, 36, 22);
            blurb.font = ClubFont();
            blurb.color = ClubCream;
            blurb.resizeTextForBestFit = true;
            blurb.resizeTextMinSize = 14;
            blurb.resizeTextMaxSize = 22;
            var facts = MakeLabel(row, TableWhere(TablePlace(game)) + " · " + PlayersRange(game).ToLowerInvariant() + " · " + DeckText(game).ToLowerInvariant(),
                text, 124, w - text - 40, 30, 18);
            facts.font = ClubFont();
            facts.color = ClubMuted;
            facts.resizeTextForBestFit = true;
            facts.resizeTextMinSize = 12;
            facts.resizeTextMaxSize = 18;
            ClubButton(row, "Выбрать игру", text, h - 22 - 64, 320, 64, () => Navigate("game:" + game), ClubTone.Primary, 28);
            ClubButton(row, "?", w - 28 - 64, 22, 64, 64, () => Navigate("rules:" + game), ClubTone.Normal, 38);
        }

        /// <summary>
        /// Правила игры: слева превью, коротко о столе и выбор игры, справа — лист правил. Открыты от стола —
        /// кнопка ведёт обратно к нему, из списка — к столу этой игры.
        /// </summary>
        private void BuildGameRules(string game)
        {
            if (!HubGames.Contains(game))
            {
                _page = "games";
                BuildClubGames();
                return;
            }
            Title(GameTitle(game), "Правила игры");
            GamePreview(_content, game, 6, 6, 348, 196);
            var facts = ClubBoard(_content, "Rules facts", 0, 222, 360, 344, 20);
            facts.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -4);
            var lines = new[] { ("Игроки", PlayersRange(game)), ("Колода", DeckText(game)), ("Где", TableWhere(TablePlace(game))) };
            for (var i = 0; i < lines.Length; i++)
            {
                var top = 250 + i * 100;
                var key = MakeLabel(_content, lines[i].Item1.ToUpperInvariant(), 30, top, 300, 26, 16);
                key.font = ClubFont();
                key.color = ClubMuted;
                var value = MakeLabel(_content, lines[i].Item2, 30, top + 28, 300, 44, 24);
                value.font = ClubFont();
                value.color = ClubCream;
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 14;
                value.resizeTextMaxSize = 24;
            }
            var fromTable = _history.Count > 0 && _history[_history.Count - 1] == "game:" + game;
            if (fromTable) ClubButton(_content, "Вернуться к столу", 0, 588, 360, 72, Back, ClubTone.Primary, 28);
            else ClubButton(_content, "Выбрать игру", 0, 588, 360, 72, () => Navigate("game:" + game), ClubTone.Primary, 28);

            var sheet = ClubBoard(_content, "Rules sheet", 384, 0, 796, 660, 22);
            sheet.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -4);
            const float inner = 716;
            // Сначала раскладываем текст, чтобы знать высоту листа, потом переносим его в листаемую область.
            var sections = GameRules(game);
            var items = new List<(Text Label, float Top)>();
            var holder = MakeRect(_content, "Rules text", 0, 0, inner, 10);
            var y = 14f;
            foreach (var (heading, body) in sections)
            {
                var head = MakeLabel(holder, heading, 0, y, inner, 40, 27);
                head.font = ClubFont();
                head.color = ClubGold;
                head.gameObject.AddComponent<Shadow>().effectColor = new Color(.06f, .03f, 0, .85f);
                items.Add((head, y));
                y += 42;
                // Тире держится за слово перед ним: в начале строки его не спутать с пунктом списка.
                var text = MakeLabel(holder, body.Replace(" — ", " — "), 0, y, inner, 30, 20);
                text.font = ClubFont();
                text.color = ClubCream;
                text.lineSpacing = .92f;
                var height = text.preferredHeight + 4;
                text.rectTransform.sizeDelta = new Vector2(inner, height);
                items.Add((text, y));
                y += height + 22;
            }
            var content = ClubScroll("rules:" + game, 424, 24, inner + 8, 612, y, 1152);
            foreach (var (label, top) in items)
            {
                label.rectTransform.SetParent(content, false);
                label.rectTransform.anchoredPosition = new Vector2(6, -top);
            }
            // Пустая заготовка уходит вместе со страницей при следующей перестройке.
            holder.gameObject.SetActive(false);
        }
    }
}
