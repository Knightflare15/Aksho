using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

public enum BuddyRealtimeCallStatus
{
    Ringing,
    Connected,
    BuddySpeaking,
    Reconnecting,
    Ended,
    Error,
}

[Serializable]
public sealed class BuddyRealtimeCallRequest
{
    public string functionsBaseUrl = "";
    public string firebaseIdToken = "";
    public string firebaseAppCheckToken = "";
    public string schoolId = "";
    public string studentId = "";
    public string dialogueTaskId = "";
    public string clientRequestId = "";
    public string trigger = "ask";
    public string learnerAttempt = "";
    public List<string> safeRelationshipMemory = new List<string>();
}

[Serializable]
sealed class FirebaseBuddyVoiceSessionRequest
{
    public string schoolId = "";
    public string studentId = "";
    public string dialogueTaskId = "";
    public string clientRequestId = "";
    public string trigger = "ask";
    public string learnerAttempt = "";
    public List<string> safeRelationshipMemory = new List<string>();
}

[Serializable]
sealed class FirebaseBuddyVoiceSessionResponse
{
    public string voiceSessionId = "";
    public string serverUrl = "";
    public string token = "";
    public string expiresAtUtc = "";
    public int maxSessionSeconds;
}

[Serializable]
sealed class FirebaseBuddyVoiceSessionEnvelope
{
    public FirebaseBuddyVoiceSessionResponse result;
}

[Serializable]
sealed class FirebaseBuddyVoiceCloseRequest
{
    public string schoolId = "";
    public string studentId = "";
    public string voiceSessionId = "";
}

[Serializable]
sealed class BuddyRealtimeGameEvent
{
    public int schemaVersion = 1;
    public string type = "wrong_answer";
    public string learnerAttempt = "";
    public string conceptId = "";
    public int turnIndex;
}

public sealed class BuddyRealtimeCallClient : MonoBehaviour
{
    static BuddyRealtimeCallClient instance;

    readonly List<AudioStream> remoteAudioStreams = new List<AudioStream>();
    readonly List<GameObject> remoteAudioObjects = new List<GameObject>();
    Room room;
    MicrophoneSource microphoneSource;
    LocalAudioTrack microphoneTrack;
    GameObject microphoneObject;
    IBuddyConversationTransport transport;
    Action<BuddyRealtimeCallStatus, string> stateChanged;
    BuddyRealtimeCallRequest activeRequest;
    string voiceSessionId = "";
    string microphoneDevice = "";
    int connectionVersion;
    bool microphoneMuted;
    bool taskCaptureActive;

    public bool IsActive { get; private set; }
    public bool IsConnected => room != null && room.IsConnected;
    public bool MicrophoneMuted => microphoneMuted;

    public static BuddyRealtimeCallClient EnsureExists()
    {
        if (instance != null)
            return instance;
        instance = FindAnyObjectByType<BuddyRealtimeCallClient>();
        if (instance != null)
            return instance;
        GameObject go = new GameObject("BuddyRealtimeCallClient");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BuddyRealtimeCallClient>();
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        transport = BuddyConversationTransportFactory.Create();
    }

    public bool BeginCall(
        BuddyRealtimeCallRequest request,
        Action<BuddyRealtimeCallStatus, string> onStateChanged)
    {
        if (request == null || IsActive || string.IsNullOrWhiteSpace(request.functionsBaseUrl) ||
            string.IsNullOrWhiteSpace(request.firebaseIdToken) || string.IsNullOrWhiteSpace(request.schoolId) ||
            string.IsNullOrWhiteSpace(request.studentId) || string.IsNullOrWhiteSpace(request.dialogueTaskId))
            return false;

        activeRequest = request;
        stateChanged = onStateChanged;
        IsActive = true;
        microphoneMuted = false;
        taskCaptureActive = false;
        int version = ++connectionVersion;
        Notify(BuddyRealtimeCallStatus.Ringing, "Preparing Buddy and the lesson context...");
        StartCoroutine(BeginCallRoutine(version));
        return true;
    }

    IEnumerator BeginCallRoutine(int version)
    {
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        if (version != connectionVersion || !IsActive)
            yield break;
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            Fail("Microphone permission is required for a Buddy call.");
            yield break;
        }

        var payload = new FirebaseBuddyVoiceSessionRequest
        {
            schoolId = activeRequest.schoolId,
            studentId = activeRequest.studentId,
            dialogueTaskId = activeRequest.dialogueTaskId,
            clientRequestId = activeRequest.clientRequestId,
            trigger = activeRequest.trigger,
            learnerAttempt = activeRequest.learnerAttempt,
            safeRelationshipMemory = activeRequest.safeRelationshipMemory ?? new List<string>(),
        };
        BuddyConversationTransportResult admission = null;
        yield return transport.Send(
            new BuddyConversationTransportRequest
            {
                endpoint = activeRequest.functionsBaseUrl.Trim().TrimEnd('/') + "/createBuddyVoiceSession",
                bearerToken = activeRequest.firebaseIdToken,
                appCheckToken = activeRequest.firebaseAppCheckToken,
                jsonBody = "{\"data\":" + JsonUtility.ToJson(payload) + "}",
                timeoutSeconds = 20,
            },
            result => admission = result);

        if (version != connectionVersion || !IsActive)
            yield break;
        if (admission == null || !admission.success)
        {
            Fail($"Buddy could not answer the call ({admission?.responseCode ?? 0}).");
            yield break;
        }

        FirebaseBuddyVoiceSessionEnvelope envelope = JsonUtility.FromJson<FirebaseBuddyVoiceSessionEnvelope>(admission.body);
        FirebaseBuddyVoiceSessionResponse session = envelope?.result;
        if (session == null || string.IsNullOrWhiteSpace(session.serverUrl) ||
            string.IsNullOrWhiteSpace(session.token) || string.IsNullOrWhiteSpace(session.voiceSessionId))
        {
            Fail("Buddy returned an invalid call session.");
            yield break;
        }
        voiceSessionId = session.voiceSessionId;

        room = new Room();
        room.TrackSubscribed += HandleTrackSubscribed;
        room.ActiveSpeakersChanged += HandleActiveSpeakersChanged;
        room.Reconnecting += HandleReconnecting;
        room.Reconnected += HandleReconnected;
        room.Disconnected += HandleDisconnected;
        var connect = room.Connect(session.serverUrl, session.token, new LiveKit.RoomOptions());
        yield return connect;
        if (version != connectionVersion || !IsActive)
            yield break;
        if (connect.IsError)
        {
            Fail("The realtime Buddy room could not connect.");
            yield break;
        }

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Fail("No microphone was found for the Buddy call.");
            yield break;
        }
        microphoneDevice = ResolveMicrophoneDevice(Microphone.devices);
        microphoneObject = new GameObject("BuddyCallMicrophone");
        microphoneObject.transform.SetParent(transform, false);
        microphoneSource = new MicrophoneSource(microphoneDevice, microphoneObject);
        microphoneTrack = LocalAudioTrack.CreateAudioTrack("buddy-microphone", microphoneSource, room);
        var publishOptions = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
            Source = TrackSource.SourceMicrophone,
        };
        var publish = room.LocalParticipant.PublishTrack(microphoneTrack, publishOptions);
        yield return publish;
        if (version != connectionVersion || !IsActive)
            yield break;
        if (publish.IsError)
        {
            Fail("The microphone could not join the Buddy call.");
            yield break;
        }

        try
        {
            microphoneSource.Start();
        }
        catch (Exception error)
        {
            Fail($"The microphone could not start: {error.Message}");
            yield break;
        }
        Notify(BuddyRealtimeCallStatus.Connected, $"Connected on {microphoneDevice}. Talk naturally; Buddy can hear you.");
    }

    public bool ToggleMicrophone()
    {
        if (!IsConnected || microphoneTrack == null)
            return microphoneMuted;
        microphoneMuted = !microphoneMuted;
        ((ILocalTrack)microphoneTrack).SetMute(microphoneMuted || taskCaptureActive);
        Notify(BuddyRealtimeCallStatus.Connected, microphoneMuted ? "Microphone muted." : "Microphone on.");
        return microphoneMuted;
    }

    static string ResolveMicrophoneDevice(IReadOnlyList<string> devices)
    {
        string selected = GameSettings.SelectedMicrophone;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i], selected, StringComparison.OrdinalIgnoreCase))
                    return devices[i];
            }
        }

        return devices[0];
    }

    public void SetTaskCaptureActive(bool active)
    {
        if (taskCaptureActive == active)
            return;
        taskCaptureActive = active;
        if (microphoneTrack != null)
            ((ILocalTrack)microphoneTrack).SetMute(active || microphoneMuted);
        if (microphoneSource == null)
            return;
        if (active)
        {
            microphoneSource.Stop();
            if (!string.IsNullOrWhiteSpace(microphoneDevice) && Microphone.IsRecording(microphoneDevice))
                Microphone.End(microphoneDevice);
        }
        else if (!microphoneMuted && IsConnected)
        {
            microphoneSource.Start();
        }
    }

    public void SendGameEvent(string type, string learnerAttempt, string conceptId, int turnIndex)
    {
        if (!IsConnected || room?.LocalParticipant == null)
            return;
        StartCoroutine(SendGameEventRoutine(new BuddyRealtimeGameEvent
        {
            type = string.Equals(type, "task_answered", StringComparison.Ordinal)
                ? "task_answered"
                : string.Equals(type, "word_meaning", StringComparison.Ordinal) ? "word_meaning" : "wrong_answer",
            learnerAttempt = (learnerAttempt ?? "").Trim(),
            conceptId = (conceptId ?? "").Trim(),
            turnIndex = Mathf.Max(0, turnIndex),
        }));
    }

    IEnumerator SendGameEventRoutine(BuddyRealtimeGameEvent gameEvent)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(gameEvent));
        var publish = room.LocalParticipant.PublishData(bytes, reliable: true, topic: "buddy.game_event");
        yield return publish;
        if (publish.IsError)
            Debug.LogWarning($"[BuddyRealtime] game event was not delivered: {publish.Error}");
    }

    public void EndCall()
    {
        if (!IsActive && string.IsNullOrWhiteSpace(voiceSessionId))
            return;
        ++connectionVersion;
        BuddyRealtimeCallRequest closingRequest = activeRequest;
        string closingSessionId = voiceSessionId;
        CleanupRoom();
        IsActive = false;
        activeRequest = null;
        voiceSessionId = "";
        stateChanged = null;
        if (closingRequest != null && !string.IsNullOrWhiteSpace(closingSessionId))
            StartCoroutine(CloseSessionRoutine(closingRequest, closingSessionId));
    }

    IEnumerator CloseSessionRoutine(BuddyRealtimeCallRequest request, string sessionId)
    {
        var payload = new FirebaseBuddyVoiceCloseRequest
        {
            schoolId = request.schoolId,
            studentId = request.studentId,
            voiceSessionId = sessionId,
        };
        yield return transport.Send(
            new BuddyConversationTransportRequest
            {
                endpoint = request.functionsBaseUrl.Trim().TrimEnd('/') + "/closeBuddyVoiceSession",
                bearerToken = request.firebaseIdToken,
                appCheckToken = request.firebaseAppCheckToken,
                jsonBody = "{\"data\":" + JsonUtility.ToJson(payload) + "}",
                timeoutSeconds = 10,
            },
            result =>
            {
                if (result == null || !result.success)
                    Debug.LogWarning($"[BuddyRealtime] close session failed with HTTP {result?.responseCode ?? 0}.");
            });
    }

    void HandleTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is not RemoteAudioTrack audioTrack)
            return;
        GameObject audioObject = new GameObject($"BuddyAudio-{audioTrack.Sid}");
        audioObject.transform.SetParent(transform, false);
        AudioSource output = audioObject.AddComponent<AudioSource>();
        output.playOnAwake = true;
        remoteAudioObjects.Add(audioObject);
        remoteAudioStreams.Add(new AudioStream(audioTrack, output));
    }

    void HandleActiveSpeakersChanged(List<Participant> speakers)
    {
        if (!IsActive || taskCaptureActive)
            return;
        foreach (Participant speaker in speakers)
        {
            if (speaker is RemoteParticipant)
            {
                Notify(BuddyRealtimeCallStatus.BuddySpeaking, "Buddy is talking. You can interrupt naturally.");
                return;
            }
        }
        Notify(BuddyRealtimeCallStatus.Connected, microphoneMuted ? "Microphone muted." : "Connected. Talk naturally; Buddy can hear you.");
    }

    void HandleReconnecting(Room reconnectingRoom) => Notify(BuddyRealtimeCallStatus.Reconnecting, "Reconnecting the call...");
    void HandleReconnected(Room reconnectedRoom) => Notify(BuddyRealtimeCallStatus.Connected, "Call reconnected.");

    void HandleDisconnected(Room disconnectedRoom)
    {
        if (IsActive)
            Fail("The Buddy call disconnected.");
    }

    void Fail(string message)
    {
        BuddyRealtimeCallRequest closingRequest = activeRequest;
        string closingSessionId = voiceSessionId;
        CleanupRoom();
        IsActive = false;
        activeRequest = null;
        voiceSessionId = "";
        Notify(BuddyRealtimeCallStatus.Error, message);
        stateChanged = null;
        if (closingRequest != null && !string.IsNullOrWhiteSpace(closingSessionId))
            StartCoroutine(CloseSessionRoutine(closingRequest, closingSessionId));
    }

    void Notify(BuddyRealtimeCallStatus status, string message)
    {
        stateChanged?.Invoke(status, message ?? "");
    }

    void CleanupRoom()
    {
        taskCaptureActive = false;
        microphoneMuted = false;
        if (microphoneSource != null)
        {
            microphoneSource.Stop();
            microphoneSource.Dispose();
            microphoneSource = null;
        }
        microphoneTrack = null;
        if (microphoneObject != null)
        {
            Destroy(microphoneObject);
            microphoneObject = null;
        }
        foreach (AudioStream stream in remoteAudioStreams)
            stream.Dispose();
        remoteAudioStreams.Clear();
        foreach (GameObject audioObject in remoteAudioObjects)
            if (audioObject != null)
                Destroy(audioObject);
        remoteAudioObjects.Clear();
        if (room != null)
        {
            room.TrackSubscribed -= HandleTrackSubscribed;
            room.ActiveSpeakersChanged -= HandleActiveSpeakersChanged;
            room.Reconnecting -= HandleReconnecting;
            room.Reconnected -= HandleReconnected;
            room.Disconnected -= HandleDisconnected;
            room.Dispose();
            room = null;
        }
        microphoneDevice = "";
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
        EndCall();
    }
}
