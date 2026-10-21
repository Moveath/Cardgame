// Companion for PlayerHubQa. Native rendered comparisons with memory-only persistence.
using System.Linq;
using System.Threading.Tasks;
using CardGameClient;
using UnityEngine;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static async Task CheckScenery()
    {
        var shader = Resources.Load<Shader>("Shaders/HubScenery");
        Check(shader != null && shader.isSupported, "scenery shader loads and is supported");
        Check(!UnityEditor.ShaderUtil.ShaderHasError(shader), "scenery shader compiles");
        var videoShader = Resources.Load<Shader>("Shaders/HubVideoMask");
        Check(videoShader != null && !UnityEditor.ShaderUtil.ShaderHasError(videoShader), "masked video shares a compilable tone shader");
        CheckSceneryCurve();
        for (var district = 0; district < 4; district++)
        {
            SettleOn(district); await NextFrame();
            SceneryPair("city-" + district);
        }
        foreach (var level in new[] { 2, 3, 4, 6 })
        {
            if (Get<int>(hub, "_cityLevel") != 0) DiveNow(0);
            DiveNow(level); await NextFrame();
            Call(hub, "UpdateInteriorControls");
            // Freeze the same poses for both stills; do not compare frames from different animation times.
            foreach (var video in hub.GetComponentsInChildren<HubClubVideo>(true)) video.Stopped = true;
            SceneryPair("interior-" + level);
            Check(FloorRect(level).GetComponentInChildren<HubCityArt>().material.shader == shader,
                "interior uses the same scenery treatment: " + level);
        }
        hub.Navigate("fortune"); await NextFrame();
        SceneryPair("fortune");
        Check(Get<RectTransform>(hub, "_fortuneTent").GetComponent<Image>().material.shader == shader,
            "fortune screen matches its interior");
        hub.Navigate("chats"); await NextFrame(); Call(hub, "UpdateTavernMail", 1f, false);
        SceneryPair("mail");
        Check(Get<Image>(hub, "_mailBackdrop").material.shader == shader, "mail backdrop is toned");
        Check(Get<RectTransform>(hub, "_mailCase").GetComponent<Image>().material.shader != shader,
            "mail paper and controls keep their original material");
        hub.GoHome(); await NextFrame(); DiveNow(0); DiveNow(4);
        Call(hub, "ShowBankLoan");
        Call(hub, "UpdateBankLoan", 1f, false);
        SceneryPair("bank-desk");
        Check(Get<RectTransform>(hub, "_bankLoanDesk").GetComponent<Image>().material.shader == shader,
            "bank desk uses the same scenery treatment");
    }

    private static void SceneryPair(string name)
    {
        var scenery = hub.GetComponentsInChildren<Graphic>(true)
            .Select(graphic => graphic.material).Where(material => material != null && material.HasProperty("_SceneryTone"))
            .Distinct().ToArray();
        Check(scenery.Length > 0, "scenery material is present: " + name);
        foreach (var material in scenery) material.SetFloat("_SceneryTone", 0);
        Save(name + "-before.png");
        foreach (var material in scenery) material.SetFloat("_SceneryTone", 1);
        Save(name + "-after.png");
        Check(hub.GetComponentsInChildren<Text>(true).All(text => !text.material.HasProperty("_SceneryTone")),
            "text is not colour-graded: " + name);
    }
}
