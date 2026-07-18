using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
public sealed class IosSpeechRecognitionProvider : ISpeechRecognitionProvider
{
    delegate void NativeCallback(int status, string alternatives, string message);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern bool TSR_SpeechIsAvailable();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TSR_StartSpeech(string language, string keywords, NativeCallback callback);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TSR_StopSpeech();
    static IosSpeechRecognitionProvider active;
    static readonly NativeCallback callback = HandleNative;
    bool listening;
    public bool IsAvailable => TSR_SpeechIsAvailable();
    public bool IsListening => listening;
    public string Name => "iOS Speech";
    public event Action<SpeechRecognitionResult> ResultReceived;

    public void Start(SpeechRecognitionRequest request)
    {
        active = this;
        listening = true;
        string keywords = request.Keywords == null ? "" : string.Join("\n", request.Keywords);
        TSR_StartSpeech(request.LanguageCode, keywords, callback);
    }

    [AOT.MonoPInvokeCallback(typeof(NativeCallback))]
    static void HandleNative(int status, string alternatives, string message)
    {
        if (active == null) return;
        active.listening = status == (int)SpeechRecognitionStatus.Listening;
        string[] values = string.IsNullOrEmpty(alternatives) ? Array.Empty<string>() : alternatives.Split('\n');
        active.ResultReceived?.Invoke(new SpeechRecognitionResult((SpeechRecognitionStatus)status, values, message));
    }

    public void Stop() { listening = false; TSR_StopSpeech(); }
    public void Dispose() { Stop(); if (active == this) active = null; }
}
#endif
