using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Окно коллекции в городе (2026-11-02): кнопка «Коллекция» панели города и всё, что вело в инвентарь, открывают поверх
    /// текущего экрана ту же доску, что настройки и задания (<see cref="CityWindow"/>). Слева — свой персонаж в золотом
    /// кольце панели, «СМЕНИТЬ ПЕРСОНАЖА» (прежний выбор аватара) и сколько вещей собрано. Справа — вкладки (карты, стол,
    /// рамки, сундучки, эмоции, образ) и витрина 4×2 из всех вещей каталога: своя — в цвете, надетая — с зелёной печатью,
    /// чужая — тёмная под замком. Нажатая вещь — в строке под витриной: название, редкость, описание и действие
    /// («ВЫБРАТЬ», «СНЯТЬ», «БРОСИТЬ», «В ЛАВКУ»). Внизу — «В ЛАВКУ», «ЗАКРЫТЬ» и кошелёк, как в окне заданий.
    /// Картинки вещей — UI/Hub/Items/&lt;id&gt; (они же в Лавке, <see cref="ShopItemArt"/>), пока их нет — заглушки Лавки;
    /// значки вкладок — Hud/col-*, пока их нет — масти. Прежняя страница инвентаря больше не открывается.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class CollectionTile
        {
            public string ItemId;
            public RectTransform Rect, Art;
            public CanvasGroup ArtGroup;
            public Image Plate, Glow, Seal, Lock;
            public Text Name, Count;
        }

        // Вкладка: надпись, значок Hud/col-*, масть вместо значка, пока его нет, и виды вещей каталога по порядку.
        private static readonly (string Title, string Icon, string Glyph, string[] Categories)[] CollectionTabs =
        {
            ("КАРТЫ", "col-cards", "♠", new[] { "cardback", "cardface" }),
            ("СТОЛ", "col-table", "♣", new[] { "table", "background" }),
            ("РАМКИ", "col-frames", "♥", new[] { "frame" }),
            ("СУНДУЧКИ", "col-chests", "♦", new[] { "chest", "trail" }),
            ("ЭМОЦИИ", "col-emotes", "★", new[] { "emote", "consumable" }),
            ("ОБРАЗ", "col-look", "●", new[] { "hat", "outfit", "hair", "eyes" }),
        };

        // Витрина справа от персонажа: от его панели до правого края строк окна заданий; вкладка помещается в 4×2 плитки.
        private const float CollectionLeft = -195, CollectionWidth = 760, CollectionTileW = 178, CollectionTileH = 168;
        private const int CollectionColumns = 4, CollectionPageSize = 8;
        private RectTransform _collectionLayer, _collectionWindow, _collectionStripArt;
        private CanvasGroup _collectionGroup;
        private Image _collectionPortrait, _collectionFill;
        private Text _collectionName, _collectionCount, _collectionWallet, _collectionItemName, _collectionItemDetail;
        private Button _collectionAction;
        private readonly List<(CanvasGroup Group, Image Glow)> _collectionTabs = new List<(CanvasGroup, Image)>();
        private readonly List<CollectionTile> _collectionTiles = new List<CollectionTile>();
        private int _collectionTab;
        // Вещь в строке под витриной; пусто — надетая во вкладке или первая.
        private string _collectionItem = "", _collectionStripItem;
        private bool _collectionShown, _collectionEscapeHeld;
        // Экран, поверх которого открыто окно: ушли с него — окно закрывается.
        private string _collectionPage;
        private float _collectionFade;
        private bool CollectionShown => _collectionShown || _collectionFade > 0;

        private void ShowCollectionWindow()
        {
            if (_state == null || _busy) return;
            if (_collectionLayer == null) BuildCollectionWindow();
            _collectionShown = true;
            _collectionPage = _page;
            _collectionLayer.gameObject.SetActive(true);
            _collectionLayer.SetAsLastSibling();
            PaintCollectionWindow();
            RefreshMapButton();
        }

        private void CloseCollectionWindow() => _collectionShown = false;

        private void BuildCollectionWindow()
        {
            _collectionLayer = CityWindow("Collection window", "КОЛЛЕКЦИЯ", out _collectionGroup, out _collectionWindow);
            BuildCollectionShowcase();

            const float tabW = 120, tabStep = 128;
            for (var i = 0; i < CollectionTabs.Length; i++)
            {
                var (title, icon, glyph, _) = CollectionTabs[i];
                var index = i;
                var at = new Vector2(CollectionLeft + tabW / 2 + i * tabStep, 290);
                // Выбранная вкладка — в том же золотом ободке, что выбранная вещь.
                var glow = CollectionGlow(_collectionWindow, at, new Vector2(tabW, 76));
                var plate = QuestArt(_collectionWindow, "Tab " + title, null, at, new Vector2(tabW, 76), new Color(.2f, .12f, .07f));
                HdPlaque(plate);
                plate.raycastTarget = true;
                var button = plate.gameObject.AddComponent<Button>();
                button.targetGraphic = plate;
                button.onClick.AddListener(() => { _collectionTab = index; _collectionItem = ""; });
                plate.gameObject.AddComponent<HubPress>();
                var art = HudArt(icon);
                if (art != null)
                {
                    var image = QuestArt(plate.transform, "Icon", null, new Vector2(0, 11), new Vector2(46, 46), Color.white);
                    image.sprite = art;
                    image.preserveAspect = true;
                }
                else SettingsSign(plate.transform, glyph, new Vector2(0, 11), new Vector2(46, 46), 34);
                var label = SettingsSign(plate.transform, title, new Vector2(0, -22), new Vector2(tabW - 16, 26), 19);
                if (label.preferredWidth > tabW - 16) label.fontSize = Mathf.FloorToInt(19 * (tabW - 16) / label.preferredWidth);
                // Невыбранные вкладки притушены.
                _collectionTabs.Add((plate.gameObject.AddComponent<CanvasGroup>(), glow));
            }

            for (var i = 0; i < CollectionPageSize; i++)
                _collectionTiles.Add(BuildCollectionTile(new Vector2(
                    CollectionLeft + CollectionTileW / 2 + i % CollectionColumns * (CollectionTileW + 16), 146 - i / CollectionColumns * (CollectionTileH + 14))));

            // Строка выбранной вещи — как строка задания: картинка в закруглённом конце, название и описание, действие справа.
            var strip = QuestArt(_collectionWindow, "Collection item", null, new Vector2(CollectionLeft + CollectionWidth / 2, -196),
                new Vector2(CollectionWidth, TaskRowH), Color.white);
            strip.sprite = HudArt("currency-plate");
            strip.type = Image.Type.Sliced;
            strip.pixelsPerUnitMultiplier = 109f / TaskRowH;
            var left = -CollectionWidth / 2;
            _collectionStripArt = QuestRect(strip.transform, "Art", new Vector2(left + TaskInset + 39, 0), new Vector2(72, 72));
            _collectionItemName = QuestText(strip.transform, "Name", "", new Vector2(left + 124 + 190, 20), new Vector2(380, 40), 30, SignGoldTop);
            _collectionItemName.alignment = TextAnchor.MiddleLeft;
            _collectionItemName.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = _collectionItemName.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.06f, .03f, 0, .9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            _collectionItemDetail = NewsFit(QuestText(strip.transform, "Detail", "", new Vector2(left + 124 + 190, -17), new Vector2(380, 26), 18, SettingsCream));
            _collectionItemDetail.font = _font;
            _collectionItemDetail.alignment = TextAnchor.MiddleLeft;
            const float actionW = 210, actionH = 58;
            _collectionAction = CityButton(strip.transform, "ВЫБРАТЬ", CollectionWidth - TaskInset - actionW, (TaskRowH - actionH) / 2, actionW, actionH, CollectionAct);
            HdPlaque(_collectionAction.GetComponent<Image>());

            // Внизу — как в окне заданий: слева кнопка с монетой, посередине «ЗАКРЫТЬ», справа кошелёк.
            var bottom = SettingsWindowH - 40 - 64;
            var shop = CityButton(_collectionWindow, "В ЛАВКУ", (SettingsWindowW - TaskRowW) / 2 + TaskInset, bottom, 360, 64,
                () => { CloseCollectionWindow(); Navigate("shop"); });
            HdPlaque(shop.GetComponent<Image>());
            var coin = CenteredImage(shop.transform, "Icon", 16, 6, 52, 52);
            coin.sprite = HubCurrencyArt.Silver;
            var caption = shop.GetComponentInChildren<Text>().rectTransform;
            caption.sizeDelta = new Vector2(caption.sizeDelta.x - 70, caption.sizeDelta.y);
            caption.anchoredPosition += new Vector2(64, 0);
            WindowCloseButton(_collectionWindow, bottom, CloseCollectionWindow);
            _collectionWallet = WindowWallet(_collectionWindow);
        }

        /// <summary>Панель персонажа: заголовок, портрет в кольце панели города, имя, смена персонажа и «СОБРАНО».</summary>
        private void BuildCollectionShowcase()
        {
            // Непрозрачная: обводка рисуется копиями под панелью и сквозь полупрозрачную просвечивала бы целиком.
            var panel = QuestArt(_collectionWindow, "Showcase", null, new Vector2(-390, 43), new Vector2(350, 570), new Color(.1f, .06f, .035f));
            panel.sprite = UiTextures.RoundedRect(96, 96, 28, Color.white);
            panel.type = Image.Type.Sliced;
            var edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(.85f, .62f, .25f, .45f);
            edge.effectDistance = new Vector2(2, -2);
            SettingsSign(panel.transform, "ПЕРСОНАЖ", new Vector2(0, 255), new Vector2(300, 36), 28);

            // Те же доли, что у портрета на панели города: круг 68 в кольце 100.
            const float ring = 196, disc = ring * .68f, k = disc / 68f;
            var circle = QuestArt(panel.transform, "Avatar disc", null, new Vector2(0, 134), new Vector2(disc, disc), new Color(.3f, .16f, .1f));
            circle.sprite = UiTextures.Circle(256, Color.white);
            circle.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _collectionPortrait = CenteredImage(circle.transform, "Current avatar", -14 * k, -4 * k, 96 * k, 108 * k);
            var frame = QuestArt(panel.transform, "Avatar frame", null, new Vector2(0, 132), new Vector2(ring, ring), Color.white);
            frame.sprite = HudArt("avatar-frame");
            frame.preserveAspect = true;
            _collectionName = SettingsSign(panel.transform, "", new Vector2(0, 8), new Vector2(300, 40), 32);

            var change = CityButton(panel.transform, "СМЕНИТЬ ПЕРСОНАЖА", 30, 285 + 52 - 28, 290, 56, OpenAvatarPicker);
            HdPlaque(change.GetComponent<Image>());
            change.GetComponentInChildren<Text>().fontSize = 24;

            // «СОБРАНО» слева, «12 / 30» справа над полосой той же ширины.
            var collected = SettingsSign(panel.transform, "СОБРАНО", Vector2.zero, new Vector2(200, 34), 26);
            var width = collected.preferredWidth + 4;
            collected.rectTransform.sizeDelta = new Vector2(width, 34);
            collected.rectTransform.anchoredPosition = new Vector2(-145 + width / 2, -128);
            _collectionCount = QuestText(panel.transform, "Collected count", "", new Vector2(85, -128), new Vector2(120, 34), 26, Color.white);
            _collectionCount.font = _font;
            _collectionCount.fontStyle = FontStyle.Bold;
            _collectionCount.alignment = TextAnchor.MiddleRight;
            var track = QuestArt(panel.transform, "Collected bar", null, new Vector2(0, -168), new Vector2(290, 28), Color.white);
            track.sprite = HudArt("xp-bar");
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 58f / 28;
            _collectionFill = MakeRect(track.transform, "Fill", 21, 8, 0, 12).gameObject.AddComponent<Image>();
            Round(_collectionFill, 290 - 42, 12, 6);
            _collectionFill.color = new Color(1f, .8f, .25f);
            _collectionFill.raycastTarget = false;
            // На телефоне шрифт крупнее — подсказка мельчает, а не расползается на четыре строки.
            NewsFit(QuestText(panel.transform, "Hint", "Новые вещи — в Лавке Дамы бубен:\nкаждую полночь новая витрина.",
                new Vector2(0, -228), new Vector2(300, 50), 18, SettingsCream)).font = _font;
        }

        /// <summary>Плитка витрины: табличка вывесок, картинка вещи, название; замок, печать «надето» и число — по вещи.</summary>
        private CollectionTile BuildCollectionTile(Vector2 at)
        {
            var tile = new CollectionTile();
            tile.Rect = QuestRect(_collectionWindow, "Collection tile", at, new Vector2(CollectionTileW, CollectionTileH));
            // Вещь в строке под витриной — в золотом ободке.
            tile.Glow = CollectionGlow(tile.Rect, Vector2.zero, new Vector2(CollectionTileW, CollectionTileH));
            tile.Plate = QuestArt(tile.Rect, "Plate", null, Vector2.zero, new Vector2(CollectionTileW, CollectionTileH), new Color(.2f, .12f, .07f));
            var plaque = HudArt("plaque-hd");
            if (plaque != null)
            {
                tile.Plate.sprite = plaque;
                tile.Plate.type = Image.Type.Sliced;
                // Рамка тоньше, чем у кнопок: плиток много, а картинке нужно место.
                tile.Plate.pixelsPerUnitMultiplier = plaque.border.y / 30f;
            }
            tile.Plate.raycastTarget = true;
            var button = tile.Plate.gameObject.AddComponent<Button>();
            button.targetGraphic = tile.Plate;
            button.onClick.AddListener(() => { if (tile.ItemId != null) _collectionItem = tile.ItemId; });
            tile.Plate.gameObject.AddComponent<HubPress>();
            tile.Art = QuestRect(tile.Plate.transform, "Art", new Vector2(0, 16), new Vector2(96, 96));
            tile.ArtGroup = tile.Art.gameObject.AddComponent<CanvasGroup>();
            tile.ArtGroup.blocksRaycasts = false;
            tile.Lock = QuestArt(tile.Plate.transform, "Lock", null, new Vector2(0, 16), new Vector2(52, 52), Color.white);
            tile.Lock.sprite = HudArt("set-private");
            tile.Lock.preserveAspect = true;
            // Название — внутри рамки; длинное мельчает и переносится на вторую строку.
            tile.Name = NewsFit(QuestText(tile.Plate.transform, "Name", "", new Vector2(0, -50), new Vector2(CollectionTileW - 44, 36), 21, SettingsCream));
            tile.Seal = QuestArt(tile.Plate.transform, "Seal", null, new Vector2(CollectionTileW / 2 - 24, CollectionTileH / 2 - 24), new Vector2(44, 44),
                new Color(.3f, .7f, .25f));
            var done = HudArt("task-done");
            tile.Seal.sprite = done != null ? done : UiTextures.Circle(64, Color.white);
            tile.Seal.preserveAspect = true;
            tile.Count = QuestText(tile.Plate.transform, "Count", "", new Vector2(46, -14), new Vector2(70, 30), 24, Color.white);
            tile.Count.font = _font;
            tile.Count.fontStyle = FontStyle.Bold;
            tile.Count.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .85f);
            return tile;
        }

        /// <summary>Золотой ободок выбранного: скруглённая подложка на 7 px шире таблички со всех сторон.</summary>
        private static Image CollectionGlow(Transform parent, Vector2 at, Vector2 size)
        {
            var rect = new GameObject("Glow", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = at;
            rect.sizeDelta = size + new Vector2(14, 14);
            var glow = rect.gameObject.AddComponent<Image>();
            glow.sprite = UiTextures.RoundedRect(96, 96, 30, Color.white);
            glow.type = Image.Type.Sliced;
            glow.color = new Color(1f, .8f, .3f);
            glow.raycastTarget = false;
            return glow;
        }

        /// <summary>Вещи вкладки: виды по порядку вкладки, внутри — как в каталоге (базовая первой). Персонажи — в своём выборе.</summary>
        private List<HubCatalogItem> CollectionItems(int tab)
        {
            var categories = CollectionTabs[tab].Categories;
            return _state.Catalog.Where(item => categories.Contains(item.Category))
                .OrderBy(item => System.Array.IndexOf(categories, item.Category)).ToList();
        }

        private bool CollectionEquipped(HubCatalogItem item) => _state.Equipped.Any(slot => slot.ItemId == item.Id);

        private int CollectionQuantity(string id) => _state.Inventory.Where(entry => entry.ItemId == id).Sum(entry => entry.Quantity);

        /// <summary>Действие строки: бросить расходник, сходить в Лавку за чужой вещью, надеть свою или снять надетую.</summary>
        private void CollectionAct()
        {
            var item = _state?.Catalog.FirstOrDefault(entry => entry.Id == _collectionItem);
            if (item == null || _busy) return;
            if (item.Category == "consumable") { CloseCollectionWindow(); Navigate("throw:" + item.Id); return; }
            if (!HubOwned(item.Id))
            {
                if (item.Price > 0) { CloseCollectionWindow(); Navigate("shop"); }
                return;
            }
            if (!CollectionEquipped(item)) Send("equip", item.Id);
            // Снять можно только то, у чего нет базовой вещи: рамку, рубашку и сукно меняют на другие.
            else if (!_state.Catalog.Any(entry => entry.Category == item.Category && entry.IsDefault)) Send("unequip", item.Category);
        }

        /// <summary>Каждый кадр: Esc, проявление и что на витрине.</summary>
        private void UpdateCollectionWindow(float deltaTime, bool typing)
        {
            if (_collectionLayer == null) return;
            if (_collectionShown && _page != _collectionPage) _collectionShown = false;
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (_collectionShown && escape && !_collectionEscapeHeld) CloseCollectionWindow();
            _collectionEscapeHeld = escape;
            _collectionFade = Mathf.MoveTowards(_collectionFade, _collectionShown ? 1 : 0, deltaTime / .18f);
            _collectionGroup.alpha = Mathf.SmoothStep(0, 1, _collectionFade);
            _collectionGroup.blocksRaycasts = _collectionShown;
            if (!CollectionShown)
            {
                // Кнопка карты обновляется не каждый кадр: вернуть её, как только окно погасло.
                if (_collectionLayer.gameObject.activeSelf) { _collectionLayer.gameObject.SetActive(false); RefreshMapButton(); }
                return;
            }
            var reduced = _state != null && _state.Settings.ReducedMotion;
            _collectionWindow.localScale = Vector3.one * (reduced ? 1 : Mathf.Lerp(.94f, 1, _collectionGroup.alpha));
            _collectionLayer.SetAsLastSibling();
            PaintCollectionWindow();
        }

        private void PaintCollectionWindow()
        {
            if (_state == null) return;
            for (var i = 0; i < _collectionTabs.Count; i++)
            {
                _collectionTabs[i].Group.alpha = i == _collectionTab ? 1 : .55f;
                _collectionTabs[i].Glow.gameObject.SetActive(i == _collectionTab);
            }
            var items = CollectionItems(_collectionTab);
            if (!items.Any(item => item.Id == _collectionItem))
                _collectionItem = (items.FirstOrDefault(CollectionEquipped) ?? items.FirstOrDefault())?.Id ?? "";
            for (var i = 0; i < _collectionTiles.Count; i++)
            {
                var tile = _collectionTiles[i];
                var item = i < items.Count ? items[i] : null;
                if (item == null)
                {
                    // Пустое место на полке — бледная табличка: вещей станет больше.
                    if (tile.ItemId != null) { tile.ItemId = null; ClearShopChildren(tile.Art); tile.Name.text = ""; }
                    tile.Plate.color = new Color(1, 1, 1, .3f);
                    tile.Lock.gameObject.SetActive(false);
                    tile.Seal.gameObject.SetActive(false);
                    tile.Glow.gameObject.SetActive(false);
                    tile.Count.text = "";
                    continue;
                }
                if (tile.ItemId != item.Id)
                {
                    tile.ItemId = item.Id;
                    ClearShopChildren(tile.Art);
                    ShopItemArt(tile.Art, item, Vector2.zero, 96);
                    tile.Name.text = item.Name;
                }
                var owned = HubOwned(item.Id);
                tile.ArtGroup.alpha = owned ? 1 : .3f;
                tile.Plate.color = owned ? Color.white : new Color(.7f, .7f, .7f);
                tile.Name.color = owned ? SettingsCream : new Color(.62f, .56f, .48f);
                tile.Lock.gameObject.SetActive(!owned && tile.Lock.sprite != null);
                tile.Seal.gameObject.SetActive(CollectionEquipped(item));
                tile.Glow.gameObject.SetActive(item.Id == _collectionItem);
                var quantity = item.Category == "consumable" ? CollectionQuantity(item.Id) : 0;
                tile.Count.text = quantity > 0 ? "×" + quantity : "";
            }
            PaintCollectionItem(items.FirstOrDefault(item => item.Id == _collectionItem));

            _collectionPortrait.sprite = PlayerProfile.Avatar;
            if (_collectionName.text != _state.Profile.DisplayName)
            {
                _collectionName.text = _state.Profile.DisplayName;
                _collectionName.fontSize = 32;
                if (_collectionName.preferredWidth > 300) _collectionName.fontSize = Mathf.Max(18, Mathf.FloorToInt(32 * 300 / _collectionName.preferredWidth));
            }
            // Собирать — вещи, а не персонажей и не расходники.
            var all = _state.Catalog.Where(item => item.Category != "avatar" && item.Category != "consumable").ToList();
            var have = all.Count(item => HubOwned(item.Id));
            _collectionCount.text = have + " / " + all.Count;
            var share = all.Count == 0 ? 0 : have / (float)all.Count;
            _collectionFill.rectTransform.sizeDelta = new Vector2((290 - 42) * share, 12);
            _collectionFill.enabled = share > 0;
            _collectionWallet.text = HubMoney(_state.Profile.Coins);
        }

        private void PaintCollectionItem(HubCatalogItem item)
        {
            _collectionAction.transform.parent.gameObject.SetActive(item != null);
            if (item == null) return;
            if (_collectionStripItem != item.Id)
            {
                _collectionStripItem = item.Id;
                ClearShopChildren(_collectionStripArt);
                ShopItemArt(_collectionStripArt, item, Vector2.zero, 72);
                _collectionItemName.text = item.Name;
                _collectionItemName.fontSize = 30;
                if (_collectionItemName.preferredWidth > 380) _collectionItemName.fontSize = Mathf.Max(18, Mathf.FloorToInt(30 * 380 / _collectionItemName.preferredWidth));
            }
            var owned = HubOwned(item.Id);
            var detail = item.Category == "consumable" ? HubCategoryName(item.Category) + " · в запасе " + CollectionQuantity(item.Id)
                : HubShop.RarityName(item.Rarity) + " · " + HubCategoryName(item.Category);
            // У чужой вещи вместо описания — цена: строка не мельчает.
            detail += !owned && item.Price > 0 ? " · в Лавке за " + item.Price : " · " + item.Description;
            _collectionItemDetail.text = detail;

            string action;
            var can = true;
            if (item.Category == "consumable") action = "БРОСИТЬ";
            else if (!owned) { action = item.Price > 0 ? "В ЛАВКУ" : "СКОРО"; can = item.Price > 0; }
            else if (!CollectionEquipped(item)) action = "ВЫБРАТЬ";
            else if (_state.Catalog.Any(entry => entry.Category == item.Category && entry.IsDefault)) { action = "ВЫБРАНО"; can = false; }
            else action = "СНЯТЬ";
            var label = _collectionAction.GetComponentInChildren<Text>();
            if (label.text != action) label.text = action;
            _collectionAction.interactable = can && !_busy;
            label.color = new Color(1, 1, 1, can ? 1 : .55f);
        }
    }
}
