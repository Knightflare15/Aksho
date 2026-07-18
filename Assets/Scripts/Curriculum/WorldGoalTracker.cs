using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Observes the optional teacher goal during normal world play.
///
/// Progression owns whether a gym is cleared. This component only turns that
/// completed gym into an authenticated, idempotent reward claim.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldGoalTracker : MonoBehaviour
{
    [Serializable]
    sealed class ClaimRequest
    {
        public string schoolId;
        public string studentId;
        public string goalId;
        public string targetGymId;
        public string completedAtUtc;
    }

    [Serializable]
    sealed class ClaimEnvelope
    {
        public WorldGoalClaimResult result = null;
    }

    const string PendingClaimKeyPrefix = "TheScript.PendingGoalClaim.";
    const float RetryIntervalSeconds = 15f;

    public static WorldGoalTracker Instance { get; private set; }

    public WorldGoalStatus Status { get; private set; } = WorldGoalStatus.NotAssigned;
    public bool RequestPending { get; private set; }

    /// <summary>The last goal for which the server has returned a final result.</summary>
    string resolvedGoalId;
    float nextRetryAt;

    public event Action OnGoalChanged;
    public event Action<WorldGoalClaimResult> OnRewardClaimed;

    public static WorldGoalTracker EnsureExists()
    {
        if (Instance != null)
            return Instance;

        Instance = FindAnyObjectByType<WorldGoalTracker>();
        if (Instance != null)
            return Instance;

        GameObject root = new GameObject(nameof(WorldGoalTracker));
        Instance = root.AddComponent<WorldGoalTracker>();
        Preserve(root);
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Preserve(gameObject);
    }

    void OnEnable()
    {
        GrammarWorldProgressService.Instance.OnAreaCompleted += HandleAreaCompleted;
    }

    void OnDisable()
    {
        if (GrammarWorldProgressService.Instance != null)
            GrammarWorldProgressService.Instance.OnAreaCompleted -= HandleAreaCompleted;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && HasPendingClaimForCurrentGoal())
            Refresh();
    }

    void Update()
    {
        if (RequestPending || Time.unscaledTime < nextRetryAt || !HasPendingClaimForCurrentGoal())
            return;

        if (Application.internetReachability == NetworkReachability.NotReachable)
            return;

        nextRetryAt = Time.unscaledTime + RetryIntervalSeconds;
        Refresh();
    }

    void HandleAreaCompleted(GrammarMapAreaState area)
    {
        WorldGoalAssignment goal = CurriculumSessionManager.Instance?.CurrentWorldGoal;
        if (area == null || area.sceneKind != SemanticZoneKind.Gym ||
            !TryGetTargetGymId(goal, out string targetGymId))
            return;

        if (string.Equals(
                GrammarWorldProgressService.CanonicalizeAreaId(area.areaId),
                targetGymId,
                StringComparison.OrdinalIgnoreCase))
            Refresh();
    }

    /// <summary>
    /// Updates the presentation state and claims a completed goal once the
    /// student is authenticated and the network request can be made.
    /// </summary>
    public void Refresh()
    {
        CurriculumSessionManager session = CurriculumSessionManager.Instance;
        WorldGoalAssignment goal = session?.CurrentWorldGoal;
        if (session == null || !session.HasStudentSession || !TryGetTargetGymId(goal, out string targetGymId))
        {
            resolvedGoalId = null;
            SetStatus(WorldGoalStatus.NotAssigned);
            return;
        }

        if (!string.Equals(resolvedGoalId, goal.goalId, StringComparison.Ordinal))
            resolvedGoalId = null;

        bool cleared = GrammarWorldProgressService.Instance.IsGymCleared(targetGymId);
        if (!cleared)
        {
            SetStatus(IsPastDeadline(goal) ? WorldGoalStatus.Expired : WorldGoalStatus.InProgress);
            return;
        }

        if (RequestPending || !string.IsNullOrEmpty(resolvedGoalId))
            return;

        SetStatus(IsPastDeadline(goal) ? WorldGoalStatus.CompletedLate : WorldGoalStatus.CompletedOnTime);
        StartCoroutine(ClaimRoutine(session, goal, targetGymId));
    }

    IEnumerator ClaimRoutine(CurriculumSessionManager session, WorldGoalAssignment goal, string targetGymId)
    {
        RequestPending = true;
        OnGoalChanged?.Invoke();
        string claimStudentId = session.activeStudentId;
        string claimGoalId = goal.goalId;

        ClaimRequest payload = new ClaimRequest
        {
            schoolId = session.activeSchoolId,
            studentId = session.activeStudentId,
            goalId = goal.goalId,
            targetGymId = targetGymId,
            // The callable uses its own clock for rewards. This timestamp is
            // useful only as client-side diagnostic evidence.
            completedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };

        string endpoint = BuildClaimEndpoint(session.firebaseFunctionsBaseUrl);
        if (string.IsNullOrEmpty(endpoint))
        {
            QueueRetry(claimStudentId, claimGoalId, "Firebase Functions URL is not configured.");
            yield break;
        }

        string body = "{\"data\":" + JsonUtility.ToJson(payload) + "}";
        using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 20,
        };
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {session.studentIdToken}");
        yield return request.SendWebRequest();

        CurriculumSessionManager currentSession = CurriculumSessionManager.Instance;
        if (currentSession == null || !currentSession.HasStudentSession ||
            !string.Equals(currentSession.activeStudentId, claimStudentId, StringComparison.Ordinal) ||
            !string.Equals(currentSession.CurrentWorldGoal?.goalId, claimGoalId, StringComparison.Ordinal))
        {
            // A shared-device account switch occurred while the request was in
            // flight. Never mirror the previous learner's wallet into the new profile.
            RequestPending = false;
            yield break;
        }

        if (request.result == UnityWebRequest.Result.Success && TryReadClaimResult(request.downloadHandler.text, out WorldGoalClaimResult result))
        {
            CompleteClaim(claimStudentId, claimGoalId, result);
        }
        else
        {
            string failure = request.result == UnityWebRequest.Result.Success
                ? "The reward service returned an invalid response."
                : request.error;
            QueueRetry(claimStudentId, claimGoalId, failure);
        }
    }

    void CompleteClaim(string studentId, string goalId, WorldGoalClaimResult result)
    {
        PlayerPrefs.DeleteKey(PendingKey(studentId, goalId));
        resolvedGoalId = goalId;

        if (result.walletBalance >= 0)
            WorldEconomyService.EnsureExists().MirrorServerBalance(result.walletBalance);

        SetStatus(string.Equals(result.status, "reward_claimed", StringComparison.OrdinalIgnoreCase)
            ? WorldGoalStatus.RewardClaimed
            : WorldGoalStatus.CompletedLate);
        OnRewardClaimed?.Invoke(result);
        RequestPending = false;
        OnGoalChanged?.Invoke();
    }

    void QueueRetry(string studentId, string goalId, string reason)
    {
        PlayerPrefs.SetInt(PendingKey(studentId, goalId), 1);
        RequestPending = false;
        nextRetryAt = Time.unscaledTime + RetryIntervalSeconds;
        Debug.LogWarning($"[WorldGoal] Reward claim queued: {reason}");
        OnGoalChanged?.Invoke();
    }

    bool HasPendingClaimForCurrentGoal()
    {
        WorldGoalAssignment goal = CurriculumSessionManager.Instance?.CurrentWorldGoal;
        string studentId = CurriculumSessionManager.Instance?.activeStudentId ?? "";
        return goal != null && !string.IsNullOrWhiteSpace(goal.goalId) &&
               PlayerPrefs.GetInt(PendingKey(studentId, goal.goalId), 0) == 1;
    }

    static bool TryReadClaimResult(string responseBody, out WorldGoalClaimResult result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        ClaimEnvelope envelope = JsonUtility.FromJson<ClaimEnvelope>(responseBody);
        result = envelope?.result;
        return result != null && !string.IsNullOrWhiteSpace(result.goalId);
    }

    static bool TryGetTargetGymId(WorldGoalAssignment goal, out string targetGymId)
    {
        targetGymId = goal?.targetGymId;
        if (string.IsNullOrWhiteSpace(targetGymId))
            return false;

        targetGymId = GrammarWorldProgressService.CanonicalizeAreaId(targetGymId);
        return !string.IsNullOrWhiteSpace(targetGymId);
    }

    static string BuildClaimEndpoint(string baseUrl)
    {
        string normalized = (baseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(normalized) ? "" : normalized + "/claimWorldGoalReward";
    }

    static bool IsPastDeadline(WorldGoalAssignment goal)
    {
        if (goal == null || !DateTime.TryParseExact(goal.dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dueDate))
            return false;

        return GetDateInSchoolTimeZone(goal.schoolTimeZone) > dueDate.Date;
    }

    static DateTime GetDateInSchoolTimeZone(string timeZoneId)
    {
        try
        {
            TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timeZoneId) ? "Asia/Kolkata" : timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
        }
        catch (TimeZoneNotFoundException)
        {
            // Android and Windows expose different identifiers. The existing
            // default is India Standard Time when IANA data is unavailable.
            return DateTime.UtcNow.AddHours(5.5d).Date;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.UtcNow.AddHours(5.5d).Date;
        }
    }

    static string PendingKey(string studentId, string goalId) =>
        PendingClaimKeyPrefix + (studentId ?? "guest") + "." + goalId;

    static void Preserve(GameObject target)
    {
        if (Application.isPlaying && target != null)
            DontDestroyOnLoad(target);
    }

    void SetStatus(WorldGoalStatus value)
    {
        if (Status == value)
            return;

        Status = value;
        OnGoalChanged?.Invoke();
    }
}
