using System;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static async Task CheckMailVisual()
    {
        hub.Navigate("chats"); await NextFrame(); Call(hub, "UpdateTavernMail", 1f, false);
        Canvas.ForceUpdateCanvases();
        var art = Get<RectTransform>(hub, "_mailCase").GetComponent<Image>().sprite;
        Check(art.texture.width == 1476 && art.texture.height == 642, "mail artwork retains original 1476x642 pixels in Unity");
        Check(PlayerHubRuntime.Current.Friends.Count(id => id.StartsWith("bot-mail-")) == 6, "six new local contacts are already available");
        var view = Get<RectTransform>(hub, "_mailFriendsView");
        var list = Get<RectTransform>(hub, "_mailFriends");
        var scroll = view.GetComponent<HubSmoothScroll>();
        var bar = Get<HubDragScrollbar>(hub, "_mailFriendsScrollbar");
        Check(list.childCount >= 7 && list.rect.height > view.rect.height && bar.gameObject.activeInHierarchy, "friend list overflows and offers a visible scrollbar");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate");
        var startY = list.anchoredPosition.y;
        ExecuteEvents.Execute(view.gameObject, new PointerEventData(events) { scrollDelta = new Vector2(0, -1) }, ExecuteEvents.scrollHandler);
        Check(Mathf.Abs(list.anchoredPosition.y - startY) < .01f, "wheel does not jump immediately");
        for (var i = 0; i < 30; i++) Call(scroll, "AdvanceWheel", .02f);
        Check(list.anchoredPosition.y > startY + 10 && list.anchoredPosition.y < startY + 31, "wheel advances smoothly");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate");
        var point = RectTransformUtility.WorldToScreenPoint(camera, view.TransformPoint(view.rect.center));
        var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = point, pressPosition = point,
            pointerPressRaycast = new RaycastResult { module = canvasObject.GetComponent<GraphicRaycaster>() } };
        ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.position += Vector2.up * 140;
        ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.endDragHandler);
        scroll.StopMovement(); Check(list.anchoredPosition.y > startY + 50, "finger drag moves the left contact list");
        scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate"); Canvas.ForceUpdateCanvases();
        var hit = Hit(bar.handleRect);
        Check(hit.gameObject != null && ExecuteEvents.GetEventHandler<IDragHandler>(hit.gameObject) == bar.gameObject, "gold handle receives the pointer");
        var grab = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = hit.screenPosition,
            pressPosition = hit.screenPosition, pointerCurrentRaycast = hit, pointerPressRaycast = hit };
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.beginDragHandler);
        grab.position -= Vector2.up * 160;
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.endDragHandler);
        Check(scroll.verticalNormalizedPosition < .8f, "holding and dragging the handle scrolls the list");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 0; Call(scroll, "LateUpdate"); Canvas.ForceUpdateCanvases();
        Save("mail-list-bottom.png");
        var position = list.anchoredPosition.y;
        await ClickButton(list.Find("Letter bot-mail-mark").GetComponent<Button>(), "Марк at bottom of list");
        Check(Get<string>(hub, "_page") == "chat:bot-mail-mark" && Mathf.Abs(list.anchoredPosition.y - position) < 1,
            "bottom contact opens without resetting the list");
        await Click("Сыграем?");
        await WaitFor(() => PlayerHubRuntime.Current.Chats.Count(m => m.ConversationId == "bot-mail-mark") >= 3, "local reply");
        Check(PlayerHubRuntime.Current.Chats.Last(m => m.ConversationId == "bot-mail-mark").FromId == "bot-mail-mark", "added bot replies in its own conversation");
        foreach (var size in new[] { new Vector2Int(1600,900), new Vector2Int(2000,900), new Vector2Int(960,540) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); Call(hub, "UpdateTavernMail", 1f, false);
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate");
            InspectLayout("mail-" + size.x + "x" + size.y); Save("mail-" + size.x + "x" + size.y + ".png");
        }
        Check(LayoutIssues.Count == 0, string.Join("\n", LayoutIssues));
    }
}
