using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


public sealed partial class CurriculumSessionManager
{
    public void ConfigureStudentSession(string schoolId, string classId, string studentId, string idToken, string refreshToken = "")
    {
        CheckpointBuddyLearningSession(true);
        studentSessionGeneration++;
        StopAllCoroutines();
        refreshingStudentToken = false;
        missionRequestPending = false;
        worldGoalRequestPending = false;
        learnerStateHydrationPending = false;
        activeSchoolId = string.IsNullOrWhiteSpace(schoolId) ? activeSchoolId : schoolId;
        activeClassId = string.IsNullOrWhiteSpace(classId) ? activeClassId : classId;
        activeStudentId = string.IsNullOrWhiteSpace(studentId) ? activeStudentId : studentId;
        studentIdToken = idToken ?? "";
        if (!string.IsNullOrWhiteSpace(refreshToken))
            studentRefreshToken = refreshToken;
        nextStudentTokenRefreshAt = Time.unscaledTime + StudentTokenRefreshIntervalSeconds;
        provider = null;
        PlayerPrefs.SetInt("TheScript.CurriculumProviderMode", (int)providerMode);
        PlayerPrefs.SetInt("TheScript.LearningAnalysisMode", (int)analysisMode);
        PlayerPrefs.SetString("TheScript.FirebaseProjectId", firebaseProjectId ?? "");
        PlayerPrefs.SetString("TheScript.FirebaseStorageBucket", firebaseStorageBucket ?? "");
        PlayerPrefs.SetString("TheScript.FirebaseFunctionsBaseUrl", firebaseFunctionsBaseUrl ?? "");
        PlayerPrefs.SetString("TheScript.FirebaseBuddyVoiceFunctionsBaseUrl", firebaseBuddyVoiceFunctionsBaseUrl ?? "");
        PlayerPrefs.SetInt("TheScript.WavLmEndpointMode", (int)wavLmEndpointMode);
        PlayerPrefs.SetString("TheScript.LocalWavLmApiBaseUrl", localWavLmApiBaseUrl ?? "");
        PlayerPrefs.SetString("TheScript.CloudWavLmApiBaseUrl", cloudWavLmApiBaseUrl ?? "");
        PlayerPrefs.SetString("TheScript.WavLmApiBaseUrl", wavLmApiBaseUrl ?? "");
        PlayerPrefs.SetString("TheScript.StudentSchoolId", activeSchoolId);
        PlayerPrefs.SetString("TheScript.StudentClassId", activeClassId);
        PlayerPrefs.SetString("TheScript.StudentId", activeStudentId);
        PlayerPrefs.SetString("TheScript.StudentIdToken", studentIdToken);
        PlayerPrefs.SetString("TheScript.StudentRefreshToken", studentRefreshToken ?? "");
        SaveBuddyPreferencePrefs();
        PlayerPrefs.Save();
        PlayerSaveSlots.SelectProfile(activeStudentId);
        GrammarWorldProgressService.Instance.ReloadForActiveProfile();
        PersistentCoinWallet.Reload();
        buddyLearningData = null;
        EnsureProvider();
        EnsureBuddyLearningData();
        BeginLearnerStateHydration();
        SubmitBuddyLearnerProfile();
        LoadOptionalWorldGoal();
        OnStudentSessionChanged?.Invoke();
    }

    public void ClearStudentSession()
    {
        CheckpointBuddyLearningSession(true);
        studentSessionGeneration++;
        StopAllCoroutines();
        refreshingStudentToken = false;
        missionRequestPending = false;
        worldGoalRequestPending = false;
        learnerStateHydrationPending = false;
        activeStudentId = "demo-student-1";
        activeSchoolId = "demo-school";
        activeClassId = "demo-class";
        studentIdToken = "";
        studentRefreshToken = "";
        nextStudentTokenRefreshAt = 0f;
        provider = null;
        if (!devSandboxMissionConfigured)
            CurrentMission = null;
        CurrentWorldGoal = null;

        PlayerPrefs.DeleteKey("TheScript.StudentSchoolId");
        PlayerPrefs.DeleteKey("TheScript.StudentClassId");
        PlayerPrefs.DeleteKey("TheScript.StudentId");
        PlayerPrefs.DeleteKey("TheScript.StudentIdToken");
        PlayerPrefs.DeleteKey("TheScript.StudentRefreshToken");
        PlayerPrefs.Save();
        PlayerSaveSlots.SelectProfile("demo-student");
        GrammarWorldProgressService.Instance.ReloadForActiveProfile();
        PersistentCoinWallet.Reload();
        buddyLearningData = null;
        EnsureProvider();
        EnsureBuddyLearningData();
        SubmitBuddyLearnerProfile();
        OnStudentSessionChanged?.Invoke();
    }

    public void RestoreStudentSessionFromPrefs()
    {
        providerMode = (CurriculumProviderMode)PlayerPrefs.GetInt("TheScript.CurriculumProviderMode", (int)providerMode);
        analysisMode = (LearningAnalysisMode)PlayerPrefs.GetInt("TheScript.LearningAnalysisMode", (int)analysisMode);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Endpoint overrides are a development convenience. Release builds use
        // the signed scene configuration so a modified PlayerPrefs file cannot
        // redirect Firebase bearer tokens to an arbitrary host.
        firebaseProjectId = PlayerPrefs.GetString("TheScript.FirebaseProjectId", firebaseProjectId);
        firebaseStorageBucket = PlayerPrefs.GetString("TheScript.FirebaseStorageBucket", firebaseStorageBucket);
        firebaseFunctionsBaseUrl = PlayerPrefs.GetString("TheScript.FirebaseFunctionsBaseUrl", firebaseFunctionsBaseUrl);
        firebaseBuddyVoiceFunctionsBaseUrl = PlayerPrefs.GetString("TheScript.FirebaseBuddyVoiceFunctionsBaseUrl", firebaseBuddyVoiceFunctionsBaseUrl);
        wavLmEndpointMode = (WavLmEndpointMode)PlayerPrefs.GetInt("TheScript.WavLmEndpointMode", (int)wavLmEndpointMode);
        localWavLmApiBaseUrl = PlayerPrefs.GetString("TheScript.LocalWavLmApiBaseUrl", localWavLmApiBaseUrl);
        if (string.IsNullOrWhiteSpace(localWavLmApiBaseUrl))
            localWavLmApiBaseUrl = DefaultLocalWavLmApiBaseUrl;
        cloudWavLmApiBaseUrl = PlayerPrefs.GetString("TheScript.CloudWavLmApiBaseUrl", cloudWavLmApiBaseUrl);
        wavLmApiBaseUrl = PlayerPrefs.GetString("TheScript.WavLmApiBaseUrl", wavLmApiBaseUrl);
#endif
        activeSchoolId = PlayerPrefs.GetString("TheScript.StudentSchoolId", activeSchoolId);
        activeClassId = PlayerPrefs.GetString("TheScript.StudentClassId", activeClassId);
        activeStudentId = PlayerPrefs.GetString("TheScript.StudentId", activeStudentId);
        studentIdToken = PlayerPrefs.GetString("TheScript.StudentIdToken", studentIdToken);
        studentRefreshToken = PlayerPrefs.GetString("TheScript.StudentRefreshToken", studentRefreshToken);
        buddyHomeLanguage = PlayerPrefs.GetString("TheScript.BuddyHomeLanguage", buddyHomeLanguage);
        buddyTargetLanguage = PlayerPrefs.GetString("TheScript.BuddyTargetLanguage", buddyTargetLanguage);
        buddyAllowTransliteration = PlayerPrefs.GetInt("TheScript.BuddyAllowTransliteration", buddyAllowTransliteration ? 1 : 0) != 0;
        buddyLearningMemoryEnabled = PlayerPrefs.GetInt("TheScript.BuddyLearningMemoryEnabled", buddyLearningMemoryEnabled ? 1 : 0) != 0;
        buddyExplanationStyle = PlayerPrefs.GetString("TheScript.BuddyExplanationStyle", buddyExplanationStyle);
        if (!string.IsNullOrWhiteSpace(studentIdToken))
            providerMode = CurriculumProviderMode.FirebaseRest;
        PlayerSaveSlots.SelectProfile(activeStudentId);
        provider = null;
    }

    IEnumerator RefreshStudentTokenIfPossible()
    {
        if (refreshingStudentToken)
            yield break;

        string apiKey = PlayerPrefs.GetString("TheScript.FirebaseApiKey", "");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(studentRefreshToken))
            yield break;

        refreshingStudentToken = true;
        string body = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(studentRefreshToken);
        using UnityWebRequest request = UnityWebRequest.Post(
            $"https://securetoken.googleapis.com/v1/token?key={apiKey}",
            body,
            "application/x-www-form-urlencoded");
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            TokenRefreshResponse refreshed = JsonUtility.FromJson<TokenRefreshResponse>(request.downloadHandler.text);
            if (refreshed != null && !string.IsNullOrWhiteSpace(refreshed.id_token))
            {
                studentIdToken = refreshed.id_token;
                if (!string.IsNullOrWhiteSpace(refreshed.refresh_token))
                    studentRefreshToken = refreshed.refresh_token;
                PlayerPrefs.SetString("TheScript.StudentIdToken", studentIdToken);
                PlayerPrefs.SetString("TheScript.StudentRefreshToken", studentRefreshToken);
                PlayerPrefs.Save();
                provider = null;
                EnsureProvider();
                BeginLearnerStateHydration();
                nextStudentTokenRefreshAt = Time.unscaledTime + StudentTokenRefreshIntervalSeconds;
                Debug.Log("[CurriculumSessionManager] Firebase student token refreshed.");
            }
        }
        else
        {
            Debug.LogWarning($"[CurriculumSessionManager] Firebase token refresh failed: {request.error}");
        }

        refreshingStudentToken = false;
    }

    [Serializable]
    sealed class TokenRefreshResponse
    {
        public string id_token = "";
        public string refresh_token = "";
    }
}
