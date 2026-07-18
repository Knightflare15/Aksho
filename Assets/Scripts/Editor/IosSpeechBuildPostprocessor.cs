#if UNITY_EDITOR && UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class IosSpeechBuildPostprocessor
{
    [PostProcessBuild]
    public static void AddSpeechConfiguration(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSMicrophoneUsageDescription", "Voice input lets you say spell words.");
        plist.root.SetString("NSSpeechRecognitionUsageDescription", "Speech recognition identifies spoken spell words.");
        plist.WriteToFile(plistPath);

        string projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        string targetGuid = project.GetUnityMainTargetGuid();
        project.AddFrameworkToProject(targetGuid, "Speech.framework", false);
        project.AddFrameworkToProject(targetGuid, "AVFoundation.framework", false);
        project.WriteToFile(projectPath);
    }
}
#endif
