// GPU checks for the real scenery shaders. Companion for the isolated PlayerHubQa fixture.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class PlayerHubQa
{
    private static void CheckSceneryCurve()
    {
        var stillShader = Resources.Load<Shader>("Shaders/HubScenery");
        var videoShader = Resources.Load<Shader>("Shaders/HubVideoMask");
        Check(stillShader != null && stillShader.isSupported && !ShaderUtil.ShaderHasError(stillShader),
            "GPU scenery curve: still shader compiles and is supported");
        Check(videoShader != null && videoShader.isSupported && !ShaderUtil.ShaderHasError(videoShader),
            "GPU scenery curve: video shader compiles and is supported");

        Material still = null, video = null;
        Texture2D ramp = null, colours = null, mask = null;
        try
        {
            still = new Material(stillShader);
            video = new Material(videoShader);
            ramp = SceneryProbeTexture(256, 32, (x, y) => new Color(x / 255f, x / 255f, x / 255f, 1));
            var swatches = new[] { Color.white, new Color(1, .62f, .08f), new Color(.12f, .45f, 1),
                new Color(.72f, .08f, .45f), new Color(.09f, .07f, .04f), new Color(.4f, .4f, .4f) };
            colours = SceneryProbeTexture(swatches.Length * 8, 8, (x, y) => swatches[x / 8]);
            // Linear mask data: 50% coverage must remain 50% in both project colour spaces.
            mask = SceneryProbeTexture(2, 2, (x, y) => Color.white, true);
            video.SetTexture("_MaskTex", mask);

            still.SetFloat("_SceneryTone", 0);
            var original = RenderSceneryProbe(still, ramp, Color.white);
            still.SetFloat("_SceneryTone", 1);
            var softened = RenderSceneryProbe(still, ramp, Color.white);
            var row = 16 * ramp.width;
            Check(original[row].r < .003f && original[row + 255].r > .98f,
                "GPU scenery curve: opaque ramp actually renders black through white");
            var neutral = true;
            var monotonic = true;
            var darksUnchanged = true;
            for (var x = 0; x < ramp.width; x++)
            {
                var c = softened[row + x];
                neutral &= SceneryFinite(c) && Mathf.Abs(c.r - c.g) < .006f && Mathf.Abs(c.g - c.b) < .006f;
                if (x > 0) monotonic &= c.r + .002f >= softened[row + x - 1].r;
                // These input values are below the shoulder in gamma and linear projects.
                if (x <= 64) darksUnchanged &= Mathf.Abs(c.r - original[row + x].r) < .006f;
            }
            Check(neutral, "GPU scenery curve: grayscale stays neutral and finite (no magenta fallback)");
            Check(monotonic && softened[row + 192].r > softened[row + 96].r + .03f,
                "GPU scenery curve: brightness remains ordered with distinct midtones");
            Check(darksUnchanged && softened[row].r < .003f && softened[row + 64].r > .015f,
                "GPU scenery curve: black ink and dark detail are neither lifted nor crushed");
            var white = softened[row + 255].r;
            Check(white < original[row + 255].r * .87f && white > original[row + 255].r * .4f,
                "GPU scenery curve: strongest highlights are softened, not blackened: " + white);

            video.SetFloat("_SceneryTone", 1);
            var stillColours = RenderSceneryProbe(still, colours, Color.white);
            var videoColours = RenderSceneryProbe(video, colours, Color.white);
            Check(SceneryPixelsNear(stillColours, videoColours, 1, .012f),
                "GPU scenery curve: still and masked video share the same colour treatment");
            var fadedStill = RenderSceneryProbe(still, colours, new Color(1, 1, 1, .35f));
            var fadedVideo = RenderSceneryProbe(video, colours, new Color(1, 1, 1, .35f));
            Check(SceneryPixelsNear(fadedStill, stillColours, .35f, .012f)
                && SceneryPixelsNear(fadedVideo, videoColours, .35f, .012f),
                "GPU scenery curve: tone is applied before opacity on both layers");
            mask.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
            mask.Apply(false, false);
            var halfMask = RenderSceneryProbe(video, colours, Color.white);
            Check(SceneryPixelsNear(halfMask, stillColours, .5f, .012f),
                "GPU scenery curve: video mask only changes coverage, not the graded colours");

            Check(still.HasProperty("_RoadReflections"), "GPU scenery curve: street-only reflection control exists");
            still.SetFloat("_RoadReflections", .32f);
            var street = RenderSceneryProbe(still, ramp, Color.white);
            var bottom = 3 * ramp.width;
            var top = (ramp.height - 4) * ramp.width;
            Check(street[bottom + 255].r < softened[bottom + 255].r - .025f
                && Mathf.Abs(street[top + 255].r - softened[top + 255].r) < .01f,
                "GPU scenery curve: street highlights are reduced only near the bottom");
            Check(Mathf.Abs(street[bottom + 24].r - softened[bottom + 24].r) < .006f,
                "GPU scenery curve: the street treatment preserves dark paving detail");
            still.SetFloat("_SceneryTone", 0);
            var bypass = RenderSceneryProbe(still, ramp, Color.white);
            Check(SceneryPixelsNear(bypass, original, 1, .006f),
                "GPU scenery curve: original-art switch also bypasses street attenuation");
        }
        finally
        {
            if (still != null) UnityEngine.Object.DestroyImmediate(still);
            if (video != null) UnityEngine.Object.DestroyImmediate(video);
            if (ramp != null) UnityEngine.Object.DestroyImmediate(ramp);
            if (colours != null) UnityEngine.Object.DestroyImmediate(colours);
            if (mask != null) UnityEngine.Object.DestroyImmediate(mask);
        }
    }

    private static Texture2D SceneryProbeTexture(int width, int height, Func<int, int, Color> pixel, bool linear = false)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) pixels[y * width + x] = pixel(x, y);
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static Color[] RenderSceneryProbe(Material material, Texture2D source, Color tint)
    {
        // No scene objects or existing camera state are touched. Explicit vertex colour avoids
        // Graphics.Blit's undefined COLOR input for a UI shader; linear RT readback avoids a second transfer curve.
        var previousTarget = RenderTexture.active;
        var previousSrgbWrite = GL.sRGBWrite;
        var target = RenderTexture.GetTemporary(source.width, source.height, 24,
            RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        Texture2D readback = null;
        try
        {
            material.SetTexture("_MainTex", source);
            material.SetColor("_Color", tint);
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            RenderTexture.active = target;
            GL.sRGBWrite = false;
            GL.Clear(true, true, Color.clear);
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                if (!material.SetPass(0)) throw new InvalidOperationException("Scenery probe shader pass is unavailable.");
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                GL.TexCoord3(0, 0, 0); GL.Vertex3(0, 0, 0);
                GL.TexCoord3(1, 0, 0); GL.Vertex3(1, 0, 0);
                GL.TexCoord3(1, 1, 1); GL.Vertex3(1, 1, 0);
                GL.TexCoord3(0, 1, 1); GL.Vertex3(0, 1, 0);
                GL.End();
            }
            finally { GL.PopMatrix(); }
            readback = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
            readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            readback.Apply(false, false);
            return readback.GetPixels();
        }
        finally
        {
            RenderTexture.active = previousTarget;
            GL.sRGBWrite = previousSrgbWrite;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static bool SceneryFinite(Color c) => !float.IsNaN(c.r) && !float.IsNaN(c.g) && !float.IsNaN(c.b)
        && !float.IsInfinity(c.r) && !float.IsInfinity(c.g) && !float.IsInfinity(c.b);

    private static bool SceneryPixelsNear(Color[] actual, Color[] reference, float factor, float tolerance)
    {
        if (actual.Length != reference.Length) return false;
        for (var i = 0; i < actual.Length; i++)
        {
            if (!SceneryFinite(actual[i]) || Mathf.Abs(actual[i].r - reference[i].r * factor) > tolerance
                || Mathf.Abs(actual[i].g - reference[i].g * factor) > tolerance
                || Mathf.Abs(actual[i].b - reference[i].b * factor) > tolerance) return false;
        }
        return true;
    }
}
