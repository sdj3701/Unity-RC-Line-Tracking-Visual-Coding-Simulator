using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Auth;
using UnityEngine;
using UnityEngine.Networking;

public class ChatRoomManager : MonoBehaviour
{
    public static ChatRoomManager Instance { get; private set; }

    [Header("API")]
    [SerializeField] private string _createRoomEndpoint = "http://ioteacher.com/api/chat/rooms";
    [SerializeField] private string _listRoomEndpoint = "http://ioteacher.com/api/chat/rooms";

    [Header("Timeout")]
    [SerializeField] private int _requestTimeoutSeconds = 15;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    public event Action<string, int> OnCreateStarted;
    public event Action<ChatRoomCreateInfo> OnCreateSucceeded;
    public event Action<string> OnCreateFailed;
    public event Action OnCreateCanceled;
    public event Action OnListStarted;
    public event Action<ChatRoomSummaryInfo[]> OnListSucceeded;
    public event Action<string> OnListFailed;
    public event Action OnListCanceled;

    public bool IsBusy { get; private set; }

    private CancellationTokenSource _requestCancellation;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 채팅방 생성 요청 진입점.
    /// room title / max user count를 검증한 뒤 웹 API에 생성을 요청한다.
    /// </summary>
    /// <param name="roomTitleRaw">사용자 입력 방 제목</param>
    /// <param name="maxUserCountRaw">사용자 입력 최대 인원 문자열</param>
    public void CreateRoom(string roomTitleRaw, string maxUserCountRaw)
    {
        _ = CreateRoomAsync(roomTitleRaw, maxUserCountRaw);
    }

    /// <summary>
    /// 채팅방 목록 조회 요청 진입점.
    /// </summary>
    public void FetchRoomList()
    {
        _ = FetchRoomListAsync();
    }

    /// <summary>
    /// 진행 중인 채팅방 생성 요청을 취소한다.
    /// </summary>
    public void CancelCurrentRequest()
    {
        if (_requestCancellation == null || _requestCancellation.IsCancellationRequested)
            return;

        _requestCancellation.Cancel();
    }

    private async Task CreateRoomAsync(string roomTitleRaw, string maxUserCountRaw)
    {
        if (IsBusy)
        {
            EmitFailure("이미 채팅방 생성 요청을 처리 중입니다.");
            return;
        }

        string roomTitle = string.IsNullOrWhiteSpace(roomTitleRaw) ? string.Empty : roomTitleRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomTitle))
        {
            EmitFailure("방 제목을 입력해 주세요.");
            return;
        }

        if (string.IsNullOrWhiteSpace(maxUserCountRaw))
        {
            EmitFailure("최대 인원을 입력해 주세요.");
            return;
        }

        if (!int.TryParse(maxUserCountRaw.Trim(), out int maxUserCount) || maxUserCount <= 0)
        {
            EmitFailure("최대 인원은 1 이상의 숫자여야 합니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_createRoomEndpoint))
        {
            EmitFailure("채팅방 생성 API URL이 비어 있습니다.");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnCreateStarted?.Invoke(roomTitle, maxUserCount);

            string accessToken = ResolveAccessToken();
            string userId = ResolveUserId();
            string idempotencyKey = Guid.NewGuid().ToString("N");

            var payload = new CreateChatRoomRequestPayload
            {
                title = roomTitle,
                roomName = roomTitle,
                maxUserCount = maxUserCount,
                max_user_count = maxUserCount,
                userId = userId,
                hostUserId = userId
            };

            string requestJson = JsonUtility.ToJson(payload);

            using (var request = new UnityWebRequest(_createRoomEndpoint, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);

                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrWhiteSpace(accessToken))
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                request.SetRequestHeader("Idempotency-Key", idempotencyKey);

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnCreateCanceled?.Invoke();
                    Log("Chat room creation canceled.");
                    return;
                }

                ChatRoomCreateInfo roomInfo = ParseCreateResponse(request, roomTitle, maxUserCount, userId);
                if (roomInfo != null)
                {
                    OnCreateSucceeded?.Invoke(roomInfo);
                    Log($"Chat room created. roomId={roomInfo.RoomId}, title={roomInfo.Title}");
                    return;
                }

                // ParseCreateResponse에서 실패 이벤트를 발행하지 못한 경우를 대비한 기본 처리.
                EmitFailure("채팅방 생성에 실패했습니다.");
            }
        }
        catch (OperationCanceledException)
        {
            OnCreateCanceled?.Invoke();
            Log("Chat room creation canceled by token.");
        }
        catch (Exception e)
        {
            EmitFailure($"채팅방 생성 중 예외가 발생했습니다. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task FetchRoomListAsync()
    {
        if (IsBusy)
        {
            EmitListFailure("이미 다른 채팅 요청을 처리 중입니다.");
            return;
        }

        string endpoint = string.IsNullOrWhiteSpace(_listRoomEndpoint)
            ? _createRoomEndpoint
            : _listRoomEndpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitListFailure("채팅방 목록 API URL이 비어 있습니다.");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnListStarted?.Invoke();

            string accessToken = ResolveAccessToken();

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrWhiteSpace(accessToken))
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnListCanceled?.Invoke();
                    Log("Chat room list fetch canceled.");
                    return;
                }

                ChatRoomSummaryInfo[] roomList = ParseRoomListResponse(request);
                if (roomList != null)
                {
                    OnListSucceeded?.Invoke(roomList);
                    Log($"Chat room list fetched. count={roomList.Length}, titles={BuildRoomTitlesLog(roomList)}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            OnListCanceled?.Invoke();
            Log("Chat room list fetch canceled by token.");
        }
        catch (Exception e)
        {
            EmitListFailure($"채팅방 목록 조회 중 예외가 발생했습니다. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private static async Task<bool> SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
    {
        var operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                request.Abort();
                return true;
            }

            await Task.Yield();
        }

        return cancellationToken.IsCancellationRequested;
    }

    private ChatRoomCreateInfo ParseCreateResponse(
        UnityWebRequest request,
        string requestedTitle,
        int requestedMaxUserCount,
        string requestedUserId)
    {
        if (request == null)
        {
            EmitFailure("요청 객체가 없습니다.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitFailure("네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        ChatRoomServiceResponse response = ParseResponseBody(body);

        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;
        bool responseSuccess = response.success || response.isSuccess || httpSuccess;

        if (!responseSuccess)
        {
            string errorMessage = FirstNonEmpty(
                response.message,
                response.error,
                $"HTTP {request.responseCode}",
                "채팅방 생성에 실패했습니다.");

            EmitFailure(errorMessage);
            return null;
        }

        ChatRoomPayload room = response.room ?? new ChatRoomPayload();

        string roomId = FirstNonEmpty(
            response.roomId,
            response.room_id,
            room.roomId,
            room.room_id,
            room.id);

        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitFailure("채팅방 ID가 응답에 없습니다.");
            return null;
        }

        string roomTitle = FirstNonEmpty(
            room.title,
            response.title,
            requestedTitle);

        string ownerUserId = FirstNonEmpty(
            room.userId,
            room.user_id,
            response.userId,
            response.user_id,
            requestedUserId);

        int maxUserCount = room.maxUserCount > 0
            ? room.maxUserCount
            : room.max_user_count > 0
                ? room.max_user_count
                : response.maxUserCount > 0
                    ? response.maxUserCount
                    : response.max_user_count > 0
                        ? response.max_user_count
                        : requestedMaxUserCount;

        string status = FirstNonEmpty(room.status, response.status, "ACTIVE");
        string createdAtUtc = FirstNonEmpty(room.createdAt, room.created_at, response.createdAt, response.created_at);

        return new ChatRoomCreateInfo
        {
            RoomId = roomId,
            Title = roomTitle,
            OwnerUserId = ownerUserId,
            MaxUserCount = maxUserCount,
            Status = status,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static ChatRoomServiceResponse ParseResponseBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new ChatRoomServiceResponse();

        try
        {
            ChatRoomServiceResponse parsed = JsonUtility.FromJson<ChatRoomServiceResponse>(body);
            return parsed ?? new ChatRoomServiceResponse();
        }
        catch (Exception)
        {
            return new ChatRoomServiceResponse();
        }
    }

    private ChatRoomSummaryInfo[] ParseRoomListResponse(UnityWebRequest request)
    {
        if (request == null)
        {
            EmitListFailure("요청 객체가 없습니다.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitListFailure("네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        ChatRoomServiceResponse response = ParseResponseBody(body);
        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

        if (!httpSuccess || HasExplicitFailureFlag(body))
        {
            string errorMessage = FirstNonEmpty(
                response.message,
                response.error,
                $"HTTP {request.responseCode}",
                "채팅방 목록 조회에 실패했습니다.");

            EmitListFailure(errorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<ChatRoomSummaryInfo>();

        ChatRoomPayload[] payloads = ExtractRoomPayloads(body);
        if (payloads == null)
        {
            EmitListFailure("채팅방 목록 응답을 해석할 수 없습니다.");
            return null;
        }

        var roomSummaries = new List<ChatRoomSummaryInfo>(payloads.Length);

        for (int i = 0; i < payloads.Length; i++)
        {
            ChatRoomPayload room = payloads[i];
            if (room == null)
                continue;

            string roomId = FirstNonEmpty(room.roomId, room.room_id, room.id);
            if (string.IsNullOrWhiteSpace(roomId))
                continue;

            int maxUserCount = room.maxUserCount > 0
                ? room.maxUserCount
                : room.max_user_count > 0
                    ? room.max_user_count
                    : 0;

            roomSummaries.Add(new ChatRoomSummaryInfo
            {
                RoomId = roomId,
                Title = FirstNonEmpty(room.title, room.roomName, room.name, string.Empty),
                OwnerUserId = FirstNonEmpty(
                    room.ownerUserId,
                    room.owner_user_id,
                    room.hostUserId,
                    room.userId,
                    room.user_id,
                    string.Empty),
                MaxUserCount = maxUserCount,
                Status = FirstNonEmpty(room.status, "ACTIVE"),
                CreatedAtUtc = FirstNonEmpty(room.createdAt, room.created_at, string.Empty)
            });
        }

        return roomSummaries.ToArray();
    }

    private static ChatRoomPayload[] ExtractRoomPayloads(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<ChatRoomPayload>();

        string trimmedBody = body.Trim();

        if (trimmedBody.StartsWith("[", StringComparison.Ordinal))
        {
            string wrapped = $"{{\"rooms\":{trimmedBody}}}";
            ChatRoomListArrayWrapper wrappedResponse = TryParseJson<ChatRoomListArrayWrapper>(wrapped);
            return wrappedResponse != null ? wrappedResponse.rooms : null;
        }

        ChatRoomListRoomsResponse roomsResponse = TryParseJson<ChatRoomListRoomsResponse>(trimmedBody);
        if (roomsResponse != null)
        {
            if (roomsResponse.rooms != null)
                return roomsResponse.rooms;

            if (roomsResponse.list != null)
                return roomsResponse.list;
        }

        ChatRoomListDataArrayResponse dataArrayResponse = TryParseJson<ChatRoomListDataArrayResponse>(trimmedBody);
        if (dataArrayResponse != null && dataArrayResponse.data != null)
            return dataArrayResponse.data;

        ChatRoomListDataObjectResponse dataObjectResponse = TryParseJson<ChatRoomListDataObjectResponse>(trimmedBody);
        if (dataObjectResponse != null && dataObjectResponse.data != null)
        {
            if (dataObjectResponse.data.rooms != null)
                return dataObjectResponse.data.rooms;

            if (dataObjectResponse.data.list != null)
                return dataObjectResponse.data.list;
        }

        return null;
    }

    private static T TryParseJson<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasExplicitFailureFlag(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        string normalized = body
            .Replace(" ", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\r", string.Empty)
            .ToLowerInvariant();

        return normalized.IndexOf("\"success\":false", StringComparison.Ordinal) >= 0 ||
               normalized.IndexOf("\"issuccess\":false", StringComparison.Ordinal) >= 0;
    }

    private static string ResolveAccessToken()
    {
        return AuthManager.Instance != null
            ? AuthManager.Instance.GetAccessToken()
            : string.Empty;
    }

    private static string ResolveUserId()
    {
        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
            return string.Empty;

        return AuthManager.Instance.CurrentUser.userId ?? string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return null;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        }

        return null;
    }

    private void EmitFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "채팅방 생성에 실패했습니다."
            : userMessage;

        OnCreateFailed?.Invoke(message);
        Log($"Chat room create failed: {message}");
    }

    private void EmitListFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "채팅방 목록 조회에 실패했습니다."
            : userMessage;

        OnListFailed?.Invoke(message);
        Log($"Chat room list fetch failed: {message}");
    }

    private static string BuildRoomTitlesLog(ChatRoomSummaryInfo[] rooms)
    {
        if (rooms == null || rooms.Length == 0)
            return "[]";

        var sb = new StringBuilder("[");
        for (int i = 0; i < rooms.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");

            ChatRoomSummaryInfo room = rooms[i];
            if (room == null)
            {
                sb.Append("(null)");
                continue;
            }

            string title = string.IsNullOrWhiteSpace(room.Title)
                ? $"(untitled:{room.RoomId})"
                : room.Title;

            sb.Append(title);
        }

        sb.Append("]");
        return sb.ToString();
    }

    private void Log(string message)
    {
        if (!_debugLog)
            return;

        Debug.Log($"[ChatRoomManager] {message}");
    }

    private void OnDisable()
    {
        CancelCurrentRequest();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

#pragma warning disable CS0649

    [Serializable]
    private class CreateChatRoomRequestPayload
    {
        public string title;
        public string roomName;
        public int maxUserCount;
        public int max_user_count;
        public string userId;
        public string hostUserId;
    }

    [Serializable]
    private class ChatRoomServiceResponse
    {
        public bool success;
        public bool isSuccess;

        public string message;
        public string error;
        public string code;
        public string status;

        public string roomId;
        public string room_id;
        public string title;
        public string userId;
        public string user_id;
        public int maxUserCount;
        public int max_user_count;
        public string createdAt;
        public string created_at;

        public ChatRoomPayload room;
    }

    [Serializable]
    private class ChatRoomPayload
    {
        public string roomId;
        public string room_id;
        public string id;
        public string title;
        public string roomName;
        public string name;
        public string ownerUserId;
        public string owner_user_id;
        public string hostUserId;
        public string userId;
        public string user_id;
        public int maxUserCount;
        public int max_user_count;
        public string status;
        public string createdAt;
        public string created_at;
    }

    [Serializable]
    private class ChatRoomListArrayWrapper
    {
        public ChatRoomPayload[] rooms;
    }

    [Serializable]
    private class ChatRoomListRoomsResponse
    {
        public ChatRoomPayload[] rooms;
        public ChatRoomPayload[] list;
    }

    [Serializable]
    private class ChatRoomListDataArrayResponse
    {
        public ChatRoomPayload[] data;
    }

    [Serializable]
    private class ChatRoomListDataObjectResponse
    {
        public ChatRoomListDataPayload data;
    }

    [Serializable]
    private class ChatRoomListDataPayload
    {
        public ChatRoomPayload[] rooms;
        public ChatRoomPayload[] list;
    }

#pragma warning restore CS0649
}

[Serializable]
public sealed class ChatRoomCreateInfo
{
    public string RoomId;
    public string Title;
    public string OwnerUserId;
    public int MaxUserCount;
    public string Status;
    public string CreatedAtUtc;
}

[Serializable]
public sealed class ChatRoomSummaryInfo
{
    public string RoomId;
    public string Title;
    public string OwnerUserId;
    public int MaxUserCount;
    public string Status;
    public string CreatedAtUtc;
}
