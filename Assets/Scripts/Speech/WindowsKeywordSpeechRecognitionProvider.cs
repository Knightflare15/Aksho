using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;

public sealed class WindowsKeywordSpeechRecognitionProvider : ISpeechRecognitionProvider
{
    KeywordRecognizer recognizer;
    string recognizerSignature = "";
    bool acceptingResults;
    volatile bool resultPending;
    public bool IsAvailable => true;
    public bool IsListening => acceptingResults || resultPending;
    public string Name => "Windows keyword recognition";
    public event Action<SpeechRecognitionResult> ResultReceived;

    public void Start(SpeechRecognitionRequest request)
    {
        if (request.Keywords == null || request.Keywords.Count == 0)
        {
            ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Error, message: "No spell words configured."));
            return;
        }

        try
        {
            var words = new string[request.Keywords.Count];
            for (int i = 0; i < words.Length; i++) words[i] = request.Keywords[i];
            string signature = string.Join("\n", words);
            if (recognizer == null || recognizerSignature != signature)
            {
                DisposeRecognizer();
                recognizerSignature = signature;
                // The accepted vocabulary is already tightly whitelisted, so Low improves
                // short-word recognition without allowing arbitrary phrases to cast.
                recognizer = new KeywordRecognizer(words, ConfidenceLevel.Low);
                recognizer.OnPhraseRecognized += OnPhraseRecognized;
            }

            resultPending = false;
            acceptingResults = true;
            if (!recognizer.IsRunning)
                recognizer.Start();
        }
        catch (Exception ex)
        {
            DisposeRecognizer();
            ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Error, message: ex.Message));
        }
    }

    void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        if (!acceptingResults || resultPending)
            return;

        acceptingResults = false;
        resultPending = true;
        string text = args.text;
        ResultReceived?.Invoke(new SpeechRecognitionResult(SpeechRecognitionStatus.Completed, new[] { text }));
    }

    public void AcknowledgeResult()
    {
        acceptingResults = false;
        resultPending = false;
    }

    public void Stop()
    {
        acceptingResults = false;
        resultPending = false;
    }

    public void Dispose() => DisposeRecognizer();
    void DisposeRecognizer()
    {
        acceptingResults = false;
        resultPending = false;
        recognizerSignature = "";
        if (recognizer == null) return;
        recognizer.OnPhraseRecognized -= OnPhraseRecognized;
        if (recognizer.IsRunning) recognizer.Stop();
        recognizer.Dispose();
        recognizer = null;
    }
}
#endif
