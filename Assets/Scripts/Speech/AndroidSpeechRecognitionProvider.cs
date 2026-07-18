using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
public sealed class AndroidSpeechRecognitionProvider : ISpeechRecognitionProvider, ISpeechRecognitionProviderUpdate, IHoldToTalkSpeechRecognitionProvider
{
    sealed class Listener : AndroidJavaProxy
    {
        readonly AndroidSpeechRecognitionProvider owner;
        public Listener(AndroidSpeechRecognitionProvider owner) : base("android.speech.RecognitionListener") => this.owner = owner;
        void onResults(AndroidJavaObject bundle) => owner.HandleResults(bundle);
        void onPartialResults(AndroidJavaObject bundle) { }
        void onError(int error) => owner.HandleError(error);
        void onReadyForSpeech(AndroidJavaObject parameters) { }
        void onBeginningOfSpeech() { }
        void onRmsChanged(float rmsdB) { }
        void onBufferReceived(byte[] buffer) { }
        void onEndOfSpeech() { }
        void onEvent(int eventType, AndroidJavaObject parameters) { }
    }

    AndroidJavaObject recognizer;
    readonly object pendingResultLock = new object();
    readonly SpeechAttemptAudioCapture audioCapture = new SpeechAttemptAudioCapture();
    SpeechRecognitionResult pendingTerminalResult;
    bool hasPendingTerminalResult;
    bool listening;
    bool stopping;
    SpeechRecognitionRequest pendingRequest;
    UnityEngine.Android.PermissionCallbacks permissionCallbacks;
    public bool IsAvailable
    {
        get
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var speechClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                    return speechClass.CallStatic<bool>("isRecognitionAvailable", activity);
            }
            catch
            {
                return false;
            }
        }
    }
    public bool IsListening => listening;
    public string Name => "Android SpeechRecognizer";
    public event Action<SpeechRecognitionResult> ResultReceived;

    public void Start(SpeechRecognitionRequest request)
    {
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            pendingRequest = request;
            permissionCallbacks = new UnityEngine.Android.PermissionCallbacks();
            permissionCallbacks.PermissionGranted += OnPermissionGranted;
            permissionCallbacks.PermissionDenied += OnPermissionDenied;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, permissionCallbacks);
            ResultReceived?.Invoke(new SpeechRecognitionResult(
                SpeechRecognitionStatus.RequestingPermission,
                message: "Waiting for microphone permission."));
            return;
        }

        lock (pendingResultLock)
            hasPendingTerminalResult = false;

        // Recognition continues even if the device does not allow a parallel Unity capture.
        // In that case no Azure job is queued, rather than uploading empty microphone data.
        audioCapture.Start();
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                StopInternal();
                using (var speechClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH"))
                using (var recognizerIntent = new AndroidJavaClass("android.speech.RecognizerIntent"))
                {
                    recognizer = speechClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
                    stopping = false;
                    recognizer.Call("setRecognitionListener", new Listener(this));
                    intent.Call<AndroidJavaObject>("putExtra", recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                        recognizerIntent.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));
                    intent.Call<AndroidJavaObject>("putExtra", recognizerIntent.GetStatic<string>("EXTRA_LANGUAGE"), request.LanguageCode);
                    intent.Call<AndroidJavaObject>("putExtra", recognizerIntent.GetStatic<string>("EXTRA_MAX_RESULTS"), 5);
                    using (var biasingStrings = new AndroidJavaObject("java.util.ArrayList"))
                    {
                        if (request.Keywords != null)
                            foreach (string keyword in request.Keywords)
                                if (!string.IsNullOrWhiteSpace(keyword))
                                    biasingStrings.Call<bool>("add", keyword);
                        intent.Call<AndroidJavaObject>(
                            "putStringArrayListExtra",
                            "android.speech.extra.BIASING_STRINGS",
                            biasingStrings);
                    }
                    recognizer.Call("startListening", intent);
                    listening = true;
                }
            }));
        }
    }

    void OnPermissionGranted(string permission)
    {
        permissionCallbacks = null;
        Start(pendingRequest);
    }

    void OnPermissionDenied(string permission)
    {
        permissionCallbacks = null;
        ResultReceived?.Invoke(new SpeechRecognitionResult(
            SpeechRecognitionStatus.Denied,
            message: "Microphone permission denied. Enable it in device settings or choose a spell."));
    }

    void HandleResults(AndroidJavaObject bundle)
    {
        if (stopping) return;
        var alternatives = new List<string>();
        using (var speechClass = new AndroidJavaClass("android.speech.SpeechRecognizer"))
        using (var results = bundle.Call<AndroidJavaObject>("getStringArrayList", speechClass.GetStatic<string>("RESULTS_RECOGNITION")))
        {
            if (results != null)
                for (int i = 0; i < results.Call<int>("size"); i++)
                    alternatives.Add(results.Call<string>("get", i));
        }
        StopInternal();
        QueueTerminalResult(new SpeechRecognitionResult(SpeechRecognitionStatus.Completed, alternatives));
    }

    void HandleError(int error)
    {
        if (stopping) return;
        StopInternal();
        var status = error == 6 || error == 7 ? SpeechRecognitionStatus.TimedOut : SpeechRecognitionStatus.Error;
        QueueTerminalResult(new SpeechRecognitionResult(status, message: $"Android speech error {error}."));
    }

    public void Tick()
    {
        audioCapture.Tick();

        SpeechRecognitionResult result;
        lock (pendingResultLock)
        {
            if (!hasPendingTerminalResult)
                return;

            result = pendingTerminalResult;
            hasPendingTerminalResult = false;
        }

        if (result.Status == SpeechRecognitionStatus.Completed)
        {
            TrimmedPronunciationAudio trimmed = audioCapture.StopAndTrim();
            if (trimmed.HasSpeech)
            {
                Debug.Log($"[Pronunciation] Android capture trimmed {trimmed.SourceSeconds:0.00}s to {trimmed.TrimmedSeconds:0.00}s.");
                result = new SpeechRecognitionResult(
                    result.Status,
                    result.Alternatives,
                    result.Message,
                    trimmed.Pcm16Audio,
                    trimmed.SampleRate);
            }
            else
            {
                Debug.LogWarning("[Pronunciation] Android SpeechRecognizer completed without usable captured audio; Azure assessment will be skipped for this attempt.");
            }
        }
        else
        {
            audioCapture.StopDiscarding();
        }

        ResultReceived?.Invoke(result);
    }

    public void Stop()
    {
        audioCapture.StopDiscarding();
        if (recognizer == null) return;
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            activity.Call("runOnUiThread", new AndroidJavaRunnable(StopInternal));
    }

    public void FinishAttempt()
    {
        if (recognizer == null)
            return;

        // stopListening asks Android to return onResults; cancel would discard
        // both its transcript and the temporary microphone clip.
        audioCapture.Tick();
        listening = false;
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() => recognizer?.Call("stopListening")));
    }

    void StopInternal()
    {
        stopping = true;
        listening = false;
        if (recognizer == null) return;
        recognizer.Call("cancel");
        recognizer.Call("destroy");
        recognizer.Dispose();
        recognizer = null;
    }

    void QueueTerminalResult(SpeechRecognitionResult result)
    {
        lock (pendingResultLock)
        {
            pendingTerminalResult = result;
            hasPendingTerminalResult = true;
        }
    }

    public void Dispose()
    {
        Stop();
        audioCapture.StopDiscarding();
    }
}
#endif
