#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds a release AAB only when signing material is supplied by CI or the local secure environment.</summary>
public static class AndroidReleaseBuilder
{
    const string OutputPath = "Builds/Android/TheScript-release.aab";

    [MenuItem("The Script/Build Android Signed Release AAB")]
    public static void BuildSignedReleaseAab()
    {
        ProductionBuildValidator.ThrowIfStrictReleaseBlocked();
        string keyStorePath = RequiredEnvironment("THE_SCRIPT_ANDROID_KEYSTORE_PATH");
        string keyStorePassword = RequiredEnvironment("THE_SCRIPT_ANDROID_KEYSTORE_PASSWORD");
        string keyAliasName = RequiredEnvironment("THE_SCRIPT_ANDROID_KEY_ALIAS_NAME");
        string keyAliasPassword = RequiredEnvironment("THE_SCRIPT_ANDROID_KEY_ALIAS_PASSWORD");
        if (!File.Exists(keyStorePath))
            throw new BuildFailedException($"Android keystore was not found: {keyStorePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        AndroidDebugApkBuilder.ConfigureLocalAndroidSdk();

        bool originalUseCustomKeyStore = PlayerSettings.Android.useCustomKeystore;
        string originalKeyStore = PlayerSettings.Android.keystoreName;
        string originalAlias = PlayerSettings.Android.keyaliasName;
        string originalKeyStorePassword = PlayerSettings.Android.keystorePass;
        string originalAliasPassword = PlayerSettings.Android.keyaliasPass;
        bool originalBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
        bool originalDevelopment = EditorUserBuildSettings.development;
        bool originalDebugging = EditorUserBuildSettings.allowDebugging;

        try
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keyStorePath;
            PlayerSettings.Android.keystorePass = keyStorePassword;
            PlayerSettings.Android.keyaliasName = keyAliasName;
            PlayerSettings.Android.keyaliasPass = keyAliasPassword;
            EditorUserBuildSettings.buildAppBundle = true;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Signed Android AAB build failed: {report.summary.result}");

            Debug.Log($"[AndroidReleaseBuilder] Built signed {OutputPath} ({report.summary.totalSize / (1024f * 1024f):0.0} MB).");
        }
        finally
        {
            // Keep credentials out of project settings and source control after a local build.
            PlayerSettings.Android.useCustomKeystore = originalUseCustomKeyStore;
            PlayerSettings.Android.keystoreName = originalKeyStore;
            PlayerSettings.Android.keystorePass = originalKeyStorePassword;
            PlayerSettings.Android.keyaliasName = originalAlias;
            PlayerSettings.Android.keyaliasPass = originalAliasPassword;
            EditorUserBuildSettings.buildAppBundle = originalBuildAppBundle;
            EditorUserBuildSettings.development = originalDevelopment;
            EditorUserBuildSettings.allowDebugging = originalDebugging;
        }
    }

    static string RequiredEnvironment(string name)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        if (string.IsNullOrWhiteSpace(value))
            throw new BuildFailedException($"Set {name} in the secure build environment before building a signed Android release.");
        return value;
    }
}
#endif
