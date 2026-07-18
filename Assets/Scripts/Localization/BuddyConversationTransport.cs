using System;
using System.Collections;
using System.Text;
using UnityEngine.Networking;

public sealed class BuddyConversationTransportRequest
{
    public string endpoint = "";
    public string bearerToken = "";
    public string appCheckToken = "";
    public string jsonBody = "";
    public int timeoutSeconds = 25;
}

public sealed class BuddyConversationTransportResult
{
    public bool success;
    public long responseCode;
    public string body = "";
    public string error = "";
    public UnityWebRequest.Result requestResult;
}

/// <summary>
/// API boundary for Buddy turns. Firebase Callable is the current adapter;
/// another REST/WebSocket gateway can replace it through the factory.
/// </summary>
public interface IBuddyConversationTransport
{
    string ProviderName { get; }
    IEnumerator Send(
        BuddyConversationTransportRequest request,
        Action<BuddyConversationTransportResult> completed);
}

public static class BuddyConversationTransportFactory
{
    public static Func<IBuddyConversationTransport> OverrideFactory { get; set; }

    public static IBuddyConversationTransport Create()
    {
        return OverrideFactory != null
            ? OverrideFactory.Invoke()
            : new FirebaseCallableBuddyConversationTransport();
    }
}

sealed class FirebaseCallableBuddyConversationTransport : IBuddyConversationTransport
{
    public string ProviderName => "Firebase Callable REST";

    public IEnumerator Send(
        BuddyConversationTransportRequest transportRequest,
        Action<BuddyConversationTransportResult> completed)
    {
        var result = new BuddyConversationTransportResult();
        using (UnityWebRequest request = new UnityWebRequest(transportRequest.endpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(transportRequest.jsonBody ?? ""));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrWhiteSpace(transportRequest.bearerToken))
                request.SetRequestHeader("Authorization", $"Bearer {transportRequest.bearerToken}");
            if (!string.IsNullOrWhiteSpace(transportRequest.appCheckToken))
                request.SetRequestHeader("X-Firebase-AppCheck", transportRequest.appCheckToken);
            request.timeout = Math.Max(5, transportRequest.timeoutSeconds);
            yield return request.SendWebRequest();

            result.success = request.result == UnityWebRequest.Result.Success;
            result.responseCode = request.responseCode;
            result.body = request.downloadHandler?.text ?? "";
            result.error = request.error ?? "";
            result.requestResult = request.result;
        }
        completed?.Invoke(result);
    }
}
