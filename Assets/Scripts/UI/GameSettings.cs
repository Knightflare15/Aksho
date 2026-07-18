using System;
using UnityEngine;
using UnityEngine.InputSystem;

public static class GameSettings
{
    const string BindingOverridesKey = "settings.controlBindings";
    const string MicrophoneKey = "settings.microphone";
    const string MasterVolumeKey = "settings.masterVolume";
    const string FullscreenKey = "settings.fullscreen";
    const string ResolutionWidthKey = "settings.resolutionWidth";
    const string ResolutionHeightKey = "settings.resolutionHeight";
    const string HandwritingDevDiagnosticsKey = "settings.handwritingDevDiagnostics";
    const string TouchControlsKey = "settings.touchControls";

    public static event Action ControlsChanged;
    public static event Action<bool> HandwritingDevDiagnosticsChanged;

    public static string SelectedMicrophone
    {
        get => PlayerPrefs.GetString(MicrophoneKey, "");
        set
        {
            PlayerPrefs.SetString(MicrophoneKey, value ?? "");
            PlayerPrefs.Save();
        }
    }

    public static float MasterVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
        set
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterVolumeKey, AudioListener.volume);
            PlayerPrefs.Save();
        }
    }

    public static bool HandwritingDevDiagnosticsVisible
    {
        get => PlayerPrefs.GetInt(HandwritingDevDiagnosticsKey, 0) != 0;
        set
        {
            bool normalized = value;
            bool previous = HandwritingDevDiagnosticsVisible;
            PlayerPrefs.SetInt(HandwritingDevDiagnosticsKey, normalized ? 1 : 0);
            PlayerPrefs.Save();
            if (previous != normalized)
                HandwritingDevDiagnosticsChanged?.Invoke(normalized);
        }
    }

    public static bool TouchControlsEnabled
    {
        get => PlayerPrefs.GetInt(TouchControlsKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(TouchControlsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static void ApplySavedDisplaySettings()
    {
        AudioListener.volume = MasterVolume;
        if (!PlayerPrefs.HasKey(ResolutionWidthKey))
            return;

        int width = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width);
        int height = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height);
        bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
        Screen.SetResolution(width, height, fullscreen);
    }

    public static void SetResolution(Resolution resolution, bool fullscreen)
    {
        Screen.SetResolution(resolution.width, resolution.height, fullscreen);
        PlayerPrefs.SetInt(ResolutionWidthKey, resolution.width);
        PlayerPrefs.SetInt(ResolutionHeightKey, resolution.height);
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void ApplyBindingOverrides(InputActionAsset asset)
    {
        if (asset == null)
            return;

        string json = PlayerPrefs.GetString(BindingOverridesKey, "");
        if (!string.IsNullOrWhiteSpace(json))
            asset.LoadBindingOverridesFromJson(json);
    }

    public static void SaveBindingOverrides(InputActionAsset asset)
    {
        if (asset == null)
            return;

        PlayerPrefs.SetString(BindingOverridesKey, asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
        ControlsChanged?.Invoke();
    }

    public static void ResetBindingOverrides(InputActionAsset asset)
    {
        asset?.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(BindingOverridesKey);
        PlayerPrefs.Save();
        ControlsChanged?.Invoke();
    }
}
