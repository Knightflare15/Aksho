using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MobileZipaAssetInstaller
{
    const string ModelFileName = "model.int8.onnx";
    const string TokensFileName = "tokens.txt";
    const string DestinationDirectory = "Assets/StreamingAssets/Zipa";

    [MenuItem("Tools/The Script/Pronunciation/Install Mobile ZIPA Assets")]
    public static void InstallMobileZipaAssets()
    {
        string modelPath = FindCachedFile(ModelFileName);
        string tokensPath = FindCachedFile(TokensFileName);
        if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(tokensPath))
        {
            EditorUtility.DisplayDialog(
                "Mobile ZIPA assets not found",
                "Could not find model.int8.onnx and tokens.txt in the local Hugging Face cache. Run the desktop ZIPA tester once, then retry this installer.",
                "OK");
            return;
        }

        long modelSize = new FileInfo(modelPath).Length;
        bool confirmed = EditorUtility.DisplayDialog(
            "Install Mobile ZIPA assets?",
            $"This will copy ZIPA's int8 ONNX model into {DestinationDirectory}.\n\nModel size: {modelSize / (1024f * 1024f):0.0} MB\n\nThis increases Android build size.",
            "Copy assets",
            "Cancel");
        if (!confirmed)
            return;

        Directory.CreateDirectory(DestinationDirectory);
        File.Copy(modelPath, Path.Combine(DestinationDirectory, ModelFileName), true);
        File.Copy(tokensPath, Path.Combine(DestinationDirectory, TokensFileName), true);
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Mobile ZIPA assets installed",
            $"Copied {ModelFileName} and {TokensFileName} to {DestinationDirectory}.",
            "OK");
    }

    static string FindCachedFile(string fileName)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = Path.Combine(home, ".cache", "huggingface", "hub", "models--anyspeech--zipa-large-crctc-ns-800k", "snapshots");
        if (!Directory.Exists(root))
            return "";

        foreach (string path in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
            return path;

        return "";
    }
}
