using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
    [SerializeField] private string _joinRequestEndpointTemplate = "http://ioteacher.com/api/chat/rooms/{roomId}/join-request";
    [SerializeField] private string _joinRequestsEndpointTemplate = "http://ioteacher.com/api/chat/rooms/{roomId}/join-requests";
    [SerializeField] private string _joinRequestDecisionEndpointTemplate = "http://ioteacher.com/api/chat/join-requests/{requestId}/decision";
    [SerializeField] private string _myJoinRequestStatusEndpointTemplate = "http://ioteacher.com/api/chat/my/join-request/{requestId}";

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
    public event Action<string> OnJoinRequestStarted;
    public event Action<ChatRoomJoinRequestInfo> OnJoinRequestSucceeded;
    public event Action<string> OnJoinRequestFailed;
    public event Action OnJoinRequestCanceled;
    public event Action<string> OnJoinRequestsFetchStarted;
    public event Action<ChatRoomJoinRequestInfo[]> OnJoinRequestsFetchSucceeded;
    public event Action<string> OnJoinRequestsFetchFailed;
    public event Action OnJoinRequestsFetchCanceled;
    public event Action<string, string, bool> OnJoinRequestDecisionStarted;
    public event Action<ChatRoomJoinRequestDecisionInfo> OnJoinRequestDecisionSucceeded;
    public event Action<string, string, bool, string> OnJoinRequestDecisionFailed;
    public event Action<string, string, bool> OnJoinRequestDecisionCanceled;
    public event Action<string> OnMyJoinRequestStatusFetchStarted;
    public event Action<ChatRoomJoinRequestInfo> OnMyJoinRequestStatusFetchSucceeded;
    public event Action<string, string> OnMyJoinRequestStatusFetchFailed;
    public event Action<string> OnMyJoinRequestStatusFetchCanceled;

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
    /// 특정 방에 대한 입장 요청을 생성한다.
    /// </summary>
    /// <param name="roomIdRaw">대상 방 ID</param>
    /// <param name="accessTokenOverride">옵션: 기본 토큰 대신 사용할 Bearer 토큰</param>
    public void RequestJoinRequest(string roomIdRaw, string accessTokenOverride = null)
    {
        _ = RequestJoinRequestAsync(roomIdRaw, accessTokenOverride);
    }

    /// <summary>
    /// 특정 방의 입장 요청 목록을 조회한다.
    /// </summary>
    /// <param name="roomIdRaw">대상 방 ID</param>
    /// <param name="accessTokenOverride">옵션: 기본 토큰 대신 사용할 Bearer 토큰</param>
    public void FetchJoinRequests(string roomIdRaw, string accessTokenOverride = null)
    {
        _ = FetchJoinRequestsAsync(roomIdRaw, accessTokenOverride);
    }

    /// <summary>
    /// Host가 입장 요청을 수락/거절 처리한다.
    /// </summary>
    /// <param name="roomIdRaw">대상 방 ID</param>
    /// <param name="requestIdRaw">대상 입장 요청 ID</param>
    /// <param name="approve">true: 수락, false: 거절</param>
    /// <param name="accessTokenOverride">옵션: 기본 토큰 대신 사용할 Bearer 토큰</param>
    public void DecideJoinRequest(
        string roomIdRaw,
        string requestIdRaw,
        bool approve,
        string accessTokenOverride = null)
    {
        _ = DecideJoinRequestAsync(roomIdRaw, requestIdRaw, approve, accessTokenOverride);
    }

    /// <summary>
    /// Client 본인의 입장 요청 상태를 조회한다.
    /// </summary>
    /// <param name="requestIdRaw">입장 요청 ID</param>
    /// <param name="accessTokenOverride">옵션: 기본 토큰 대신 사용할 Bearer 토큰</param>
    public void FetchMyJoinRequestStatus(string requestIdRaw, string accessTokenOverride = null)
    {
        _ = FetchMyJoinRequestStatusAsync(requestIdRaw, accessTokenOverride);
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

    private async Task RequestJoinRequestAsync(string roomIdRaw, string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitJoinRequestFailure("이미 다른 채팅 요청을 처리 중입니다.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitJoinRequestFailure("입장 요청 대상 방 ID가 비어 있습니다.");
            return;
        }

        string endpoint = BuildRoomScopedEndpoint(_joinRequestEndpointTemplate, roomId, "join-request");
        Log($"Join request endpoint resolved. template={_joinRequestEndpointTemplate}, roomIdRaw={roomIdRaw}, roomId={roomId}, endpoint={endpoint}");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitJoinRequestFailure("입장 요청 API URL이 비어 있습니다.");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnJoinRequestStarted?.Invoke(roomId);

            string accessToken = ResolveAccessToken(accessTokenOverride);
            Log($"Join request auth header attached={!string.IsNullOrWhiteSpace(accessToken)}");

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrWhiteSpace(accessToken))
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnJoinRequestCanceled?.Invoke();
                    Log($"Join request canceled. roomId={roomId}");
                    return;
                }

                string responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Log($"Join request raw response. result={request.result}, code={request.responseCode}, error={request.error}, body={responseBody}");

                ChatRoomJoinRequestInfo joinRequestInfo = ParseJoinRequestCreateResponse(request, roomId);
                if (joinRequestInfo != null)
                {
                    OnJoinRequestSucceeded?.Invoke(joinRequestInfo);
                    Log($"Join request sent. roomId={joinRequestInfo.RoomId}, requestId={joinRequestInfo.RequestId}, status={joinRequestInfo.Status}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            OnJoinRequestCanceled?.Invoke();
            Log($"Join request canceled by token. roomId={roomId}");
        }
        catch (Exception e)
        {
            EmitJoinRequestFailure($"방 입장 요청 중 예외가 발생했습니다. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task FetchJoinRequestsAsync(string roomIdRaw, string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitJoinRequestsFetchFailure("이미 다른 채팅 요청을 처리 중입니다.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitJoinRequestsFetchFailure("입장 요청 목록 대상 방 ID가 비어 있습니다.");
            return;
        }

        string endpoint = BuildRoomScopedEndpoint(_joinRequestsEndpointTemplate, roomId, "join-requests");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitJoinRequestsFetchFailure("입장 요청 목록 API URL이 비어 있습니다.");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnJoinRequestsFetchStarted?.Invoke(roomId);

            string accessToken = ResolveAccessToken(accessTokenOverride);

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
                    OnJoinRequestsFetchCanceled?.Invoke();
                    Log($"Join request list fetch canceled. roomId={roomId}");
                    return;
                }

                ChatRoomJoinRequestInfo[] joinRequests = ParseJoinRequestListResponse(request, roomId);
                if (joinRequests != null)
                {
                    OnJoinRequestsFetchSucceeded?.Invoke(joinRequests);
                    Log($"Join request list fetched. roomId={roomId}, count={joinRequests.Length}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            OnJoinRequestsFetchCanceled?.Invoke();
            Log($"Join request list fetch canceled by token. roomId={roomId}");
        }
        catch (Exception e)
        {
            EmitJoinRequestsFetchFailure($"입장 요청 목록 조회 중 예외가 발생했습니다. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task DecideJoinRequestAsync(
        string roomIdRaw,
        string requestIdRaw,
        bool approve,
        string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitJoinRequestDecisionFailure(roomIdRaw, requestIdRaw, approve, "이미 다른 채팅 요청을 처리 중입니다.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitJoinRequestDecisionFailure(roomIdRaw, requestIdRaw, approve, "입장 요청 처리 대상 방 ID가 비어 있습니다.");
            return;
        }

        string requestId = string.IsNullOrWhiteSpace(requestIdRaw) ? string.Empty : requestIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            EmitJoinRequestDecisionFailure(roomId, requestIdRaw, approve, "입장 요청 처리 대상 요청 ID가 비어 있습니다.");
            return;
        }

        string decisionEndpoint = BuildJoinRequestDecisionEndpoint(
            _joinRequestDecisionEndpointTemplate,
            roomId,
            requestId);

        if (string.IsNullOrWhiteSpace(decisionEndpoint))
        {
            EmitJoinRequestDecisionFailure(roomId, requestId, approve, "입장 요청 처리 API URL이 비어 있습니다.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitJoinRequestDecisionFailure(roomId, requestId, approve, "입장 요청 처리에는 로그인 토큰이 필요합니다.");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnJoinRequestDecisionStarted?.Invoke(roomId, requestId, approve);

            string status = approve ? "APPROVED" : "REJECTED";

            var payload = new JoinRequestDecisionRequestPayload
            {
                decision = status,
                reviewComment = approve ? "승인합니다." : "거절합니다."
            };

            string requestJson = JsonUtility.ToJson(payload);
            JoinRequestDecisionAttemptResult attempt = await SendJoinRequestDecisionAttemptAsync(
                decisionEndpoint,
                UnityWebRequest.kHttpVerbPOST,
                requestJson,
                accessToken,
                _requestCancellation.Token);

            if (attempt.IsCanceled)
            {
                OnJoinRequestDecisionCanceled?.Invoke(roomId, requestId, approve);
                Log($"Join request decision canceled. roomId={roomId}, requestId={requestId}, approve={approve}");
                return;
            }

            if (attempt.IsSuccess)
            {
                var result = new ChatRoomJoinRequestDecisionInfo
                {
                    RoomId = roomId,
                    RequestId = requestId,
                    Approved = approve,
                    Status = status,
                    ResponseCode = attempt.ResponseCode,
                    ResponseBody = attempt.ResponseBody
                };

                OnJoinRequestDecisionSucceeded?.Invoke(result);
                Log(
                    $"Join request decision succeeded. roomId={roomId}, requestId={requestId}, approve={approve}, endpoint={decisionEndpoint}, code={attempt.ResponseCode}");
                return;
            }

            string lastError = FirstNonEmpty(
                attempt.ErrorMessage,
                attempt.ResponseCode > 0 ? $"HTTP {attempt.ResponseCode}" : null,
                "방 입장 요청 처리에 실패했습니다.");

            EmitJoinRequestDecisionFailure(roomId, requestId, approve, lastError);
            Log(
                $"Join request decision failed. roomId={roomId}, requestId={requestId}, approve={approve}, endpoint={decisionEndpoint}, code={attempt.ResponseCode}, body={attempt.ResponseBody}");
        }
        catch (OperationCanceledException)
        {
            OnJoinRequestDecisionCanceled?.Invoke(roomId, requestId, approve);
            Log($"Join request decision canceled by token. roomId={roomId}, requestId={requestId}, approve={approve}");
        }
        catch (Exception e)
        {
            EmitJoinRequestDecisionFailure(roomId, requestId, approve, $"입장 요청 처리 중 예외가 발생했습니다. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task FetchMyJoinRequestStatusAsync(string requestIdRaw, string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitMyJoinRequestStatusFetchFailure(requestIdRaw, "이미 다른 채팅 요청을 처리 중입니다.");
            return;
        }

        string requestId = string.IsNullOrWhiteSpace(requestIdRaw) ? string.Empty : requestIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            EmitMyJoinRequestStatusFetchFailure(requestIdRaw, "입장 요청 상태 조회 대상 요청 ID가 비어 있습니다.");
            return;
        }

        string endpoint = BuildRequestScopedEndpoint(_myJoinRequestStatusEndpointTemplate, requestId);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitMyJoinRequestStatusFetchFailure(requestId, "입장 요청 상태 조회 API URL이 비어 있습니다.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitMyJoinRequestStatusFetchFailure(requestId, "입장 요청 상태 조회에는 로그인 토큰이 필요합니다.");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnMyJoinRequestStatusFetchStarted?.Invoke(requestId);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnMyJoinRequestStatusFetchCanceled?.Invoke(requestId);
                    Log($"My join request status fetch canceled. requestId={requestId}");
                    return;
                }

                ChatRoomJoinRequestInfo info = ParseMyJoinRequestStatusResponse(request, requestId);
                if (info != null)
                {
                    OnMyJoinRequestStatusFetchSucceeded?.Invoke(info);
                    Log($"My join request status fetched. requestId={info.RequestId}, roomId={info.RoomId}, status={info.Status}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            OnMyJoinRequestStatusFetchCanceled?.Invoke(requestId);
            Log($"My join request status fetch canceled by token. requestId={requestId}");
        }
        catch (Exception e)
        {
            EmitMyJoinRequestStatusFetchFailure(requestId, $"입장 요청 상태 조회 중 예외가 발생했습니다. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task<JoinRequestDecisionAttemptResult> SendJoinRequestDecisionAttemptAsync(
        string endpoint,
        string method,
        string requestJson,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new JoinRequestDecisionAttemptResult
            {
                IsSuccess = false,
                IsCanceled = false,
                ErrorMessage = "입장 요청 처리 API URL이 비어 있습니다."
            };
        }

        using (var request = new UnityWebRequest(endpoint, method))
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(requestJson) ? "{}" : requestJson);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            bool isCanceled = await SendRequestAsync(request, cancellationToken);
            if (isCanceled)
            {
                return new JoinRequestDecisionAttemptResult
                {
                    IsSuccess = false,
                    IsCanceled = true
                };
            }

            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            JoinRequestSingleResponse response = TryParseJson<JoinRequestSingleResponse>(body);
            bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return new JoinRequestDecisionAttemptResult
                {
                    IsSuccess = false,
                    IsCanceled = false,
                    ResponseCode = request.responseCode,
                    ResponseBody = body,
                    ErrorMessage = "네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요."
                };
            }

            if (!httpSuccess || HasExplicitFailureFlag(body))
            {
                string errorMessage = FirstNonEmpty(
                    response != null ? response.message : null,
                    response != null ? response.error : null,
                    request.error,
                    $"HTTP {request.responseCode}",
                    "방 입장 요청 처리에 실패했습니다.");

                return new JoinRequestDecisionAttemptResult
                {
                    IsSuccess = false,
                    IsCanceled = false,
                    ResponseCode = request.responseCode,
                    ResponseBody = body,
                    ErrorMessage = errorMessage
                };
            }

            return new JoinRequestDecisionAttemptResult
            {
                IsSuccess = true,
                IsCanceled = false,
                ResponseCode = request.responseCode,
                ResponseBody = body
            };
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

    private ChatRoomJoinRequestInfo ParseJoinRequestCreateResponse(UnityWebRequest request, string requestedRoomId)
    {
        if (request == null)
        {
            EmitJoinRequestFailure("요청 객체가 없습니다.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitJoinRequestFailure("네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        JoinRequestSingleResponse response = TryParseJson<JoinRequestSingleResponse>(body);
        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

        if (!httpSuccess || HasExplicitFailureFlag(body))
        {
            string errorMessage = FirstNonEmpty(
                response != null ? response.message : null,
                response != null ? response.error : null,
                $"HTTP {request.responseCode}",
                "방 입장 요청에 실패했습니다.");

            EmitJoinRequestFailure(errorMessage);
            return null;
        }

        ChatRoomJoinRequestInfo info = BuildJoinRequestInfoFromSingleResponse(
            body,
            response,
            requestedRoomId,
            fallbackRequestId: null);

        if (info != null)
            return info;

        return new ChatRoomJoinRequestInfo
        {
            RequestId = null,
            RoomId = requestedRoomId,
            RequestUserId = string.Empty,
            Status = "REQUESTED",
            CreatedAtUtc = string.Empty
        };
    }

    private ChatRoomJoinRequestInfo ParseMyJoinRequestStatusResponse(UnityWebRequest request, string requestedRequestId)
    {
        if (request == null)
        {
            EmitMyJoinRequestStatusFetchFailure(requestedRequestId, "요청 객체가 없습니다.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitMyJoinRequestStatusFetchFailure(requestedRequestId, "네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        JoinRequestSingleResponse response = TryParseJson<JoinRequestSingleResponse>(body);
        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

        if (!httpSuccess || HasExplicitFailureFlag(body))
        {
            string errorMessage = FirstNonEmpty(
                response != null ? response.message : null,
                response != null ? response.error : null,
                $"HTTP {request.responseCode}",
                "입장 요청 상태 조회에 실패했습니다.");

            EmitMyJoinRequestStatusFetchFailure(requestedRequestId, errorMessage);
            return null;
        }

        ChatRoomJoinRequestInfo info = BuildJoinRequestInfoFromSingleResponse(
            body,
            response,
            fallbackRoomId: string.Empty,
            fallbackRequestId: requestedRequestId);

        if (info != null)
            return info;

        return new ChatRoomJoinRequestInfo
        {
            RequestId = requestedRequestId,
            RoomId = string.Empty,
            RequestUserId = string.Empty,
            Status = "REQUESTED",
            CreatedAtUtc = string.Empty
        };
    }

    private ChatRoomJoinRequestInfo[] ParseJoinRequestListResponse(UnityWebRequest request, string roomId)
    {
        if (request == null)
        {
            EmitJoinRequestsFetchFailure("요청 객체가 없습니다.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitJoinRequestsFetchFailure("네트워크 오류가 발생했습니다. 잠시 후 다시 시도해 주세요.");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        JoinRequestSingleResponse response = TryParseJson<JoinRequestSingleResponse>(body);
        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

        if (!httpSuccess || HasExplicitFailureFlag(body))
        {
            string errorMessage = FirstNonEmpty(
                response != null ? response.message : null,
                response != null ? response.error : null,
                $"HTTP {request.responseCode}",
                "방 입장 요청 목록 조회에 실패했습니다.");

            EmitJoinRequestsFetchFailure(errorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<ChatRoomJoinRequestInfo>();

        JoinRequestPayload[] payloads = ExtractJoinRequestPayloads(body);
        if (payloads == null)
        {
            EmitJoinRequestsFetchFailure("방 입장 요청 목록 응답을 해석할 수 없습니다.");
            return null;
        }

        var results = new List<ChatRoomJoinRequestInfo>(payloads.Length);

        for (int i = 0; i < payloads.Length; i++)
        {
            ChatRoomJoinRequestInfo info = ToJoinRequestInfo(payloads[i], roomId);
            if (info != null)
                results.Add(info);
        }

        return results.ToArray();
    }

    private static JoinRequestPayload[] ExtractJoinRequestPayloads(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<JoinRequestPayload>();

        string trimmedBody = body.Trim();

        if (trimmedBody.StartsWith("[", StringComparison.Ordinal))
        {
            string wrapped = $"{{\"requests\":{trimmedBody}}}";
            JoinRequestArrayWrapper wrappedResponse = TryParseJson<JoinRequestArrayWrapper>(wrapped);
            return wrappedResponse != null ? wrappedResponse.requests : null;
        }

        JoinRequestListResponse listResponse = TryParseJson<JoinRequestListResponse>(trimmedBody);
        if (listResponse != null)
        {
            if (listResponse.requests != null)
                return listResponse.requests;
            if (listResponse.joinRequests != null)
                return listResponse.joinRequests;
            if (listResponse.list != null)
                return listResponse.list;
            if (listResponse.data != null)
                return listResponse.data;
            if (listResponse.items != null)
                return listResponse.items;
        }

        JoinRequestDataObjectResponse dataObjectResponse = TryParseJson<JoinRequestDataObjectResponse>(trimmedBody);
        if (dataObjectResponse != null && dataObjectResponse.data != null)
        {
            if (dataObjectResponse.data.requests != null)
                return dataObjectResponse.data.requests;
            if (dataObjectResponse.data.joinRequests != null)
                return dataObjectResponse.data.joinRequests;
            if (dataObjectResponse.data.list != null)
                return dataObjectResponse.data.list;
            if (dataObjectResponse.data.items != null)
                return dataObjectResponse.data.items;
        }

        return null;
    }

    private static ChatRoomJoinRequestInfo ToJoinRequestInfo(JoinRequestPayload payload, string fallbackRoomId)
    {
        if (payload == null)
            return null;

        string requestId = FirstNonEmpty(payload.requestId, payload.request_id, payload.id);
        string roomId = FirstNonEmpty(payload.roomId, payload.room_id, fallbackRoomId);
        string requestUserId = FirstNonEmpty(
            payload.requesterUserId,
            payload.requester_user_id,
            payload.requestUserId,
            payload.request_user_id,
            payload.userId,
            payload.user_id);

        if (string.IsNullOrWhiteSpace(requestId) &&
            string.IsNullOrWhiteSpace(roomId) &&
            string.IsNullOrWhiteSpace(requestUserId))
        {
            return null;
        }

        return new ChatRoomJoinRequestInfo
        {
            RequestId = requestId,
            RoomId = roomId,
            RequestUserId = requestUserId,
            Status = FirstNonEmpty(payload.status, "REQUESTED"),
            CreatedAtUtc = FirstNonEmpty(payload.createdAt, payload.created_at, string.Empty)
        };
    }

    private static ChatRoomJoinRequestInfo BuildJoinRequestInfoFromSingleResponse(
        string body,
        JoinRequestSingleResponse response,
        string fallbackRoomId,
        string fallbackRequestId)
    {
        JoinRequestPayload payload = FirstNonEmptyJoinRequestPayload(
            response != null ? response.joinRequest : null,
            response != null ? response.request : null,
            response != null ? response.data : null);

        ChatRoomJoinRequestInfo payloadInfo = ToJoinRequestInfo(payload, fallbackRoomId);

        string requestId = FirstNonEmpty(
            payloadInfo != null ? payloadInfo.RequestId : null,
            response != null ? FirstNonEmpty(response.requestId, response.request_id) : null,
            ExtractJsonScalarAsString(body, "requestId"),
            ExtractJsonScalarAsString(body, "request_id"),
            ExtractJsonScalarAsString(body, "id"),
            fallbackRequestId);

        string roomId = FirstNonEmpty(
            payloadInfo != null ? payloadInfo.RoomId : null,
            response != null ? FirstNonEmpty(response.roomId, response.room_id) : null,
            ExtractJsonScalarAsString(body, "roomId"),
            ExtractJsonScalarAsString(body, "room_id"),
            fallbackRoomId);

        string requestUserId = FirstNonEmpty(
            payloadInfo != null ? payloadInfo.RequestUserId : null,
            response != null ? FirstNonEmpty(
                response.requesterUserId,
                response.requester_user_id,
                response.requestUserId,
                response.request_user_id,
                response.userId,
                response.user_id) : null,
            ExtractJsonScalarAsString(body, "requesterUserId"),
            ExtractJsonScalarAsString(body, "requester_user_id"),
            ExtractJsonScalarAsString(body, "requestUserId"),
            ExtractJsonScalarAsString(body, "request_user_id"),
            ExtractJsonScalarAsString(body, "userId"),
            ExtractJsonScalarAsString(body, "user_id"),
            string.Empty);

        string status = FirstNonEmpty(
            payloadInfo != null ? payloadInfo.Status : null,
            response != null ? response.status : null,
            ExtractJsonScalarAsString(body, "status"),
            "REQUESTED");

        string createdAt = FirstNonEmpty(
            payloadInfo != null ? payloadInfo.CreatedAtUtc : null,
            response != null ? FirstNonEmpty(response.createdAt, response.created_at) : null,
            ExtractJsonScalarAsString(body, "createdAt"),
            ExtractJsonScalarAsString(body, "created_at"),
            string.Empty);

        if (string.IsNullOrWhiteSpace(requestId) &&
            string.IsNullOrWhiteSpace(roomId) &&
            string.IsNullOrWhiteSpace(requestUserId) &&
            string.IsNullOrWhiteSpace(status) &&
            string.IsNullOrWhiteSpace(createdAt))
        {
            return null;
        }

        return new ChatRoomJoinRequestInfo
        {
            RequestId = requestId,
            RoomId = roomId,
            RequestUserId = requestUserId,
            Status = status,
            CreatedAtUtc = createdAt
        };
    }

    private static JoinRequestPayload FirstNonEmptyJoinRequestPayload(params JoinRequestPayload[] payloads)
    {
        if (payloads == null)
            return null;

        for (int i = 0; i < payloads.Length; i++)
        {
            if (payloads[i] != null)
                return payloads[i];
        }

        return null;
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

    private static string ExtractJsonScalarAsString(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            return null;

        string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(\"(?<text>(?:\\\\.|[^\"\\\\])*)\"|(?<number>-?\\d+(?:\\.\\d+)?)|(?<bool>true|false)|null)";
        Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        if (match.Groups["text"].Success)
            return UnescapeJsonString(match.Groups["text"].Value);

        if (match.Groups["number"].Success)
            return match.Groups["number"].Value;

        if (match.Groups["bool"].Success)
            return match.Groups["bool"].Value;

        return null;
    }

    private static string UnescapeJsonString(string escaped)
    {
        if (escaped == null)
            return null;

        try
        {
            return Regex.Unescape(escaped);
        }
        catch (Exception)
        {
            return escaped;
        }
    }

    private static string BuildRoomScopedEndpoint(string endpointTemplate, string roomId, string fallbackSuffix)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(endpointTemplate))
            return string.Empty;

        string encodedRoomId = UnityWebRequest.EscapeURL(roomId.Trim());
        string template = endpointTemplate.Trim();

        if (template.IndexOf("{roomId}", StringComparison.Ordinal) >= 0)
            return template.Replace("{roomId}", encodedRoomId);

        string suffix = string.IsNullOrWhiteSpace(fallbackSuffix)
            ? string.Empty
            : fallbackSuffix.StartsWith("/", StringComparison.Ordinal)
                ? fallbackSuffix
                : $"/{fallbackSuffix}";

        return $"{template.TrimEnd('/')}/{encodedRoomId}{suffix}";
    }

    private static string BuildRequestScopedEndpoint(string endpointTemplate, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(endpointTemplate))
            return string.Empty;

        string encodedRequestId = UnityWebRequest.EscapeURL(requestId.Trim());
        string template = endpointTemplate.Trim();

        if (template.IndexOf("{requestId}", StringComparison.Ordinal) >= 0)
            return template.Replace("{requestId}", encodedRequestId);

        return $"{template.TrimEnd('/')}/{encodedRequestId}";
    }

    private static string BuildJoinRequestDecisionEndpoint(
        string endpointTemplate,
        string roomId,
        string requestId)
    {
        if (string.IsNullOrWhiteSpace(endpointTemplate) || string.IsNullOrWhiteSpace(requestId))
            return string.Empty;

        string encodedRoomId = string.IsNullOrWhiteSpace(roomId)
            ? string.Empty
            : UnityWebRequest.EscapeURL(roomId.Trim());
        string encodedRequestId = UnityWebRequest.EscapeURL(requestId.Trim());
        string resolved = endpointTemplate.Trim();

        if (resolved.IndexOf("{roomId}", StringComparison.Ordinal) >= 0)
            resolved = resolved.Replace("{roomId}", encodedRoomId);

        bool hasRequestIdPlaceholder = resolved.IndexOf("{requestId}", StringComparison.Ordinal) >= 0;
        if (hasRequestIdPlaceholder)
        {
            resolved = resolved.Replace("{requestId}", encodedRequestId);
        }
        else if (!resolved.EndsWith($"/{encodedRequestId}", StringComparison.Ordinal))
        {
            resolved = $"{resolved.TrimEnd('/')}/{encodedRequestId}";
        }

        if (!resolved.EndsWith("/decision", StringComparison.OrdinalIgnoreCase))
            resolved = $"{resolved.TrimEnd('/')}/decision";

        return resolved;
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

    private static string ResolveAccessToken(string accessTokenOverride)
    {
        if (!string.IsNullOrWhiteSpace(accessTokenOverride))
            return accessTokenOverride.Trim();

        return ResolveAccessToken();
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

    private void EmitJoinRequestFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "방 입장 요청에 실패했습니다."
            : userMessage;

        OnJoinRequestFailed?.Invoke(message);
        Log($"Join request failed: {message}");
    }

    private void EmitJoinRequestsFetchFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "방 입장 요청 목록 조회에 실패했습니다."
            : userMessage;

        OnJoinRequestsFetchFailed?.Invoke(message);
        Log($"Join request list fetch failed: {message}");
    }

    private void EmitJoinRequestDecisionFailure(string roomId, string requestId, bool approve, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "방 입장 요청 처리에 실패했습니다."
            : userMessage;

        OnJoinRequestDecisionFailed?.Invoke(
            roomId ?? string.Empty,
            requestId ?? string.Empty,
            approve,
            message);

        Log($"Join request decision failed: roomId={roomId}, requestId={requestId}, approve={approve}, message={message}");
    }

    private void EmitMyJoinRequestStatusFetchFailure(string requestId, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "입장 요청 상태 조회에 실패했습니다."
            : userMessage;

        OnMyJoinRequestStatusFetchFailed?.Invoke(requestId ?? string.Empty, message);
        Log($"My join request status fetch failed: requestId={requestId}, message={message}");
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

    [Serializable]
    private class JoinRequestSingleResponse
    {
        public bool success;
        public bool isSuccess;

        public string message;
        public string error;
        public string status;

        public string requestId;
        public string request_id;
        public string roomId;
        public string room_id;
        public string requesterUserId;
        public string requester_user_id;
        public string requestUserId;
        public string request_user_id;
        public string userId;
        public string user_id;
        public string createdAt;
        public string created_at;

        public JoinRequestPayload request;
        public JoinRequestPayload joinRequest;
        public JoinRequestPayload data;
    }

    [Serializable]
    private class JoinRequestPayload
    {
        public string requestId;
        public string request_id;
        public string id;

        public string roomId;
        public string room_id;
        public string requesterUserId;
        public string requester_user_id;
        public string userId;
        public string user_id;
        public string requestUserId;
        public string request_user_id;
        public string status;
        public string createdAt;
        public string created_at;
    }

    [Serializable]
    private class JoinRequestDecisionRequestPayload
    {
        public string decision;
        public string reviewComment;
    }

    private struct JoinRequestDecisionAttemptResult
    {
        public bool IsSuccess;
        public bool IsCanceled;
        public long ResponseCode;
        public string ResponseBody;
        public string ErrorMessage;
    }

    [Serializable]
    private class JoinRequestArrayWrapper
    {
        public JoinRequestPayload[] requests;
    }

    [Serializable]
    private class JoinRequestListResponse
    {
        public JoinRequestPayload[] requests;
        public JoinRequestPayload[] joinRequests;
        public JoinRequestPayload[] list;
        public JoinRequestPayload[] data;
        public JoinRequestPayload[] items;
    }

    [Serializable]
    private class JoinRequestDataObjectResponse
    {
        public JoinRequestDataPayload data;
    }

    [Serializable]
    private class JoinRequestDataPayload
    {
        public JoinRequestPayload[] requests;
        public JoinRequestPayload[] joinRequests;
        public JoinRequestPayload[] list;
        public JoinRequestPayload[] items;
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

[Serializable]
public sealed class ChatRoomJoinRequestInfo
{
    public string RequestId;
    public string RoomId;
    public string RequestUserId;
    public string Status;
    public string CreatedAtUtc;
}

[Serializable]
public sealed class ChatRoomJoinRequestDecisionInfo
{
    public string RoomId;
    public string RequestId;
    public bool Approved;
    public string Status;
    public long ResponseCode;
    public string ResponseBody;
}
