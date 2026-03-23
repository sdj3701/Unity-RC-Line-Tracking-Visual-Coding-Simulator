using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Auth;
using UnityEngine;
using UnityEngine.Networking;

public class ChatRoomManager : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private string _createRoomEndpoint = "http://ioteacher.com/api/chat/rooms";

    [Header("Timeout")]
    [SerializeField] private int _requestTimeoutSeconds = 15;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    public event Action<string, int> OnCreateStarted;
    public event Action<ChatRoomCreateInfo> OnCreateSucceeded;
    public event Action<string> OnCreateFailed;
    public event Action OnCreateCanceled;

    public bool IsBusy { get; private set; }

    private CancellationTokenSource _requestCancellation;

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
        public string userId;
        public string user_id;
        public int maxUserCount;
        public int max_user_count;
        public string status;
        public string createdAt;
        public string created_at;
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
