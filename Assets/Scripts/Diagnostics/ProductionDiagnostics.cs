using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Privacy-bounded managed crash/exception reporting. Native crash capture still
/// requires a platform crash SDK; this component also detects an unclean prior exit.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProductionDiagnostics : MonoBehaviour
{
    const int SessionReportLimit = 8;
    const string CleanExitKey = "TheScript.Diagnostics.CleanExit";
    readonly Queue<DiagnosticPayload> pending = new Queue<DiagnosticPayload>();
    readonly HashSet<string> fingerprints = new HashSet<string>(StringComparer.Ordinal);
    readonly object gate = new object();
    int sentCount;
    bool sending;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void EnsureExists()
    {
        if (FindAnyObjectByType<ProductionDiagnostics>() != null)
            return;
        GameObject host = new GameObject("ProductionDiagnostics");
        DontDestroyOnLoad(host);
        host.AddComponent<ProductionDiagnostics>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceivedThreaded -= HandleLog;
        Application.logMessageReceivedThreaded += HandleLog;
        Application.quitting -= MarkCleanExit;
        Application.quitting += MarkCleanExit;

        if (!Application.isEditor && PlayerPrefs.GetInt(CleanExitKey, 1) == 0)
        {
            Enqueue("crash", "unclean_exit", "The previous game session ended unexpectedly.", "");
        }
        PlayerPrefs.SetInt(CleanExitKey, 0);
        PlayerPrefs.Save();
    }

    void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= HandleLog;
        Application.quitting -= MarkCleanExit;
    }

    void MarkCleanExit()
    {
        PlayerPrefs.SetInt(CleanExitKey, 1);
        PlayerPrefs.Save();
    }

    void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;
        Enqueue(type == LogType.Exception ? "exception" : "error", type.ToString(), condition, stackTrace);
    }

    void Enqueue(string severity, string category, string message, string stack)
    {
        string safeMessage = Redact(message, 700);
        string safeStack = Redact(stack, 2400);
        string fingerprint = Fingerprint(category + "\n" + safeMessage + "\n" + FirstLine(safeStack));
        lock (gate)
        {
            if (sentCount + pending.Count >= SessionReportLimit || !fingerprints.Add(fingerprint))
                return;
            pending.Enqueue(new DiagnosticPayload
            {
                schoolId = "",
                studentId = "",
                severity = severity,
                fingerprint = fingerprint,
                category = SafeToken(category, 64),
                // Unity APIs are intentionally filled on the main thread in Update;
                // logMessageReceivedThreaded may be raised by a worker thread.
                scene = "",
                build = "",
                platform = "",
                message = safeMessage,
                stack = safeStack
            });
        }
    }

    void Update()
    {
        if (sending || sentCount >= SessionReportLimit)
            return;
        CurriculumSessionManager session = CurriculumSessionManager.Instance;
        if (session == null || !session.HasStudentSession || string.IsNullOrWhiteSpace(session.firebaseFunctionsBaseUrl))
            return;

        DiagnosticPayload payload = null;
        lock (gate)
        {
            if (pending.Count > 0)
                payload = pending.Dequeue();
        }
        if (payload == null)
            return;
        payload.schoolId = session.activeSchoolId;
        payload.studentId = session.activeStudentId;
        payload.scene = SafeToken(SceneManager.GetActiveScene().name, 96);
        payload.build = SafeToken(Application.version, 96);
        payload.platform = SafeToken(Application.platform.ToString(), 96);
        StartCoroutine(Send(session, payload));
    }

    IEnumerator Send(CurriculumSessionManager session, DiagnosticPayload payload)
    {
        sending = true;
        string url = session.firebaseFunctionsBaseUrl.TrimEnd('/') + "/reportClientDiagnostic";
        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new CallableEnvelope { data = payload }));
        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + session.studentIdToken);
            request.timeout = 12;
            yield return request.SendWebRequest();
        }
        sentCount++;
        sending = false;
    }

    static string Redact(string value, int maximum)
    {
        string text = value ?? "";
        text = Regex.Replace(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[email]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?:\+?\d[\d\s().-]{7,}\d)", "[phone]");
        text = Regex.Replace(text, @"(?:[A-Za-z]:\\|/Users/|/home/)[^\s\r\n]+", "[local-path]");
        text = Regex.Replace(text, @"\b(?:eyJ|AIza|sk-)[A-Za-z0-9_.-]{12,}\b", "[secret]");
        return text.Length <= maximum ? text : text.Substring(0, maximum);
    }

    static string FirstLine(string value)
    {
        int line = value.IndexOf('\n');
        return line < 0 ? value : value.Substring(0, line);
    }

    static string SafeToken(string value, int maximum)
    {
        string text = Regex.Replace(value ?? "", @"[^a-zA-Z0-9_.:/-]", "_");
        return text.Length <= maximum ? text : text.Substring(0, maximum);
    }

    static string Fingerprint(string value)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
            StringBuilder result = new StringBuilder(24);
            for (int i = 0; i < 12; i++) result.Append(hash[i].ToString("x2"));
            return result.ToString();
        }
    }

    [Serializable]
    sealed class CallableEnvelope { public DiagnosticPayload data; }

    [Serializable]
    sealed class DiagnosticPayload
    {
        public string schoolId;
        public string studentId;
        public string severity;
        public string fingerprint;
        public string category;
        public string scene;
        public string build;
        public string platform;
        public string message;
        public string stack;
    }
}
