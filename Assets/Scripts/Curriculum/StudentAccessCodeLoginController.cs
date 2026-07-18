using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public sealed class StudentAccessCodeLoginController : MonoBehaviour
{
    public enum AccessCodeLoginMode
    {
        ParentCode,
        StudentCode,
    }

    [Header("Firebase")]
    public string firebaseApiKey = "";
    public string firebaseProjectId = "";
    public string functionsBaseUrl = "";
    public string buddyVoiceFunctionsBaseUrl = "";
    public WavLmEndpointMode wavLmEndpointMode = WavLmEndpointMode.Auto;
    public string localWavLmApiBaseUrl = CurriculumSessionManager.DefaultLocalWavLmApiBaseUrl;
    public string cloudWavLmApiBaseUrl = "";
    public string wavLmApiBaseUrl = "";
    public AccessCodeLoginMode loginMode = AccessCodeLoginMode.ParentCode;

    [Header("UI")]
    public InputField emailInput;
    public InputField passwordInput;
    public InputField displayNameInput;
    public InputField codeInput;
    public Button loginButton;
    public Button registerButton;
    public Text statusLabel;

    [Header("Session")]
    public CurriculumSessionManager curriculumSession;
    public bool loadMissionAfterLogin = true;

    void Awake()
    {
        if (curriculumSession == null)
            curriculumSession = CurriculumSessionManager.EnsureExists();

        if (loginButton != null)
            loginButton.onClick.AddListener(StartLogin);
        if (registerButton != null)
            registerButton.gameObject.SetActive(false);
    }

    public void StartLogin()
    {
        if (HasEmailCredentials())
        {
            StartCoroutine(LoginParentAccount(false));
            return;
        }

        string code = codeInput != null ? codeInput.text : "";
        if (!string.IsNullOrWhiteSpace(code))
        {
            StartCodeOnlyLogin();
            return;
        }

        SetStatus("Enter the student email and password from your teacher.");
    }

    public void StartRegister()
    {
        if (!HasEmailCredentials())
        {
            SetStatus("Student accounts are created by the teacher portal.");
            return;
        }

        SetStatus("Ask your teacher for the login email and password.");
    }

    void StartCodeOnlyLogin()
    {
        string code = codeInput != null ? codeInput.text : "";
        if (string.IsNullOrWhiteSpace(code))
        {
            SetStatus("Enter an access code.");
            return;
        }

        if (string.IsNullOrWhiteSpace(firebaseApiKey) || string.IsNullOrWhiteSpace(functionsBaseUrl))
        {
            SetStatus("Firebase API key or functions URL is missing.");
            return;
        }

        StartCoroutine(LoginWithCode(code.Trim().ToUpperInvariant()));
    }

    IEnumerator LoginParentAccount(bool register)
    {
        if (string.IsNullOrWhiteSpace(firebaseApiKey) || string.IsNullOrWhiteSpace(firebaseProjectId))
        {
            SetStatus("Firebase API key or project ID is missing.");
            yield break;
        }

        SetBusy(true, register ? "Creating account..." : "Signing in...");
        FirebaseSignInResponse signIn = null;
        string endpoint = register ? "accounts:signUp" : "accounts:signInWithPassword";
        string email = EscapeJson(emailInput.text.Trim());
        string password = EscapeJson(passwordInput.text);
        string body = "{\"email\":\"" + email + "\",\"password\":\"" + password + "\",\"returnSecureToken\":true}";
        yield return PostJson(
            $"https://identitytoolkit.googleapis.com/v1/{endpoint}?key={firebaseApiKey}",
            body,
            null,
            response => signIn = JsonUtility.FromJson<FirebaseSignInResponse>(response));

        if (signIn == null || string.IsNullOrWhiteSpace(signIn.idToken))
        {
            SetBusy(false, register ? "Could not create account." : "Could not sign in.");
            yield break;
        }

        string code = codeInput != null ? codeInput.text.Trim().ToUpperInvariant() : "";
        if (!string.IsNullOrEmpty(code))
        {
            RedeemStudentCodeResult redeemResult = null;
            yield return PostJson(
                $"{TrimSlash(functionsBaseUrl)}/redeemParentCode",
                "{\"data\":{\"code\":\"" + EscapeJson(code) + "\"}}",
                signIn.idToken,
                response => redeemResult = JsonUtility.FromJson<CallableRedeemResponse>(response)?.result);

            if (redeemResult == null || string.IsNullOrWhiteSpace(redeemResult.customToken))
            {
                SetBusy(false, "Access code could not be linked.");
                yield break;
            }

            yield return SignInWithCustomToken(redeemResult.customToken, response => signIn = response);
            ConfigureCurriculumSession(redeemResult, signIn.idToken, signIn.refreshToken);
            SetBusy(false, curriculumSession.HasWorldGoalPractice ? "Class focus loaded." : "Signed in. No class focus practice is assigned yet.");
            yield break;
        }

        LinkedChildSession linked = null;
        if (string.IsNullOrWhiteSpace(functionsBaseUrl))
        {
            yield return FetchLinkedStudentProfile(signIn, response => linked = response);
        }
        else
        {
            yield return PostJson(
                $"{TrimSlash(functionsBaseUrl)}/getLinkedChildSession",
                "{\"data\":{}}",
                signIn.idToken,
                response => linked = JsonUtility.FromJson<CallableLinkedChildResponse>(response)?.result);
        }

        if (linked == null || string.IsNullOrWhiteSpace(linked.studentId))
        {
            SetBusy(false, "No student is linked to this login. Ask your teacher to check the account.");
            yield break;
        }

        ConfigureCurriculumSession(linked.schoolId, linked.classId, linked.studentId, signIn.idToken, signIn.refreshToken);
        SetBusy(false, curriculumSession.HasWorldGoalPractice ? "Class focus loaded." : "Signed in. No class focus practice is assigned yet.");
    }

    IEnumerator LoginWithCode(string code)
    {
        SetBusy(true, "Checking code...");
        RedeemStudentCodeResult redeemResult = null;
        string functionName = loginMode == AccessCodeLoginMode.ParentCode ? "redeemParentCode" : "redeemStudentCode";
        yield return PostJson(
            $"{TrimSlash(functionsBaseUrl)}/{functionName}",
            "{\"data\":{\"code\":\"" + EscapeJson(code) + "\"}}",
            null,
            response => redeemResult = JsonUtility.FromJson<CallableRedeemResponse>(response)?.result);

        if (redeemResult == null || string.IsNullOrWhiteSpace(redeemResult.customToken))
        {
            SetBusy(false, "Student code could not be redeemed.");
            yield break;
        }

        SetStatus("Starting session...");
        FirebaseSignInResponse signIn = null;
        string signInBody = "{\"token\":\"" + EscapeJson(redeemResult.customToken) + "\",\"returnSecureToken\":true}";
        yield return PostJson(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key={firebaseApiKey}",
            signInBody,
            null,
            response => signIn = JsonUtility.FromJson<FirebaseSignInResponse>(response));

        if (signIn == null || string.IsNullOrWhiteSpace(signIn.idToken))
        {
            SetBusy(false, "Could not start Firebase student session.");
            yield break;
        }

        curriculumSession.providerMode = CurriculumProviderMode.FirebaseRest;
        curriculumSession.firebaseProjectId = firebaseProjectId;
        curriculumSession.firebaseFunctionsBaseUrl = functionsBaseUrl;
        curriculumSession.firebaseBuddyVoiceFunctionsBaseUrl = buddyVoiceFunctionsBaseUrl;
        curriculumSession.wavLmEndpointMode = wavLmEndpointMode;
        curriculumSession.localWavLmApiBaseUrl = localWavLmApiBaseUrl;
        curriculumSession.cloudWavLmApiBaseUrl = cloudWavLmApiBaseUrl;
        curriculumSession.wavLmApiBaseUrl = wavLmApiBaseUrl;
        curriculumSession.ConfigureStudentSession(
            redeemResult.schoolId,
            redeemResult.classId,
            redeemResult.studentId,
            signIn.idToken,
            signIn.refreshToken);
        PlayerPrefs.SetString("TheScript.FirebaseApiKey", firebaseApiKey ?? "");
        PlayerPrefs.Save();

        if (loadMissionAfterLogin)
            curriculumSession.LoadWorldGoalPractice();

        SetBusy(false, curriculumSession.HasWorldGoalPractice ? "Class focus loaded." : "Signed in. No class focus practice is assigned yet.");
    }

    IEnumerator SignInWithCustomToken(string customToken, Action<FirebaseSignInResponse> onSuccess)
    {
        FirebaseSignInResponse signIn = null;
        string signInBody = "{\"token\":\"" + EscapeJson(customToken) + "\",\"returnSecureToken\":true}";
        yield return PostJson(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key={firebaseApiKey}",
            signInBody,
            null,
            response => signIn = JsonUtility.FromJson<FirebaseSignInResponse>(response));
        onSuccess?.Invoke(signIn);
    }

    void ConfigureCurriculumSession(RedeemStudentCodeResult result, string idToken, string refreshToken)
    {
        ConfigureCurriculumSession(result.schoolId, result.classId, result.studentId, idToken, refreshToken);
    }

    void ConfigureCurriculumSession(string schoolId, string classId, string studentId, string idToken, string refreshToken)
    {
        curriculumSession.providerMode = CurriculumProviderMode.FirebaseRest;
        curriculumSession.firebaseProjectId = firebaseProjectId;
        curriculumSession.firebaseFunctionsBaseUrl = functionsBaseUrl;
        curriculumSession.firebaseBuddyVoiceFunctionsBaseUrl = buddyVoiceFunctionsBaseUrl;
        curriculumSession.wavLmEndpointMode = wavLmEndpointMode;
        curriculumSession.localWavLmApiBaseUrl = localWavLmApiBaseUrl;
        curriculumSession.cloudWavLmApiBaseUrl = cloudWavLmApiBaseUrl;
        curriculumSession.wavLmApiBaseUrl = wavLmApiBaseUrl;
        curriculumSession.ConfigureStudentSession(schoolId, classId, studentId, idToken, refreshToken);
        PlayerPrefs.SetString("TheScript.FirebaseApiKey", firebaseApiKey ?? "");
        PlayerPrefs.Save();

        if (loadMissionAfterLogin)
            curriculumSession.LoadWorldGoalPractice();
    }

    IEnumerator PostJson(string url, string body, string bearerToken, Action<string> onSuccess)
    {
        using UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[StudentAccessCodeLoginController] Request failed: {request.error}");
            yield break;
        }

        onSuccess?.Invoke(request.downloadHandler.text);
    }

    IEnumerator FetchLinkedStudentProfile(FirebaseSignInResponse signIn, Action<LinkedChildSession> onSuccess)
    {
        if (signIn == null || string.IsNullOrWhiteSpace(signIn.localId) || string.IsNullOrWhiteSpace(signIn.idToken))
            yield break;

        string url = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/users/{signIn.localId}";
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", $"Bearer {signIn.idToken}");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[StudentAccessCodeLoginController] Profile fetch failed: {request.error}");
            yield break;
        }

        FirestoreUserDocument document = JsonUtility.FromJson<FirestoreUserDocument>(request.downloadHandler.text);
        FirestoreUserFields fields = document?.fields;
        if (fields == null || fields.role?.stringValue != "student")
            yield break;

        string studentId = fields.studentId?.stringValue;
        if (string.IsNullOrWhiteSpace(studentId))
            studentId = fields.studentIds?.FirstString();

        string classId = fields.classIds?.FirstString();
        if (string.IsNullOrWhiteSpace(fields.schoolId?.stringValue) ||
            string.IsNullOrWhiteSpace(classId) ||
            string.IsNullOrWhiteSpace(studentId))
            yield break;

        onSuccess?.Invoke(new LinkedChildSession
        {
            role = "student",
            schoolId = fields.schoolId.stringValue,
            classId = classId,
            studentId = studentId,
            studentName = fields.displayName?.stringValue,
        });
    }

    void SetBusy(bool busy, string message)
    {
        if (loginButton != null)
            loginButton.interactable = !busy;
        SetStatus(message);
    }

    void SetStatus(string message)
    {
        if (statusLabel != null)
            statusLabel.text = message;
        Debug.Log($"[StudentAccessCodeLoginController] {message}");
    }

    bool HasEmailCredentials()
    {
        return emailInput != null && passwordInput != null &&
            !string.IsNullOrWhiteSpace(emailInput.text) &&
            !string.IsNullOrWhiteSpace(passwordInput.text);
    }

    static string TrimSlash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.TrimEnd('/');
    }

    static string EscapeJson(string value)
    {
        return string.IsNullOrEmpty(value)
            ? ""
            : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

#pragma warning disable 0649
    [Serializable]
    sealed class CallableRedeemResponse
    {
        public RedeemStudentCodeResult result;
    }

    [Serializable]
    sealed class CallableLinkedChildResponse
    {
        public LinkedChildSession result;
    }

    [Serializable]
    sealed class RedeemStudentCodeResult
    {
        public string customToken;
        public string role;
        public string schoolId;
        public string classId;
        public string studentId;
    }

    [Serializable]
    sealed class LinkedChildSession
    {
        public string role;
        public string schoolId;
        public string classId;
        public string studentId;
        public string studentName;
    }

    [Serializable]
    sealed class FirebaseSignInResponse
    {
        public string idToken;
        public string refreshToken;
        public string localId;
        public string expiresIn;
    }

    [Serializable]
    sealed class FirestoreUserDocument
    {
        public FirestoreUserFields fields;
    }

    [Serializable]
    sealed class FirestoreUserFields
    {
        public FirestoreStringValue displayName;
        public FirestoreStringValue role;
        public FirestoreStringValue schoolId;
        public FirestoreStringValue studentId;
        public FirestoreArrayValue classIds;
        public FirestoreArrayValue studentIds;
    }

    [Serializable]
    sealed class FirestoreStringValue
    {
        public string stringValue;
    }

    [Serializable]
    sealed class FirestoreArrayValue
    {
        public FirestoreArray arrayValue;

        public string FirstString()
        {
            if (arrayValue?.values == null || arrayValue.values.Length == 0)
                return "";
            return arrayValue.values[0]?.stringValue ?? "";
        }
    }

    [Serializable]
    sealed class FirestoreArray
    {
        public FirestoreStringValue[] values;
    }
#pragma warning restore 0649
}
