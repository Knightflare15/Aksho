#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AndroidDebugApkBuilder
{
    const string OutputPath = "Builds/Android/TheScript-debug.apk";
    const string PackageId = "com.thescript.game";
    const string LocalAndroidSdkPath = "Builds/AndroidSDK";

    [MenuItem("The Script/Build Android Debug APK")]
    public static void BuildDebugApk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        ConfigureLocalAndroidSdk();

        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
        PlayerSettings.Android.bundleVersionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode);
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = true;
        EditorUserBuildSettings.allowDebugging = true;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"Android debug APK build failed: {summary.result}");

        Debug.Log($"[AndroidDebugApkBuilder] Built {OutputPath} ({summary.totalSize / (1024f * 1024f):0.0} MB)");
    }

    internal static bool TryGetLocalAndroidSdkPath(out string sdkPath)
    {
        sdkPath = Path.GetFullPath(LocalAndroidSdkPath);
        return Directory.Exists(sdkPath);
    }

    internal static void ConfigureLocalAndroidSdk()
    {
        if (!TryGetLocalAndroidSdkPath(out string sdkPath))
            return;

        System.Environment.SetEnvironmentVariable("ANDROID_HOME", sdkPath);
        System.Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", sdkPath);
    }

    internal static string EscapeGradlePropertyPath(string path)
    {
        return path.Replace('\\', '/').Replace(":", "\\:");
    }
}

public sealed class AndroidDebugGradleSdkPostprocessor : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        AndroidDebugApkBuilder.ConfigureLocalAndroidSdk();
        if (!AndroidDebugApkBuilder.TryGetLocalAndroidSdkPath(out string sdkPath))
            return;

        string gradleRoot = Directory.GetParent(path)?.FullName;
        if (string.IsNullOrEmpty(gradleRoot))
            return;

        File.WriteAllText(
            Path.Combine(gradleRoot, "local.properties"),
            $"sdk.dir={AndroidDebugApkBuilder.EscapeGradlePropertyPath(sdkPath)}\n");

        string gradlePropertiesPath = Path.Combine(gradleRoot, "gradle.properties");
        if (!File.Exists(gradlePropertiesPath))
            return;

        string normalizedSdkPath = sdkPath.Replace('\\', '/');
        string[] lines = File.ReadAllLines(gradlePropertiesPath);
        bool replaced = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("unity.androidSdkPath="))
                continue;

            lines[i] = $"unity.androidSdkPath={normalizedSdkPath}";
            replaced = true;
        }

        if (!replaced)
            lines = lines.Append($"unity.androidSdkPath={normalizedSdkPath}").ToArray();

        File.WriteAllLines(gradlePropertiesPath, lines);
    }
}
#endif
