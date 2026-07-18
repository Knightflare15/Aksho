using System;
using UnityEngine;

public static class AccessibilitySettings
{
    const string VibrationKey = "accessibility.vibration";
    const string ShakeKey = "accessibility.shakeIntensity";
    const string SpeechLanguageKey = "accessibility.speechLanguage";

    public static event Action Changed;

    public static bool VibrationEnabled
    {
        get => PlayerPrefs.GetInt(VibrationKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
            SaveAndNotify();
        }
    }

    public static float ShakeIntensity
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(ShakeKey, 1f));
        set
        {
            PlayerPrefs.SetFloat(ShakeKey, Mathf.Clamp01(value));
            SaveAndNotify();
        }
    }

    public static string SpeechLanguage
    {
        get
        {
            string value = PlayerPrefs.GetString(SpeechLanguageKey, "en-US");
            return string.IsNullOrWhiteSpace(value) ? "en-US" : value.Trim();
        }
        set
        {
            PlayerPrefs.SetString(SpeechLanguageKey, string.IsNullOrWhiteSpace(value) ? "en-US" : value.Trim());
            SaveAndNotify();
        }
    }

    static void SaveAndNotify()
    {
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
