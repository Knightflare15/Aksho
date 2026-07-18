using System.Collections.Generic;
using UnityEngine;

public enum FeedbackCue
{
    Guidance = 0,
    Warm = 10,
    Wrong = 20,
    VeryWrong = 30,
    Correct = 40,
    Success = 50
}

public readonly struct FeedbackRequest
{
    public readonly FeedbackCue Cue;
    public readonly IReadOnlyList<GameObject> Strokes;

    public FeedbackRequest(FeedbackCue cue, IReadOnlyList<GameObject> strokes = null)
    {
        Cue = cue;
        Strokes = strokes;
    }
}

public interface IHapticsProvider
{
    bool IsAvailable { get; }
    void Vibrate(int milliseconds, float intensity = 1f);
}

public static class HapticsProviderFactory
{
    public static IHapticsProvider Create()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return new AndroidHapticsProvider();
#elif UNITY_IOS && !UNITY_EDITOR
        return new IosHapticsProvider();
#else
        return new NoOpHapticsProvider();
#endif
    }
}

public sealed class NoOpHapticsProvider : IHapticsProvider
{
    public bool IsAvailable => false;
    public void Vibrate(int milliseconds, float intensity = 1f) { }
}

public sealed class IosHapticsProvider : IHapticsProvider
{
    public bool IsAvailable => true;
    public void Vibrate(int milliseconds, float intensity = 1f) => Handheld.Vibrate();
}

public sealed class AndroidHapticsProvider : IHapticsProvider
{
    public bool IsAvailable => true;

    public void Vibrate(int milliseconds, float intensity = 1f)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
            {
                if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                    return;

                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (version.GetStatic<int>("SDK_INT") >= 26)
                    {
                        int amplitude = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 255f, Mathf.Clamp01(intensity))), 1, 255);
                        using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (var effect = effectClass.CallStatic<AndroidJavaObject>(
                                   "createOneShot", (long)Mathf.Max(1, milliseconds), amplitude))
                            vibrator.Call("vibrate", effect);
                    }
                    else
                    {
                        vibrator.Call("vibrate", (long)Mathf.Max(1, milliseconds));
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Haptics] Android vibration failed: {ex.Message}");
        }
#endif
    }
}
