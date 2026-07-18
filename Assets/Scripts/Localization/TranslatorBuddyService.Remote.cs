using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public partial class TranslatorBuddyService : MonoBehaviour
{
    void BeginRemoteRefreshIfNeeded(string cacheKey, LocalizedDialogueLine line)
    {
        if (providerMode != TranslatorProviderMode.RestEndpoints ||
            line == null ||
            !Application.isPlaying ||
            !isActiveAndEnabled ||
            pendingRemoteLines.Contains(cacheKey))
            return;

        bool needsTranslation = !string.IsNullOrWhiteSpace(translationEndpointUrl) &&
                                !line.remoteTranslationSucceeded &&
                                line.remoteTranslationAttempts < Mathf.Max(1, maxRemoteAttempts);
        bool needsSpeech = RemoteTtsAllowed &&
                           !string.IsNullOrWhiteSpace(ttsEndpointUrl) &&
                           !line.remoteSpeechSucceeded &&
                           line.remoteSpeechAttempts < Mathf.Max(1, maxRemoteAttempts) &&
                           line.cachedSpeech == null;
        if (!needsTranslation && !needsSpeech)
            return;

        if (nextRemoteRetryAt.TryGetValue(cacheKey, out float retryAt) &&
            Time.realtimeSinceStartup < retryAt)
            return;

        StartCoroutine(ResolveRemoteLine(cacheKey, line));
    }

    IEnumerator ResolveRemoteLine(string cacheKey, LocalizedDialogueLine line)
    {
        pendingRemoteLines.Add(cacheKey);
        if (!string.IsNullOrWhiteSpace(translationEndpointUrl) && !line.remoteTranslationSucceeded)
            yield return RequestRemoteTranslation(line);

        if (RemoteTtsAllowed && line.cachedSpeech == null && !line.remoteSpeechSucceeded && !string.IsNullOrWhiteSpace(ttsEndpointUrl))
            yield return RequestRemoteSpeech(line);

        if (!string.IsNullOrWhiteSpace(line.lastError) &&
            (line.remoteTranslationAttempts < Mathf.Max(1, maxRemoteAttempts) ||
             line.remoteSpeechAttempts < Mathf.Max(1, maxRemoteAttempts)))
            nextRemoteRetryAt[cacheKey] = Time.realtimeSinceStartup +
                remoteRetryBackoffSeconds * Mathf.Max(1, line.remoteTranslationAttempts + line.remoteSpeechAttempts);
        else
            nextRemoteRetryAt.Remove(cacheKey);

        pendingRemoteLines.Remove(cacheKey);
        OnLineResolved?.Invoke(line);
    }

    IEnumerator RequestRemoteTranslation(LocalizedDialogueLine line)
    {
        line.remoteTranslationRequested = true;
        line.remoteTranslationAttempts++;
        var requestBody = new TranslatorRestRequest
        {
            text = line.sourceText,
            sourceLanguage = line.sourceLanguage,
            targetLanguage = line.targetLanguage,
            ttsBackend = RemoteTtsAllowed ? ttsBackendHint : "",
            voice = RemoteTtsAllowed ? voice : "",
        };

        using UnityWebRequest request = BuildJsonPost(translationEndpointUrl, JsonUtility.ToJson(requestBody));
        yield return request.SendWebRequest();

        if (!IsRequestOk(request))
        {
            line.lastError = DescribeRequestFailure("translate", request);
            Debug.LogWarning($"[TranslatorBuddy] {line.lastError}");
            yield break;
        }

        string json = request.downloadHandler != null ? request.downloadHandler.text : "";
        TranslatorRestResponse response = JsonUtility.FromJson<TranslatorRestResponse>(json);
        if (response == null)
            yield break;
        if (!string.IsNullOrWhiteSpace(response.error))
        {
            line.lastError = response.error;
            yield break;
        }
        if (response.fallback && !allowRemoteTextFallback)
        {
            line.lastError = "Translation endpoint returned fallback text. Configure a real translation provider or enable allowRemoteTextFallback for development.";
            line.providerName = FirstNonEmpty(response.providerName, "REST translation fallback");
            Debug.LogWarning($"[TranslatorBuddy] {line.lastError}");
            yield break;
        }

        string translated = FirstNonEmpty(response.translation, response.translatedText, response.text);
        if (!string.IsNullOrWhiteSpace(translated))
            line.cachedTranslation = translated;
        line.providerName = FirstNonEmpty(response.providerName, "REST translation");
        line.remoteTranslationSucceeded = !string.IsNullOrWhiteSpace(translated);
        if (line.remoteTranslationSucceeded)
            line.lastError = "";

        if (RemoteTtsAllowed && line.cachedSpeech == null)
            yield return TryApplyAudioResponse(line, response.audioUrl, response.audioBase64, response.audioContentType, line.providerName);
    }

    IEnumerator RequestRemoteSpeech(LocalizedDialogueLine line)
    {
        line.remoteSpeechRequested = true;
        line.remoteSpeechAttempts++;
        var requestBody = new TtsRestRequest
        {
            text = string.IsNullOrWhiteSpace(line.cachedTranslation) ? line.sourceText : line.cachedTranslation,
            language = line.targetLanguage,
            ttsBackend = ttsBackendHint,
            voice = voice,
        };

        using UnityWebRequest request = BuildJsonPost(ttsEndpointUrl, JsonUtility.ToJson(requestBody));
        yield return request.SendWebRequest();

        if (!IsRequestOk(request))
        {
            line.lastError = DescribeRequestFailure("tts", request);
            Debug.LogWarning($"[TranslatorBuddy] {line.lastError}");
            yield break;
        }

        string contentType = request.GetResponseHeader("Content-Type") ?? "";
        byte[] data = request.downloadHandler != null ? request.downloadHandler.data : Array.Empty<byte>();
        if (LooksLikeAudio(contentType, data))
        {
            if (TranslatorAudioClipDecoder.TryDecodeWav(data, $"tts_{line.lineId}", out AudioClip rawClip))
            {
                line.cachedSpeech = rawClip;
                line.ttsProviderName = "REST raw WAV";
                line.remoteSpeechSucceeded = true;
                line.lastError = "";
            }
            else
            {
                line.lastError = "TTS endpoint returned audio, but it was not a supported PCM WAV.";
            }
            yield break;
        }

        TtsRestResponse response = JsonUtility.FromJson<TtsRestResponse>(request.downloadHandler.text);
        if (response == null)
            yield break;
        if (!string.IsNullOrWhiteSpace(response.error))
        {
            line.lastError = response.error;
            yield break;
        }

        yield return TryApplyAudioResponse(
            line,
            response.audioUrl,
            response.audioBase64,
            response.audioContentType,
            FirstNonEmpty(response.providerName, ttsBackendHint, "REST TTS"));
    }

    IEnumerator TryApplyAudioResponse(
        LocalizedDialogueLine line,
        string audioUrl,
        string audioBase64,
        string contentType,
        string providerName)
    {
        if (!string.IsNullOrWhiteSpace(audioBase64))
        {
            try
            {
                byte[] base64Bytes = Convert.FromBase64String(audioBase64);
                if (TranslatorAudioClipDecoder.TryDecodeWav(base64Bytes, $"tts_{line.lineId}", out AudioClip clip))
                {
                    line.cachedSpeech = clip;
                    line.ttsProviderName = providerName;
                    line.remoteSpeechSucceeded = true;
                    line.lastError = "";
                }
                else
                {
                    line.lastError = "Base64 TTS audio was not a supported PCM WAV.";
                }
            }
            catch (Exception ex)
            {
                line.lastError = $"Base64 TTS decode failed: {ex.Message}";
            }
            yield break;
        }

        if (string.IsNullOrWhiteSpace(audioUrl))
            yield break;

        using UnityWebRequest request = UnityWebRequest.Get(audioUrl);
        request.timeout = Mathf.Max(1, requestTimeoutSeconds);
        yield return request.SendWebRequest();

        if (!IsRequestOk(request))
        {
            line.lastError = DescribeRequestFailure("audio", request);
            yield break;
        }

        byte[] audioBytes = request.downloadHandler != null ? request.downloadHandler.data : Array.Empty<byte>();
        if (TranslatorAudioClipDecoder.TryDecodeWav(audioBytes, $"tts_{line.lineId}", out AudioClip clipFromUrl))
        {
            line.cachedSpeech = clipFromUrl;
            line.ttsProviderName = providerName;
            line.remoteSpeechSucceeded = true;
            line.lastError = "";
        }
        else
        {
            line.lastError = "Audio URL did not return a supported PCM WAV.";
        }
    }

    UnityWebRequest BuildJsonPost(string url, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json ?? "{}");
        var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.Max(1, requestTimeoutSeconds),
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json, audio/wav");
        return request;
    }

    static bool IsRequestOk(UnityWebRequest request)
    {
        return request != null && request.result == UnityWebRequest.Result.Success;
    }

    static string DescribeRequestFailure(string operation, UnityWebRequest request)
    {
        if (request == null)
            return $"{operation} request was not created.";

        return $"{operation} request failed: result={request.result} code={request.responseCode} error={request.error}";
    }

    static string DescribeBuddyRequestFailure(UnityWebRequest request)
    {
        if (request == null)
            return "request was not created.";

        return $"result={request.result} http={request.responseCode} error='{request.error ?? ""}' body='{ClipLogBody(request.downloadHandler?.text)}'.";
    }

    static string ClipLogBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        string flattened = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return flattened.Length <= 500 ? flattened : flattened.Substring(0, 500) + "...";
    }

    static bool LooksLikeAudio(string contentType, byte[] data)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.ToLowerInvariant().Contains("audio"))
            return true;

        return data != null &&
               data.Length >= 12 &&
               data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
               data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';
    }

    static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return "";

        foreach (string value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        return "";
    }

    static string BuildCacheKey(string lineId, string sourceText, string targetLanguage)
    {
        string id = string.IsNullOrWhiteSpace(lineId) ? sourceText ?? "" : lineId;
        return $"{id}::{targetLanguage}";
    }

    static byte[] EncodePcm16Wav(byte[] pcm16Audio, int sampleRate)
    {
        byte[] audio = pcm16Audio ?? Array.Empty<byte>();
        int dataLength = audio.Length;
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(audio);
        return stream.ToArray();
    }

    static string ResolveSarvamLanguageCode(string language)
    {
        string code = (language ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code))
            return "unknown";
        if (code.Contains("-"))
            return code;

        return code.ToLowerInvariant() switch
        {
            "as" => "as-IN",
            "bn" => "bn-IN",
            "brx" => "brx-IN",
            "doi" => "doi-IN",
            "en" => "en-IN",
            "gu" => "gu-IN",
            "hi" => "hi-IN",
            "kn" => "kn-IN",
            "kok" => "kok-IN",
            "ks" => "ks-IN",
            "mai" => "mai-IN",
            "ml" => "ml-IN",
            "mni" => "mni-IN",
            "mr" => "mr-IN",
            "ne" => "ne-IN",
            "od" => "od-IN",
            "or" => "od-IN",
            "pa" => "pa-IN",
            "sa" => "sa-IN",
            "sat" => "sat-IN",
            "sd" => "sd-IN",
            "ta" => "ta-IN",
            "te" => "te-IN",
            "ur" => "ur-IN",
            _ => "unknown",
        };
    }
}
