using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Пробный APK для телефона (2026-10-10): собирается в отдельной копии проекта `C:/CardCityBuild/project`
/// (`scripts/android/build-test-apk.sh`; инструменты Android не принимают пути с не латинскими буквами), открытый
/// редактор и qa-project не трогает. Только горизонтально (интерфейс 1600×900), ARM64, IL2CPP, отладочная
/// подпись Unity — ставится на телефон без магазина. Название и пакет — пробные: настоящие выберет автор к выпуску.
/// </summary>
public static class AndroidTestBuild
{
    public static void Run()
    {
        var output = Environment.GetEnvironmentVariable("ANDROID_APK");
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath("../CardCity-test.apk");
        var android = NamedBuildTarget.Android;
        PlayerSettings.productName = "Card City";
        PlayerSettings.SetApplicationIdentifier(android, "com.cardcity.test");
        // Поворот — только между двумя горизонтальными положениями.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.buildAppBundle = false;
        // Время кадра по процессору и видеокарте — для FrameProbe.cs в диагностической сборке.
        PlayerSettings.enableFrameTimingStats = Environment.GetEnvironmentVariable("ANDROID_PROBE") == "1";
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes, locationPathName = output, target = BuildTarget.Android, options = BuildOptions.None,
        });
        var summary = report.summary;
        Debug.Log("ANDROID TEST BUILD " + summary.result + ": " + output + " · " + (summary.totalSize / 1048576f).ToString("0.0")
            + " MB · " + summary.totalTime.TotalMinutes.ToString("0.0") + " min · errors " + summary.totalErrors);
        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
