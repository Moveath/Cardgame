using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CardGameClient;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class MobileLayoutQa
{
    public static void RunDurakClub()
    {
        Directory.CreateDirectory(Output);
        try
        {
            typeof(AppFlowController).Assembly.GetType("CardGameClient.MobileUi").GetField("TestPhone").SetValue(null, true);
            PlayerHubRuntime.UseService(new LocalPlayerHubService(new Store(), new Clock()));
            PlayerHubRuntime.LoadAsync().GetAwaiter().GetResult();
            CheckClubRosterIdentity();
            foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(1822, 820), new Vector2Int(1400, 820), new Vector2Int(1920, 1080) })
            foreach (var count in new[] { 2, 3, 6, 8 })
            {
                Begin(size.x, size.y);
                capturePrefix = count + "p-" + size.x + "x" + size.y + "-";
                var root = new GameObject("Durak", typeof(RectTransform)); root.transform.SetParent(canvasObject.transform, false);
                ((RectTransform)root.transform).sizeDelta = size;
                var table = root.AddComponent<DurakTableController>();
                table.Configure(new DurakMatchSettings { PlayerCount = count, DeckSize = count > 6 ? DeckKind.FiftyTwo : DeckKind.ThirtySix }, () => { });
                table.enabled = false; Call(table, "Start"); table.StopAllCoroutines();
                Set(table, "_waitingForHuman", true); Call(table, "Refresh"); Canvas.ForceUpdateCanvases();
                CheckClubSeats(table);
                DurakCheckHand(table, false); Save("opening.png");
                CheckClubLabels(table);
                if (count == 3 || count == 6)
                {
                    CheckDurakLayout(table);
                    CheckClubSeats(table);
                    var names = Get<DurakGame>(table, "_game").Players.Select(p => p.Name).ToArray();
                    DurakShowFixture(table, DurakOccupiedFixture(names, true));
                    CheckClubTouch(table);
                    CheckClubTransfer(table);
                    CheckClubDetails(table);
                    Call(table, "ShowClubSocial"); Save("social.png"); Call(table, "CloseClubPopup");
                    Call(table, "ShowClubSettings"); Save("settings.png"); Call(table, "CloseClubPopup");
                }
                var portraits = Get<Sprite[]>(table, "_playerPortraits");
                Call(table, "StartNewGame"); table.StopAllCoroutines();
                Check(ReferenceEquals(portraits, Get<Sprite[]>(table, "_playerPortraits")), "rematch retains the same portraits");
            }
            File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " checks passed. Durak: 2/3/6/8 players at 1600x900, 1822x820, 1400x820 (safe-area stress), 1920x1080; full table, raised card, large hand, visible labels, stable hand objects, two-tap defense, legal/invalid drops, rematch, menus, persistent trump, hidden card counts, profile identity, public typed chat and history. Memory-only profile.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void CheckClubSeats(DurakTableController table)
    {
        var root = (RectTransform)table.transform;
        var seats = Get<RectTransform>(table, "_opponentsArea").Cast<Transform>().Cast<RectTransform>().ToArray();
        Check(seats.Length == Get<DurakGame>(table, "_game").Players.Count - 1, "every opponent has a seat");
        for (var i = 0; i < seats.Length; i++)
        {
            var bounds = DurakBounds(root, seats[i]);
            Check(DurakInside(root.rect, bounds, 16), "compact seat inside screen");
            Check(!bounds.Overlaps(DurakBounds(root, Get<RectTransform>(table, "_tableRow"))), "portrait is outside the card drop zone");
            for (var j = i + 1; j < seats.Length; j++) Check(!bounds.Overlaps(DurakBounds(root, seats[j])), "seats do not overlap");
            foreach (var card in Get<RectTransform>(table, "_tableRow").GetComponentsInChildren<CardView>())
                Check(!bounds.Overlaps(DurakBounds(root, card.Rect)), "seat clears played cards");
            foreach (var card in root.Find("Trump").GetComponentsInChildren<CardView>())
                Check(!bounds.Overlaps(DurakBounds(root, card.Rect)), "seat clears deck");
            foreach (var label in new[] { "_deckInfo", "_clubDiscardText" })
                Check(!bounds.Overlaps(DurakBounds(root, Get<Text>(table, label).rectTransform)), "seat clears pile label");
            Check(seats[i].GetComponentInChildren<Button>().GetComponent<RectTransform>().rect.width >= 90, "portrait has generous touch target");
        }
    }

    private static void CheckClubRosterIdentity()
    {
        var state = new HubSnapshot(); state.Profile.Id = "self";
        state.People.Add(new HubPerson { Id = "person-a", Name = "Одинаковое имя", AvatarId = "avatar_01", Level = 4, Rating = 1400 });
        state.People.Add(new HubPerson { Id = "person-b", Name = "Одинаковое имя", AvatarId = "avatar_01", Level = 9, Rating = 1900 });
        var room = new HubRoom { Id = "identity-test", GameId = "durak" };
        room.Members.Add(new HubRoomMember { PlayerId = "person-b" });
        room.Members.Add(new HubRoomMember { PlayerId = "self" });
        room.Members.Add(new HubRoomMember { PlayerId = "person-a" }); state.Rooms.Add(room);
        HubRoster.Configure(state, room.Id);
        Check(HubRoster.PlayerIdFor("durak", 0, 3) == "self", "local identity is seat zero regardless of room order");
        Check(HubRoster.PlayerIdFor("durak", 1, 3) == "person-b" && HubRoster.PlayerIdFor("durak", 2, 3) == "person-a", "identical names and portraits do not mix player profiles");
        Check(HubRoster.PlayerIdFor("durak", 1, 6) == null && HubRoster.PlayerIdFor("poker", 1, 3) == null, "unrelated game/roster cannot resolve a profile");
        HubRoster.Clear();
        Check(HubRoster.PlayerIdFor("durak", 1, 3) == null, "leaving a room clears its profile identities");
    }

    private static void CheckClubLabels(DurakTableController table)
    {
        var texts = table.GetComponentsInChildren<Text>();
        foreach (var text in texts.Where(t => t.text == "ДУРАК" || t.text == "ВЫ" || t == Get<Text>(table, "_status") || t.transform.parent.name.StartsWith("Seat ")))
        {
            if (string.IsNullOrEmpty(text.text)) continue;
            Check(text.cachedTextGenerator.vertexCount > 0, "visible rendered label: " + text.text);
        }
        Check(table.transform.Find("button-wood").GetComponent<Image>().sprite != null, "settings button art exists");
    }

    private static void CheckClubDetails(DurakTableController table)
    {
        var game = Get<DurakGame>(table, "_game");
        var topSuit = Get<Text>(table, "_clubTrumpSuit");
        Check(topSuit.text == CardView.SuitText(game.Trump), "permanent emblem shows the actual trump");
        Check(topSuit.fontSize >= 44 && topSuit.gameObject.activeInHierarchy, "trump is large and visible with an empty deck");
        var trumpNames = new Dictionary<Suit, string> { { Suit.Spades, "ПИКИ" }, { Suit.Hearts, "ЧЕРВИ" }, { Suit.Diamonds, "БУБНЫ" }, { Suit.Clubs, "ТРЕФЫ" } };
        Check(Get<Text>(table, "_clubTrumpName").text == trumpNames[game.Trump], "trump suit is named in words");
        Check(!Get<Text>(table, "_deckInfo").text.Any(char.IsDigit) && !Get<Text>(table, "_clubDiscardText").text.Any(char.IsDigit), "deck and discard show no numbers");
        var seats = Get<RectTransform>(table, "_opponentsArea");
        foreach (Transform seat in seats)
        {
            Check(!seat.GetComponentsInChildren<Text>().Any(t => t.text.Any(char.IsDigit)), "seat shows no card count");
            var held = seat.Cast<Transform>().Count(c => c.name == "Held card");
            Check(held == 0 || held == 5, "opponent fan has one size whatever the hand");
            Check(seat.GetComponentInChildren<Button>().GetComponent<RectTransform>().rect.width >= 120, "larger portrait target");
        }
        foreach (var index in new[] { game.AttackerIndex, game.DefenderIndex })
        {
            var seat = index == 0 ? Get<RectTransform>(table, "_playerSeatArea").GetChild(0) : seats.Find("Seat " + index);
            Check(seat.Find("Role glow") != null && seat.Find("Role chip") != null, "fighting player has a soft glow and role plate");
        }
        Check(table.GetComponentsInChildren<Text>().Any(t => t.text == "Защищается"), "defense role uses the requested verb");
        Check(table.GetComponentsInChildren<Text>().Any(t => t.text == "Атакует"), "attack role uses the requested verb");
        seats.GetChild(0).GetComponentInChildren<Button>().onClick.Invoke();
        var popup = Get<RectTransform>(table, "_clubPopup");
        Check(popup.GetComponentsInChildren<Text>().Any(t => t.text == "ПРОФИЛЬ ИГРОКА"), "avatar opens a profile");
        Check(!popup.GetComponentsInChildren<Text>().Any(t => t.text.Contains("Карт в руке")), "profile does not reveal the hand size");
        Check(!popup.GetComponentsInChildren<Text>().Any(t => t.text == "Хороший ход!"), "profile has no targeted-message picker");
        Save("opponent-profile.png"); Call(table, "CloseClubPopup");
        Get<RectTransform>(table, "_playerSeatArea").GetComponentInChildren<Button>().onClick.Invoke();
        Check(Get<RectTransform>(table, "_clubPopup").GetComponentsInChildren<Text>().Any(t => t.text == "МОЙ ПРОФИЛЬ"), "own portrait opens own profile");
        Save("my-profile.png"); Call(table, "CloseClubPopup");
        Set(table, "_clubReactionAt", -10f);
        Call(table, "ShowClubSocial");
        var input = Get<InputField>(table, "_clubChatInput"); input.text = "Всем привет!";
        Get<Button>(table, "_clubChatSend").onClick.Invoke();
        Check(Get<RectTransform>(table, "_clubPopup") == null, "sending returns to the table");
        var bubble = Get<RectTransform>(table, "_clubBubble");
        Check(bubble.GetComponentInChildren<Text>().text.Contains("Всем привет!"), "broadcast appears in public header");
        Check(!bubble.GetComponentInChildren<Text>().text.Contains("→"), "broadcast has no private recipient");
        Call(table, "ShowClubSocial");
        Check(Get<Text>(table, "_clubChatLog").text.Contains("Всем привет!"), "public chat history retains typed messages");
        Check(!Get<Button>(table, "_clubChatSend").interactable, "empty composer cannot send");
        Call(table, "CloseClubPopup");
        Set(table, "_clubDiscardCount", 12); Call(table, "PaintClubDiscard");
        Check(Get<RectTransform>(table, "_clubDiscardCards").GetComponentsInChildren<Image>().Count(i => i.name == "Discard card") == 12, "discard is a real pile of card backs");
        Check(!Get<Text>(table, "_clubDiscardEmpty").gameObject.activeSelf, "empty marker disappears after collecting cards");
        Save("table-details.png");
    }

    private static void CheckClubTouch(DurakTableController table)
    {
        var game = Get<DurakGame>(table, "_game");
        var move = game.GetLegalMoves(0).First(m => m.Type == DurakMoveType.Defend);
        var hand = Get<Dictionary<Card, CardView>>(table, "_clubHand");
        var ids = hand.ToDictionary(p => p.Key, p => p.Value);
        Set(table, "_pendingMove", null); Set(table, "_choiceCard", null); Call(table, "Refresh");
        hand[move.Card].GetComponent<Button>().onClick.Invoke();
        Check(!Get<DurakMove?>(table, "_pendingMove").HasValue, "first tap never commits a move");
        Check(Get<Card?>(table, "_choiceCard") == move.Card, "first tap selects the exact card");
        Call(table, "Refresh");
        Check(ids.All(p => ReferenceEquals(hand[p.Key], p.Value)), "selection preserves hand view identities");
        Check(hand.Values.All(v => v.GetComponent<CanvasGroup>().alpha == 1f), "overlapping hand faces remain opaque even when unplayable");
        hand[move.Card].GetComponent<Button>().onClick.Invoke();
        var pending = Get<DurakMove?>(table, "_pendingMove");
        Check(pending.HasValue && pending.Value.Card == move.Card && pending.Value.TargetSlot == move.TargetSlot, "second tap queues the unique legal defense");
        Check(game.Table.Count(s => !s.Defense.HasValue) == 1, "presentation never bypasses rules to mutate the game");
        Set(table, "_pendingMove", null); Set(table, "_choiceCard", null); Call(table, "Refresh");
        var es = new GameObject("QA events", typeof(EventSystem)).GetComponent<EventSystem>();
        var data = new PointerEventData(es) { position = new Vector2(-10000, -10000) };
        Check(!(bool)Call(table, "DropClubCard", move.Card, data), "off-table drop rejected");
        Check(!Get<DurakMove?>(table, "_pendingMove").HasValue, "invalid drop cannot queue a move");
        // World-space QA canvas is in pixel units; a null event camera interprets these as screen coordinates.
        var slot = Get<RectTransform>(table, "_tableRow").GetChild(move.TargetSlot).GetComponentInChildren<CardView>();
        data.position = slot.Rect.position;
        Check((bool)Call(table, "DropClubCard", move.Card, data), "drop on legal defense target accepted");
        Check(Get<DurakMove?>(table, "_pendingMove").Value.TargetSlot == move.TargetSlot, "drop preserves the chosen slot");
        UnityEngine.Object.DestroyImmediate(es.gameObject);
        Set(table, "_pendingMove", null); Set(table, "_choiceCard", null); Call(table, "Refresh");
    }

    private static void CheckClubTransfer(DurakTableController table)
    {
        var names = Get<DurakGame>(table, "_game").Players.Select(p => p.Name).ToArray();
        DurakGame game = null;
        for (var seed = 0; seed < 500; seed++)
        {
            var candidate = DurakGame.Create(names, Deck.Standard36(seed), new DurakOptions(true, ThrowInMode.All, false));
            if (candidate.DefenderIndex != 0) continue;
            var attack = candidate.Players[candidate.AttackerIndex].Hand.FirstOrDefault(c => candidate.Players[0].Hand.Any(h => h.Rank == c.Rank));
            if (!candidate.TryApplyMove(candidate.AttackerIndex, DurakMove.Attack(attack), out _)) continue;
            if (candidate.GetLegalMoves(0).Any(m => m.Type == DurakMoveType.Transfer)) { game = candidate; break; }
        }
        Check(game != null, "legal transfer fixture found");
        DurakShowFixture(table, game);
        var move = game.GetLegalMoves(0).First(m => m.Type == DurakMoveType.Transfer);
        Call(table, "TapClubCard", move.Card); Call(table, "Refresh"); Canvas.ForceUpdateCanvases();
        var chip = Get<RectTransform>(table, "_handRow").Find("MoveChoice").GetComponent<Button>();
        Check(chip != null, "selected transfer exposes explicit action");
        var root = (RectTransform)table.transform;
        var bounds = DurakBounds(root, chip.GetComponent<RectTransform>());
        Check(!bounds.Overlaps(DurakBounds(root, Get<RectTransform>(table, "_clubDiscard"))), "transfer clears discard pile");
        // «ПЕРЕВЕСТИ» встаёт на место «БИТО»: у защиты её нет.
        foreach (var field in new[] { "_takeButton", "_passButton" })
            if (Get<Button>(table, field).gameObject.activeSelf)
                Check(!bounds.Overlaps(DurakBounds(root, Get<Button>(table, field).GetComponent<RectTransform>())), "transfer clears " + field);
        Save("transfer.png"); chip.onClick.Invoke();
        Check(Get<DurakMove?>(table, "_pendingMove").Value.Type == DurakMoveType.Transfer, "transfer button queues transfer, not defense");
        Check(game.TryApplyMove(0, Get<DurakMove?>(table, "_pendingMove").Value, out _), "queued transfer accepted by rules");
        Set(table, "_pendingMove", null); Set(table, "_choiceCard", null); Call(table, "Refresh");
    }
}
