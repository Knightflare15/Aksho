using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class FirebaseCurriculumProvider : ICurriculumAccessProvider, IAsyncCurriculumAccessProvider, IRemoteLearnerStateProvider
{
    bool ShouldUseDirectWavLm(WordCastRecord castEvent)
    {
        return false;
    }

    IEnumerator TryAnalyzeWithDirectWavLm(WordCastRecord castEvent, Action<bool> onComplete)
    {
        onComplete?.Invoke(false);
        yield break;
    }

    void QueueDirectWavLmRecord(WordCastRecord castEvent)
    {
        if (castEvent == null || castEvent.pronunciationAudioWavBytes == null || castEvent.pronunciationAudioWavBytes.Length <= 44)
            return;

        try
        {
            string directory = DirectWavLmQueuePath();
            Directory.CreateDirectory(directory);
            string id = string.IsNullOrWhiteSpace(castEvent.serverAnalysisJobId)
                ? Guid.NewGuid().ToString("N")
                : castEvent.serverAnalysisJobId;
            string wavName = $"{id}.wav";
            File.WriteAllBytes(Path.Combine(directory, wavName), castEvent.pronunciationAudioWavBytes);
            var envelope = new DirectWavLmQueueEnvelope
            {
                record = castEvent,
                wavFileName = wavName,
            };
            File.WriteAllText(Path.Combine(directory, $"{id}.json"), JsonUtility.ToJson(envelope));
            Debug.Log($"[FirebaseCurriculumProvider] Queued pronunciation audio for direct server retry: {id}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Could not queue direct pronunciation record: {ex.Message}");
        }
    }

    void FlushQueuedDirectWavLmRecords()
    {
        if (string.IsNullOrWhiteSpace(wavLmApiBaseUrl) || CurriculumSessionManager.Instance == null)
            return;

        string directory = DirectWavLmQueuePath();
        if (!Directory.Exists(directory))
            return;

        foreach (string jsonPath in Directory.GetFiles(directory, "*.json"))
            CurriculumSessionManager.Instance.StartCoroutine(ProcessQueuedDirectWavLmRecord(jsonPath));
    }

    IEnumerator ProcessQueuedDirectWavLmRecord(string jsonPath)
    {
        DirectWavLmQueueEnvelope envelope = null;
        try
        {
            envelope = JsonUtility.FromJson<DirectWavLmQueueEnvelope>(File.ReadAllText(jsonPath));
            string wavPath = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", envelope.wavFileName ?? "");
            if (envelope?.record == null || !File.Exists(wavPath))
                yield break;

            envelope.record.pronunciationAudioWavBytes = File.ReadAllBytes(wavPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Could not read queued pronunciation record: {ex.Message}");
            yield break;
        }

        bool analyzed = false;
        yield return TryAnalyzeWithDirectWavLm(envelope.record, value => analyzed = value);
        if (!analyzed)
            yield break;

        if (string.IsNullOrWhiteSpace(functionsBaseUrl))
            yield return PostFirestoreDocument("submitWordCast", envelope.record);
        else
            yield return PostCallable("submitWordCast", envelope.record);

        try
        {
            string wavPath = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", envelope.wavFileName ?? "");
            File.Delete(jsonPath);
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FirebaseCurriculumProvider] Could not delete queued pronunciation files: {ex.Message}");
        }
    }

    string DirectWavLmQueuePath()
    {
        return Path.Combine(Application.persistentDataPath, DirectWavLmQueueDirectory);
    }
}
