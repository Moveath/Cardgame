// Companion for PlayerHubQa; phone-only renders and interaction checks, using its memory store.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGameClient;
using UnityEngine;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static async Task CheckPhoneHubLayout()
    {
        Check(PhoneUi, "phone hub layout runs with the phone density override");
        await WaitFor(() => (bool)Call(hub, "EnsureFloor", 2) && (bool)Call(hub, "EnsureFloor", 6),
            "club and tavern art loads");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null)).Cast<object>().ToArray();
        var area = Get<RectTransform>(hub, "_cityArea");
        object Place(string id) => places.Single(place => (string)Item(place, "Id") == id);
        Vector2 PlaceScreen(object place) => RectTransformUtility.WorldToScreenPoint(camera,
            area.TransformPoint(area.rect.center + (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"))));
        foreach (var video in hub.GetComponentsInChildren<HubClubVideo>(true)) video.Stopped = true;

        foreach (var width in new[] { 1600, 2000 })
        {
            hub.GoHome(); await NextFrame();
            Call(hub, "EndVisitNow");
            if (Get<int>(hub, "_cityLevel") != 0) DiveNow(0);
            ResizeCanvas(width, 900);
            ((RectTransform)hub.transform).sizeDelta = new Vector2(width, 900);
            Call(hub, "Resize");
            DiveNow(2); await NextFrame();
            Call(hub, "UpdateInteriorControls");
            var floor = FloorRect(2);
            var baseScale = Mathf.Max(area.rect.width / floor.rect.width, area.rect.height / floor.rect.height);
            Check((float)Call(hub, "FloorScale", 2) >= baseScale * 1.15f,
                "club residents are noticeably closer on phone " + width);
            PhoneCheckControl("ВЫЙТИ", 240, 88, 40);
            PhoneCheckClubVideoAlignment();

            Swipe(area, 10f);
            var limit = (float)Call(hub, "FloorPanLimit", 2);
            Check(limit > 0 && Mathf.Abs(Get<float>(hub, "_floorPan") + limit) < .5f && CityCovered(),
                "left swipe reaches the club entrance without an uncovered edge " + width);
            Save("phone-club-left-" + width + ".png");
            Swipe(area, -10f);
            Check(Mathf.Abs(Get<float>(hub, "_floorPan") - limit) < .5f && CityCovered(),
                "right swipe reaches the final club table without an uncovered edge " + width);
            Save("phone-club-right-" + width + ".png");

            foreach (var game in new[] { "durak", "bluff", "chests", "govno" })
            {
                var place = Place("table-" + game);
                Call(hub, "LookAtPlace", place);
                Call(hub, "UpdateInteriorControls");
                Canvas.ForceUpdateCanvases();
                var title = ClubTableName(place);
                Check(PhoneInside(title), "the full floor title is reachable after panning: " + game + " " + width);
                var point = (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"));
                Check((int)Call(hub, "PlaceAt", point) == Array.IndexOf(places, place),
                    "enlarged table art and hit region agree: " + game + " " + width);
                // Tap the physical table, rather than navigating directly to its route.
                TapScreen(area, PlaceScreen(place), "phone club " + game);
                DriveTableTrip(game, "phone " + width + " " + game); await NextFrame();
                Call(hub, "UpdateInteriorControls");
                Save("phone-entrance-" + game + "-" + width + ".png");
                CheckPlaqueBesideTable(game);
                PhoneCheckControl("ОТОЙТИ", 240, 88, 40);
                PhoneCheckControl("Правила", 184, 88, 34);
                var plaque = (RectTransform)Get<RectTransform>(hub, "_content").Find("Table plaque");
                var away = (RectTransform)ActiveButtons().Single(button => ButtonLabel(button) == "ОТОЙТИ").transform;
                Check(!PhoneWorldRect(away).Overlaps(PhoneWorldRect(plaque)),
                    "step-away control does not cover the settings plaque: " + game + " " + width);
                if (game == "govno")
                    Check(PhoneWorldRect(plaque).xMax < PhoneWorldRect(away).xMin && CityCovered(),
                        "right-wall table uses a left plaque with an unbroken painted background " + width);
                InspectLayout("phone-entrance-" + game + "-" + width);
                Save("phone-entrance-" + game + "-" + width + ".png");

                await Click("Правила");
                Check(Get<string>(hub, "_page") == "rules:" + game, "rules open from phone table " + game);
                PhoneCheckControl("НАЗАД", 240, 88, 40);
                var scroll = Get<RectTransform>(hub, "_content").GetComponentsInChildren<ScrollRect>()
                    .Single(item => item.name == "Scroll rules:" + game);
                var body = scroll.content.GetComponentsInChildren<Text>().Where(text => text.text.Length > 70).ToArray();
                Check(body.Length > 0 && body.All(text => text.fontSize >= 30 && !text.resizeTextForBestFit
                    && text.preferredHeight <= text.rectTransform.rect.height + 2),
                    "rules stay large and fully laid out: " + game + " " + width);
                scroll.verticalNormalizedPosition = 1; await NextFrame();
                InspectLayout("phone-rules-top-" + game + "-" + width);
                Save("phone-rules-" + game + "-" + width + ".png");
                scroll.verticalNormalizedPosition = 0; await NextFrame();
                InspectLayout("phone-rules-end-" + game + "-" + width);
                if (game == "durak") Save("phone-rules-end-" + width + ".png");
                await Click("Вернуться к столу");
                Check(Get<string>(hub, "_page") == "game:" + game, "rules return to the same phone table " + game);
                await Click("ОТОЙТИ");
                for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") != 0; frame++)
                    Call(hub, "MoveVisit", 1 / 60f);
                Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_visitPhase") == 0,
                    "enlarged step-away control returns to the hall: " + game);
            }

            DiveNow(0); DiveNow(6); await NextFrame();
            Set(hub, "_tavernGreeted", false);
            Set(hub, "_tavernMet", false);
            Call(hub, "UpdateInteriorControls");
            Call(hub, "UpdateTavern", .01f);
            var queen = (Text)Call(hub, "TalkLine", "tavern-queen");
            Check(queen.text.Length > 100 && queen.text.Contains("Червовую даму"), "long first tavern greeting is under test");
            PhoneCheckSpeech(queen, "queen " + width);
            InspectLayout("phone-tavern-greeting-" + width);
            Save("phone-tavern-greeting-" + width + ".png");
            Call(hub, "UpdateTavern", 5f);
            Check(queen.gameObject.activeInHierarchy && queen.GetComponentInParent<CanvasGroup>().alpha > .95f,
                "long phone greeting remains readable beyond the previous short timeout");
            // The Five speaks from the left of the room; verify both camera extremes, too.
            Call(hub, "TavernTalk", "tavern-five");
            var five = (Text)Call(hub, "TalkLine", "tavern-five");
            Check(!queen.gameObject.activeInHierarchy, "only the current tavern speaker has a visible bubble");
            foreach (var edge in new[] { 0f, 1f })
            {
                Call(hub, "LookAtPicture", edge);
                Call(hub, "UpdateTavern", .01f);
                PhoneCheckSpeech(five, "five " + width + " pan " + edge);
                InspectLayout("phone-tavern-five-" + width + "-" + edge);
                Save("phone-tavern-five-" + width + "-" + (edge == 0 ? "left" : "right") + ".png");
            }
            Call(hub, "UpdateTavern", 14f);
            Check(!five.gameObject.activeInHierarchy, "phone speech still fades after sufficient reading time");
        }
        File.WriteAllLines(Path.Combine(Output, "phone-layout-issues.txt"), LayoutIssues);
        Check(LayoutIssues.Count == 0, "phone hub controls and text fit: " + string.Join(" | ", LayoutIssues));
        Check(RuntimeErrors.Count == 0, "phone hub layout has no runtime errors: " + string.Join(" | ", RuntimeErrors));
    }

    private static void PhoneCheckControl(string caption, float minWidth, float minHeight, int minFont)
    {
        var button = ActiveButtons().Single(item => ButtonLabel(item) == caption);
        var rect = (RectTransform)button.transform;
        var label = button.GetComponentInChildren<Text>();
        Check(rect.rect.width >= minWidth - .1f && rect.rect.height >= minHeight - .1f && label.fontSize >= minFont,
            "phone control is large enough: " + caption);
        Check(PhoneInside(rect), "phone control remains inside the viewport: " + caption);
        var hit = Hit(rect);
        Check(hit.gameObject != null && (hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
            "phone control receives its own tap: " + caption);
    }

    private static void PhoneCheckSpeech(Text line, string label)
    {
        Canvas.ForceUpdateCanvases();
        var bubble = (RectTransform)line.transform.parent;
        Check(line.gameObject.activeInHierarchy && !line.resizeTextForBestFit && line.fontSize >= 40,
            "phone speech keeps a readable font: " + label);
        Check(line.preferredHeight <= line.rectTransform.rect.height + 2 && PhoneInside(bubble),
            "the entire speech fits its paper and the viewport: " + label);
        var leave = (RectTransform)ActiveButtons().Single(button => ButtonLabel(button) == "ВЫЙТИ").transform;
        Check(!PhoneWorldRect(bubble).Overlaps(PhoneWorldRect(leave)), "speech does not overlap the exit: " + label);
    }

    private static void PhoneCheckClubVideoAlignment()
    {
        var floor = FloorRect(2);
        var art = floor.GetComponent<HubCityArt>();
        Check(art != null && art.Art != null, "phone club retains the painted backdrop");
        var definitions = ((string Place, string Clip, float X, float Y, float W, float H)[])
            typeof(PlayerHubController).GetField("ClubVideos", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (var item in definitions)
        {
            var video = floor.GetComponentsInChildren<HubClubVideo>(true).Single(candidate => candidate.name == "Video " + item.Place);
            var rect = (RectTransform)video.transform;
            var topLeft = floor.TransformPoint(new Vector3((item.X / 1916f - .5f) * floor.rect.width,
                (.5f - item.Y / 821f) * floor.rect.height, 0));
            var bottomRight = floor.TransformPoint(new Vector3(((item.X + item.W) / 1916f - .5f) * floor.rect.width,
                (.5f - (item.Y + item.H) / 821f) * floor.rect.height, 0));
            var actual = PhoneWorldRect(rect);
            Check(Vector2.Distance(new Vector2(actual.xMin, actual.yMax), topLeft) < .1f
                && Vector2.Distance(new Vector2(actual.xMax, actual.yMin), bottomRight) < .1f,
                "phone zoom keeps masked video registered with its painted resident: " + item.Place);
        }
    }

    private static Rect PhoneWorldRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y),
            corners.Max(point => point.x), corners.Max(point => point.y));
    }

    private static bool PhoneInside(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        return corners.Select(corner => camera.WorldToViewportPoint(corner)).All(point =>
            point.x >= -.001f && point.x <= 1.001f && point.y >= -.001f && point.y <= 1.001f);
    }
}
