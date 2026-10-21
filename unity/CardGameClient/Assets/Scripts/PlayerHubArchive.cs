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
    /// Архив изнутри (2026-10-18, IDEAS.md «Архив — память города») — этаж <see cref="ArchiveLevel"/>, картинка
    /// `UI/Hub/archive-hall` (2196×941, 21:9; исходник `screens/references/city/archive-hall-v2-wide.png`). Вход — нырок
    /// в дверь бывшего полицейского поста в Старом городе, как в Лавку и банк. В зале три вещи и житель: витрина слева —
    /// достижения экспонатами (неполученные — под тканью, награду забирают тут же), летопись на пюпитре — главы истории
    /// города по уровню (HubChronicle), картотека справа — «Личное дело» игрока, Туз червей за столом — реплики и
    /// подсказка, какая глава следующая. Витрина, книга и досье открываются крупно поверх зала; «Назад», Esc и
    /// «ЗАКРЫТЬ» сначала закрывают их. Экспонаты пока заглушки (медаль и ткань), настоящий арт — потом.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string ArchiveHall = "archive-hall";
        private const int ArchiveLevel = 8;
        private const string ArchiveAceId = "archive-ace", ArchiveBookId = "archive-book", ArchiveCabinetId = "archive-cabinet",
            ArchiveDrawersId = "archive-drawers";
        // Облачко Туза — над ним, на стене картотеки; хвостик к голове. Пиксели игровой картинки 2196×941.
        private const float ArchiveBubbleX = 1520, ArchiveBubbleY = 235, ArchiveBubbleWidth = 440, ArchiveBubbleHeight = 112,
            ArchiveAceHeadX = 1600;
        // Книга поверх зала: лист `archive-book` 1335×753 шириной 1240; середины страниц и поле текста — в пикселях сцены.
        private const float ArchiveBookWidth = 1240, ArchiveBookY = 20;
        private static readonly Vector2 ArchiveLeftPage = new Vector2(-248, 96), ArchiveRightPage = new Vector2(244, 96);
        private const int ArchiveShelfColumns = 5, ArchiveShelfRows = 2;
        private static readonly Color ArchiveGold = new Color(.93f, .78f, .45f), ArchiveVelvet = new Color(.27f, .05f, .06f),
            ArchiveWood = new Color(.2f, .1f, .05f), ArchiveGlassText = new Color(1f, .93f, .82f);
        private static readonly string[] ArchiveRoman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        private static readonly string[] ArchiveMonths = { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа",
            "сентября", "октября", "ноября", "декабря" };

        private static readonly string[] ArchiveTalk =
        {
            "Пыль не трогай — она тоже история.",
            "Этот город построили четыре туза. Я — последний, кто остался на своём месте.",
            "Камеру оставили с полицейских времён. Теперь в ней сидят только бумаги.",
            "В ящике с твоим именем — всё, что город о тебе знает. Загляни.",
            "Тузяра сюда не заходит. Не любит, когда помнят, как было.",
            "Шестёрка? Хороший малый. Много знает — для шестёрки…",
        };

        private RectTransform _archiveBubble, _archiveLayer, _archiveBook, _archiveShelf, _archiveFile;
        private RectTransform _archiveLeft, _archiveRight, _archiveExhibits, _archiveRows;
        private Text _archiveBubbleText, _archiveShelfCount;
        private Button _archivePrev, _archiveNext;
        // Что открыто поверх зала: null, "book", "cabinet" или "file".
        private string _archiveOpen, _archiveLine;
        private int _archiveSpread, _archiveShelfPage, _archiveTalk;
        private bool _archiveGreeted, _archiveEscapeHeld, _archiveArrowHeld;
        private HubSnapshot _archivePaintedFor;

        private bool ArchiveInside => _cityLevel == ArchiveLevel && _page == "home" && _diveTarget < 0 && _visitPhase == 0;
        /// <summary>Открыта книга, витрина или досье: под ними зал не слушает клавиши, «ВЫЙТИ» и карта спрятаны.</summary>
        private bool ArchiveShown => _archiveOpen != null && _cityLevel == ArchiveLevel && _page == "home";

        // Методы, а не поля: из них собираются места в CityPlaces при инициализации класса (см. PlayerHubShop.cs).
        private static CityPlace ArchiveAcePlace() => Place(ArchiveAceId, ArchiveHall, "Туз червей", "Архивариус", null, 1600, 520,
            new float[] { 1440, 668, 1445, 560, 1495, 515, 1502, 392, 1540, 376, 1716, 412, 1714, 560, 1742, 600, 1738, 668, 1600, 676 },
            talk: true);

        private static CityPlace ArchiveBookPlace() => Place(ArchiveBookId, ArchiveHall, "Летопись", "Туз червей", null, 930, 560,
            new float[] { 690, 880, 680, 622, 735, 600, 760, 470, 940, 458, 1150, 470, 1176, 610, 1182, 872, 1000, 886 });

        private static CityPlace ArchiveCabinetPlace() => Place(ArchiveCabinetId, ArchiveHall, "Витрина достижений", "Туз червей", null,
            440, 520, new float[] { 150, 890, 152, 90, 320, 35, 530, 140, 600, 170, 732, 265, 736, 770, 600, 776, 540, 806, 520, 882, 300, 895 });

        private static CityPlace ArchiveDrawersPlace() => Place(ArchiveDrawersId, ArchiveHall, "Личное дело", "Туз червей", null,
            1810, 310, new float[] { 1530, 392, 1528, 172, 1800, 96, 2066, 12, 2072, 600, 1960, 642, 1742, 612, 1736, 420 });

        /// <summary>Облачко Туза червей — ребёнок этажа (зовётся из AddInteriorFurniture).</summary>
        private void AddArchiveHall(RectTransform floor, float width)
        {
            var picture = PictureSize(ArchiveHall);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            var at = new Vector2((ArchiveBubbleX / picture.x - .5f) * width, (.5f - ArchiveBubbleY / picture.y) * CityHeight);
            var (bubble, line, _) = SpeechBubble(floor, "Archive ace bubble", at, ArchiveBubbleWidth, ArchiveBubbleHeight,
                ArchiveAceHeadX - ArchiveBubbleX, unit);
            _archiveBubble = bubble;
            _archiveBubbleText = line;
            PaintArchive();
        }

        /// <summary>Нажатие на место в зале Архива.</summary>
        private void ClickArchive(string id)
        {
            if (!ArchiveInside || ArchiveShown || _state == null) return;
            if (id == ArchiveAceId) { _archiveLine = ArchiveTalk[_archiveTalk++ % ArchiveTalk.Length]; PaintArchive(); return; }
            if (id == ArchiveBookId)
            {
                // Книга открывается на самой новой открытой главе.
                _archiveSpread = Math.Max(0, HubChronicle.OpenCount(_state) - 1);
                OpenArchive("book");
            }
            else if (id == ArchiveCabinetId) { _archiveShelfPage = 0; OpenArchive("cabinet"); }
            else if (id == ArchiveDrawersId) OpenArchive("file");
        }

        private void OpenArchive(string what)
        {
            if (_archiveLayer == null) BuildArchiveLayer();
            _archiveOpen = what;
            // Сразу, а не со следующего кадра (UpdateArchive дальше держит его в согласии с залом).
            _archiveLayer.gameObject.SetActive(true);
            PaintArchive();
            _archiveLayer.SetAsLastSibling();
        }

        /// <summary>«Назад» сначала закрывает книгу, витрину или досье.</summary>
        private bool TryArchiveBack()
        {
            if (!ArchiveShown) return false;
            _archiveOpen = null;
            // Закрыл — архивариус снова о деле: награда на витрине или следующая глава.
            _archiveLine = null;
            PaintArchive();
            return true;
        }

        /// <summary>Каждый кадр: приветствие при входе, слой поверх зала, Esc и стрелки листают/закрывают.</summary>
        private void UpdateArchive(float deltaTime, bool typing)
        {
            if (_cityLevel != ArchiveLevel)
            {
                _archiveGreeted = false;
                _archiveOpen = null;
            }
            var show = ArchiveInside && _state != null && _archiveOpen != null && !_mapOpen && _mapFade <= 0;
            if (_archiveLayer != null && _archiveLayer.gameObject.activeSelf != show) _archiveLayer.gameObject.SetActive(show);
            var keyboard = typing ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            // Esc закрывает слой; его удержание — уже учтённое нажатие, из Архива оно не выводит.
            if (ArchiveShown && escape && !_archiveEscapeHeld) { _cityVerticalHeld = 1; TryArchiveBack(); }
            _archiveEscapeHeld = escape;
            var arrow = keyboard == null ? 0 : keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed ? -1
                : keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed ? 1 : 0;
            if (ArchiveShown && arrow != 0 && !_archiveArrowHeld) TurnArchive(arrow);
            _archiveArrowHeld = arrow != 0;
            if (!ArchiveInside || _state == null || _archiveBubble == null) return;
            if (!_archiveGreeted)
            {
                _archiveGreeted = true;
                _archiveLine = null;
                PaintArchive();
            }
            if (_archivePaintedFor != _state) PaintArchive();
        }

        /// <summary>Стрелки: в книге — разворот, на витрине — полка.</summary>
        private void TurnArchive(int step)
        {
            if (_archiveOpen == "book") _archiveSpread = Mathf.Clamp(_archiveSpread + step, 0, HubChronicle.Chapters.Length - 1);
            else if (_archiveOpen == "cabinet") _archiveShelfPage = Mathf.Clamp(_archiveShelfPage + step, 0, ArchiveShelfPages() - 1);
            else return;
            PaintArchive();
        }

        private int ArchiveShelfPages() => Math.Max(1, (_state.Achievements.Count + ArchiveShelfColumns * ArchiveShelfRows - 1)
            / (ArchiveShelfColumns * ArchiveShelfRows));

        private void BuildArchiveLayer()
        {
            _archiveLayer = StageLayer("Archive overlay");
            var dim = LoanArt(_archiveLayer, "Archive shade", null, Vector2.zero, new Vector2(6000, 6000), new Color(0, 0, 0, .7f));
            // Только держит нажатия, как стол банка: закрывают «ЗАКРЫТЬ», «Назад» и Esc.
            dim.raycastTarget = true;

            _archiveBook = LoanRect(_archiveLayer, "Archive book", Vector2.zero, new Vector2(1600, 900));
            var book = LoanArt(_archiveBook, "Book", "archive-book", new Vector2(0, ArchiveBookY),
                new Vector2(ArchiveBookWidth, ArchiveBookWidth * 753 / 1335f), new Color(.93f, .85f, .66f));
            book.raycastTarget = true;
            _archiveLeft = LoanRect(_archiveBook, "Left page", ArchiveLeftPage, new Vector2(420, 500));
            _archiveRight = LoanRect(_archiveBook, "Right page", ArchiveRightPage, new Vector2(420, 500));

            _archiveShelf = LoanRect(_archiveLayer, "Archive cabinet", Vector2.zero, new Vector2(1600, 900));
            var frame = LoanArt(_archiveShelf, "Cabinet frame", null, new Vector2(0, 14), new Vector2(1390, 724), ArchiveWood);
            frame.sprite = UiTextures.RoundedRect(64, 64, 24, Color.white);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = true;
            var rim = frame.gameObject.AddComponent<HubOutline>();
            rim.effectColor = new Color(.62f, .45f, .18f);
            rim.effectDistance = new Vector2(3, -3);
            var velvet = LoanArt(_archiveShelf, "Cabinet velvet", null, new Vector2(0, -12), new Vector2(1350, 630), ArchiveVelvet);
            velvet.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            velvet.type = Image.Type.Sliced;
            LoanText(_archiveShelf, "Cabinet title", "ВИТРИНА ДОСТИЖЕНИЙ", new Vector2(-110, 345), new Vector2(740, 44), 34, ArchiveGold, true);
            _archiveShelfCount = LoanText(_archiveShelf, "Cabinet count", "", new Vector2(500, 345), new Vector2(340, 40), 26, ArchiveGlassText);
            _archiveExhibits = LoanRect(_archiveShelf, "Exhibits", new Vector2(0, -12), new Vector2(1350, 630));

            _archiveFile = LoanRect(_archiveLayer, "Archive file", Vector2.zero, new Vector2(1600, 900));
            var paper = LoanArt(_archiveFile, "File paper", null, new Vector2(0, 20), new Vector2(880, 660), ShopPaper);
            paper.sprite = UiTextures.RoundedRect(64, 64, 14, Color.white);
            paper.type = Image.Type.Sliced;
            paper.raycastTarget = true;
            var edge = paper.gameObject.AddComponent<HubOutline>();
            edge.effectColor = new Color(.55f, .38f, .14f);
            edge.effectDistance = new Vector2(3, -3);
            LoanText(paper.rectTransform, "File title", "ЛИЧНОЕ ДЕЛО", new Vector2(0, 270), new Vector2(600, 56), 46, LoanInk, true);
            // Печать — внизу, как на заверенном документе.
            var seal = LoanArt(paper.rectTransform, "File seal", "archive-seal", new Vector2(335, -286), new Vector2(88, 82), ShopRed);
            seal.preserveAspect = true;
            seal.rectTransform.localRotation = Quaternion.Euler(0, 0, -12);
            _archiveRows = LoanRect(paper.rectTransform, "File rows", new Vector2(0, -10), new Vector2(780, 470));

            // Листать — у краёв книги и полки; закрыть — под ними.
            _archivePrev = CityButton(_archiveLayer, "‹", 16, 380, 80, 120, () => TurnArchive(-1));
            _archiveNext = CityButton(_archiveLayer, "›", 1600 - 16 - 80, 380, 80, 120, () => TurnArchive(1));
            CityButton(_archiveLayer, "ЗАКРЫТЬ", (1600 - 260) / 2f, 900 - 20 - 58, 260, 58, () => TryArchiveBack());
            _archiveLayer.gameObject.SetActive(false);
        }

        private void PaintArchive()
        {
            if (_state == null) return;
            _archivePaintedFor = _state;
            if (_archiveBubble != null)
            {
                _archiveBubbleText.text = _archiveLine ?? ArchiveGreeting();
                _archiveBubble.gameObject.SetActive(true);
            }
            if (_archiveLayer == null) return;
            _archiveBook.gameObject.SetActive(_archiveOpen == "book");
            _archiveShelf.gameObject.SetActive(_archiveOpen == "cabinet");
            _archiveFile.gameObject.SetActive(_archiveOpen == "file");
            var pages = _archiveOpen == "book" ? HubChronicle.Chapters.Length : _archiveOpen == "cabinet" ? ArchiveShelfPages() : 1;
            var at = _archiveOpen == "book" ? _archiveSpread : _archiveShelfPage;
            _archivePrev.gameObject.SetActive(pages > 1 && at > 0);
            _archiveNext.gameObject.SetActive(pages > 1 && at < pages - 1);
            if (_archiveOpen == "book") PaintArchiveBook();
            else if (_archiveOpen == "cabinet") PaintArchiveShelf();
            else if (_archiveOpen == "file") PaintArchiveFile();
            if (_archiveOpen != null) _archiveLayer.SetAsLastSibling();
        }

        private string ArchiveGreeting()
        {
            if (_state.Achievements.Any(a => a.Unlocked && !a.Claimed)) return "На витрине тебя ждёт награда — забери!";
            var next = HubChronicle.NextLocked(_state);
            if (next != null) return "Заходи. Следующая глава летописи откроется на " + next.Level + " уровне.";
            return "Все главы открыты. Остальное город ещё пишет.";
        }

        // ── Летопись ─────────────────────────────────────────────────────────────────────

        private void PaintArchiveBook()
        {
            ClearShopChildren(_archiveLeft);
            ClearShopChildren(_archiveRight);
            var chapter = HubChronicle.Chapters[_archiveSpread];
            var open = HubChronicle.IsOpen(_state, chapter);
            var total = HubChronicle.Chapters.Length;
            LoanText(_archiveLeft, "Chapter number", "ГЛАВА " + ArchiveRoman[_archiveSpread] + " ИЗ " + ArchiveRoman[total - 1],
                new Vector2(0, 222), new Vector2(400, 30), 22, ShopMuted);
            NewsFit(LoanText(_archiveLeft, "Chapter title", chapter.Title, new Vector2(0, 178), new Vector2(400, 52), 40, LoanInk, true));
            if (!open)
            {
                var seal = LoanArt(_archiveLeft, "Seal", "archive-seal", new Vector2(0, 20), new Vector2(190, 177), ShopRed);
                seal.preserveAspect = true;
                LoanText(_archiveLeft, "Locked", "Откроется на " + chapter.Level + " уровне", new Vector2(0, -120), new Vector2(400, 40), 30,
                    ShopRed, true);
                ArchivePageText(_archiveRight, "Страницы запечатаны сургучом. Архивариус сломает печать, когда придёт время.",
                    new Vector2(0, 0), new Vector2(360, 200), ShopMuted, TextAnchor.MiddleCenter);
                return;
            }
            ArchivePageText(_archiveLeft, chapter.Left, new Vector2(0, -20), new Vector2(400, 300), LoanInk, TextAnchor.UpperLeft);
            ArchivePageText(_archiveRight, chapter.Right + (chapter.Torn ? "…" : ""), new Vector2(0, chapter.Torn ? 50 : 20),
                new Vector2(400, chapter.Torn ? 340 : 420), LoanInk, TextAnchor.UpperLeft);
            LoanText(_archiveLeft, "Page number", "— " + (_archiveSpread * 2 + 1) + " —", new Vector2(0, -222), new Vector2(200, 30), 22, ShopMuted);
            if (chapter.Torn)
            {
                // Конец главы вырван: обрывок лежит на странице, ниже — пометка архивариуса.
                var scrap = LoanArt(_archiveRight, "Torn scrap", "archive-page", new Vector2(110, -170), new Vector2(170, 146),
                    new Color(.93f, .85f, .66f));
                scrap.preserveAspect = true;
                scrap.rectTransform.localRotation = Quaternion.Euler(0, 0, 9);
                LoanText(_archiveRight, "Torn note", "Дальше страница вырвана.", new Vector2(-70, -150), new Vector2(240, 60), 24, ShopRed);
                return;
            }
            LoanText(_archiveRight, "Page number", "— " + (_archiveSpread * 2 + 2) + " —", new Vector2(0, -222), new Vector2(200, 30), 22, ShopMuted);
        }

        private void ArchivePageText(Transform page, string text, Vector2 at, Vector2 size, Color color, TextAnchor anchor)
        {
            var label = LoanText(page, "Text", text, at, size, 29, color);
            label.alignment = anchor;
            label.lineSpacing = 1f;
            label.rectTransform.sizeDelta = size;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 29;
        }

        // ── Витрина достижений ────────────────────────────────────────────────────────────

        private void PaintArchiveShelf()
        {
            ClearShopChildren(_archiveExhibits);
            var all = _state.Achievements;
            _archiveShelfPage = Mathf.Clamp(_archiveShelfPage, 0, ArchiveShelfPages() - 1);
            _archiveShelfCount.text = "Получено " + all.Count(a => a.Unlocked) + " из " + all.Count;
            var perPage = ArchiveShelfColumns * ArchiveShelfRows;
            for (var i = 0; i < perPage; i++)
            {
                var index = _archiveShelfPage * perPage + i;
                if (index >= all.Count) break;
                var x = (i % ArchiveShelfColumns - (ArchiveShelfColumns - 1) / 2f) * 260;
                var y = i < ArchiveShelfColumns ? 150 : -160;
                ArchiveExhibit(all[index], new Vector2(x, y));
            }
        }

        /// <summary>Экспонат: стекло, предмет (полученное — медаль, нет — под тканью), табличка, описание, награда.</summary>
        private void ArchiveExhibit(HubAchievement achievement, Vector2 at)
        {
            var glass = LoanArt(_archiveExhibits, "Exhibit " + achievement.Id, null, at, new Vector2(246, 296), new Color(1, 1, 1, .07f));
            glass.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            glass.type = Image.Type.Sliced;
            var edge = glass.gameObject.AddComponent<HubOutline>();
            edge.effectColor = new Color(.78f, .6f, .3f, .55f);
            edge.effectDistance = new Vector2(2, -2);
            var box = glass.rectTransform;

            var art = LoanArt(box, "Item", null, new Vector2(0, 82), new Vector2(112, 112),
                achievement.Unlocked ? new Color(.86f, .66f, .28f) : new Color(.86f, .84f, .78f));
            art.sprite = UiTextures.RoundedRect(128, 128, achievement.Unlocked ? 64 : 34, Color.white);
            art.type = Image.Type.Sliced;
            var rim = art.gameObject.AddComponent<HubOutline>();
            rim.effectColor = achievement.Unlocked ? new Color(.45f, .28f, .06f) : new Color(.55f, .52f, .46f);
            rim.effectDistance = new Vector2(3, -3);
            var glyph = LoanText(art.rectTransform, "Glyph", achievement.Unlocked ? "♥" : "?", Vector2.zero, new Vector2(112, 112), 56,
                achievement.Unlocked ? new Color(.62f, .1f, .08f) : new Color(.55f, .52f, .46f), true);
            glyph.font = _font;

            var plate = LoanArt(box, "Plate", null, new Vector2(0, 2), new Vector2(228, 40), new Color(.78f, .6f, .3f));
            plate.sprite = UiTextures.RoundedRect(64, 64, 8, Color.white);
            plate.type = Image.Type.Sliced;
            NewsFit(LoanText(plate.rectTransform, "Name", achievement.Name, Vector2.zero, new Vector2(216, 34), 24, LoanInk, true));
            var about = LoanText(box, "About", achievement.Description, new Vector2(0, -54), new Vector2(232, 64), 19, ArchiveGlassText);
            about.rectTransform.sizeDelta = new Vector2(232, 64);
            about.verticalOverflow = VerticalWrapMode.Truncate;
            about.resizeTextForBestFit = true;
            about.resizeTextMinSize = 13;
            about.resizeTextMaxSize = 19;

            var reward = "+" + achievement.RewardCoins + " коинов";
            if (achievement.Unlocked && !achievement.Claimed)
            {
                var id = achievement.Id;
                LoanPaperButton(box, "ЗАБРАТЬ " + reward, new Vector2(0, -118), new Vector2(224, 44), () => ClaimArchiveReward(id), true);
                return;
            }
            var status = achievement.Claimed ? "Награда получена"
                : (achievement.Goal > 1 ? achievement.Progress + " / " + achievement.Goal + " · " : "") + reward;
            LoanText(box, "Status", status, new Vector2(0, -118), new Vector2(232, 32), 21, achievement.Claimed ? ArchiveGold : ArchiveGlassText);
        }

        private async void ClaimArchiveReward(string id)
        {
            var achievement = _state?.Achievements.FirstOrDefault(a => a.Id == id);
            if (achievement == null || _busy) return;
            var coins = achievement.RewardCoins;
            await ArchiveRun("achievement_claim", id, "Награда твоя: +" + coins + " коинов. Заслужил.");
        }

        /// <summary>Действие в Архиве; отвечает Туз червей в облачке (общая плашка ушла бы под слой).</summary>
        private async Task<bool> ArchiveRun(string action, string target, string reply)
        {
            if (_busy) return false;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = action, Target = target });
            if (this == null) return false;
            SetBusy(false);
            if (result.Success) { _state = result.Snapshot; Render(); }
            _archiveLine = result.Success ? reply : PlayerHubRuntime.ErrorText(result);
            PaintArchive();
            return result.Success;
        }

        // ── Личное дело ───────────────────────────────────────────────────────────────────

        private void PaintArchiveFile()
        {
            ClearShopChildren(_archiveRows);
            var profile = _state.Profile;
            var joined = DateTimeOffset.FromUnixTimeSeconds(profile.JoinedAt).ToOffset(TimeSpan.FromHours(3));
            var favourite = _state.Matches.Where(m => m.Completed && !string.IsNullOrEmpty(m.Game)).GroupBy(m => m.Game)
                .OrderByDescending(g => g.Count()).FirstOrDefault();
            var loans = _state.Bank.Contracts.Count;
            var rows = new[]
            {
                ("Приезжий", "«" + profile.DisplayName + "»"),
                ("В городе с", joined.Day + " " + ArchiveMonths[joined.Month - 1] + " " + joined.Year),
                ("Уровень", profile.Level + " · опыт " + HubMoney(profile.XP)),
                ("Партий сыграно", profile.Matches + " · побед " + profile.Wins + " · поражений " + profile.Losses),
                ("Любимая игра", favourite == null ? "пока не выбрана" : HubGameName(favourite.Key) + " · "
                    + ArchiveCount(favourite.Count(), "партия", "партии", "партий")),
                ("Заработано всего", HubMoney(profile.CoinsEarned) + " коинов"),
                ("Кредиты у Тузяры", loans == 0 ? "не брал" : "взято " + loans + (_state.Bank.Debt > 0
                    ? " · долг " + HubMoney(_state.Bank.Debt) : " · долгов нет")),
                ("Брошено помидоров", profile.TomatoesThrown.ToString()),
                ("Достижения", _state.Achievements.Count(a => a.Unlocked) + " из " + _state.Achievements.Count),
                ("Летопись", HubChronicle.OpenCount(_state) + " из " + HubChronicle.Chapters.Length + " глав"),
            };
            for (var i = 0; i < rows.Length; i++)
            {
                var y = 212 - i * 47;
                var label = LoanText(_archiveRows, "Label " + i, rows[i].Item1, new Vector2(-200, y), new Vector2(360, 40), 26, ShopMuted);
                label.alignment = TextAnchor.MiddleLeft;
                var value = NewsFit(LoanText(_archiveRows, "Value " + i, rows[i].Item2, new Vector2(170, y), new Vector2(440, 40), 27, LoanInk));
                value.alignment = TextAnchor.MiddleRight;
                if (i < rows.Length - 1)
                {
                    var line = LoanArt(_archiveRows, "Rule " + i, null, new Vector2(0, y - 23), new Vector2(780, 1.5f), new Color(.55f, .38f, .14f, .3f));
                    line.raycastTarget = false;
                }
            }
        }

        private static string ArchiveCount(int count, string one, string few, string many)
        {
            var tens = count % 100;
            var word = tens >= 11 && tens <= 14 ? many : (count % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
            return count + " " + word;
        }
    }
}
