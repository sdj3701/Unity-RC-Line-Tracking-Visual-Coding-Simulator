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
    [SerializeField] private string _blockShareEndpointTemplate = "http://ioteacher.com/api/chat/rooms/{roomId}/block-shares";
    [SerializeField] private string _blockShareDetailEndpointTemplate = "http://ioteacher.com/api/chat/rooms/{roomId}/block-shares/{shareId}";
    [SerializeField] private string _saveBlockShareToMyLevelEndpointTemplate = "http://ioteacher.com/api/chat/block-shares/{shareId}/save-to-my-level";

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
    public event Action<string, int, int> OnBlockShareListFetchStarted;
    public event Action<ChatRoomBlockShareListInfo> OnBlockShareListFetchSucceeded;
    public event Action<string, int, int, string> OnBlockShareListFetchFailed;
    public event Action<string, int, int> OnBlockShareListFetchCanceled;
    public event Action<string, string> OnBlockShareDetailFetchStarted;
    public event Action<ChatRoomBlockShareInfo> OnBlockShareDetailFetchSucceeded;
    public event Action<string, string, string> OnBlockShareDetailFetchFailed;
    public event Action<string, string> OnBlockShareDetailFetchCanceled;
    public event Action<string, int, string> OnBlockShareUploadStarted;
    public event Action<ChatRoomBlockShareUploadInfo> OnBlockShareUploadSucceeded;
    public event Action<string, int, string> OnBlockShareUploadFailed;
    public event Action<string, int> OnBlockShareUploadCanceled;
    public event Action<string> OnBlockShareSaveStarted;
    public event Action<ChatRoomBlockShareSaveInfo> OnBlockShareSaveSucceeded;
    public event Action<string, string> OnBlockShareSaveFailed;
    public event Action<string> OnBlockShareSaveCanceled;

    public bool IsBusy { get; private set; }

    private CancellationTokenSource _requestCancellation;
    private static readonly string[] _detailJsonCandidateKeys =
    {
        "json",
        "jsonData",
        "codeJson",
        "blockJson",
        "blockCodeJson",
        "workspaceJson",
        "payloadJson"
    };

    private static readonly string[] _detailXmlCandidateKeys =
    {
        "xml",
        "xmlData",
        "codeXml",
        "blockXml",
        "blockCodeXml",
        "workspaceXml",
        "payloadXml"
    };

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
    /// 梨꾪똿諛??앹꽦 ?붿껌 吏꾩엯??
    /// room title / max user count瑜?寃利앺븳 ????API???앹꽦???붿껌?쒕떎.
    /// </summary>
    /// <param name="roomTitleRaw">?ъ슜???낅젰 諛??쒕ぉ</param>
    /// <param name="maxUserCountRaw">?ъ슜???낅젰 理쒕? ?몄썝 臾몄옄??/param>
    public void CreateRoom(string roomTitleRaw, string maxUserCountRaw)
    {
        _ = CreateRoomAsync(roomTitleRaw, maxUserCountRaw);
    }

    /// <summary>
    /// 梨꾪똿諛?紐⑸줉 議고쉶 ?붿껌 吏꾩엯??
    /// </summary>
    public void FetchRoomList()
    {
        _ = FetchRoomListAsync();
    }

    /// <summary>
    /// ?뱀젙 諛⑹뿉 ????낆옣 ?붿껌???앹꽦?쒕떎.
    /// </summary>
    /// <param name="roomIdRaw">???諛?ID</param>
    /// <param name="accessTokenOverride">?듭뀡: 湲곕낯 ?좏겙 ????ъ슜??Bearer ?좏겙</param>
    public void RequestJoinRequest(string roomIdRaw, string accessTokenOverride = null)
    {
        _ = RequestJoinRequestAsync(roomIdRaw, accessTokenOverride);
    }

    /// <summary>
    /// ?뱀젙 諛⑹쓽 ?낆옣 ?붿껌 紐⑸줉??議고쉶?쒕떎.
    /// </summary>
    /// <param name="roomIdRaw">???諛?ID</param>
    /// <param name="accessTokenOverride">?듭뀡: 湲곕낯 ?좏겙 ????ъ슜??Bearer ?좏겙</param>
    public void FetchJoinRequests(string roomIdRaw, string accessTokenOverride = null)
    {
        _ = FetchJoinRequestsAsync(roomIdRaw, accessTokenOverride);
    }

    /// <summary>
    /// Host媛 ?낆옣 ?붿껌???섎씫/嫄곗젅 泥섎━?쒕떎.
    /// </summary>
    /// <param name="roomIdRaw">???諛?ID</param>
    /// <param name="requestIdRaw">????낆옣 ?붿껌 ID</param>
    /// <param name="approve">true: ?섎씫, false: 嫄곗젅</param>
    /// <param name="accessTokenOverride">?듭뀡: 湲곕낯 ?좏겙 ????ъ슜??Bearer ?좏겙</param>
    public void DecideJoinRequest(
        string roomIdRaw,
        string requestIdRaw,
        bool approve,
        string accessTokenOverride = null)
    {
        _ = DecideJoinRequestAsync(roomIdRaw, requestIdRaw, approve, accessTokenOverride);
    }

    /// <summary>
    /// Client 蹂몄씤???낆옣 ?붿껌 ?곹깭瑜?議고쉶?쒕떎.
    /// </summary>
    /// <param name="requestIdRaw">?낆옣 ?붿껌 ID</param>
    /// <param name="accessTokenOverride">?듭뀡: 湲곕낯 ?좏겙 ????ъ슜??Bearer ?좏겙</param>
    public void FetchMyJoinRequestStatus(string requestIdRaw, string accessTokenOverride = null)
    {
        _ = FetchMyJoinRequestStatusAsync(requestIdRaw, accessTokenOverride);
    }

    /// <summary>
    /// ?뱀젙 諛⑹쓽 釉붾줉 怨듭쑀 紐⑸줉??議고쉶?쒕떎.
    /// </summary>
    /// <param name="roomIdRaw">???諛?ID</param>
    /// <param name="page">?섏씠吏 踰덊샇(1遺???쒖옉)</param>
    /// <param name="size">?섏씠吏 ?ш린</param>
    /// <param name="accessTokenOverride">?듭뀡: 湲곕낯 ?좏겙 ????ъ슜??Bearer ?좏겙</param>
    public void FetchBlockShares(
        string roomIdRaw,
        int page = 1,
        int size = 20,
        string accessTokenOverride = null)
    {
        _ = FetchBlockSharesAsync(roomIdRaw, page, size, accessTokenOverride);
    }

    /// <summary>
    /// ?뱀젙 諛⑹뿉 釉붾줉 肄붾뱶瑜?怨듭쑀?쒕떎.
    /// </summary>
    /// <param name="roomIdRaw">???諛?ID</param>
    /// <param name="userLevelSeq">怨듭쑀???ъ슜???덈꺼 ?쒗??/param>
    /// <param name="message">怨듭쑀 硫붿떆吏</param>
    /// <param name="accessTokenOverride">?듭뀡: 湲곕낯 ?좏겙 ????ъ슜??Bearer ?좏겙</param>
    public void UploadBlockShare(
        string roomIdRaw,
        int userLevelSeq,
        string message,
        string accessTokenOverride = null)
    {
        _ = UploadBlockShareAsync(roomIdRaw, userLevelSeq, message, accessTokenOverride);
    }

    public void FetchBlockShareDetail(
        string roomIdRaw,
        string shareIdRaw,
        string accessTokenOverride = null)
    {
        _ = FetchBlockShareDetailAsync(roomIdRaw, shareIdRaw, accessTokenOverride);
    }

    public void SaveBlockShareToMyLevel(string shareIdRaw, string accessTokenOverride = null)
    {
        _ = SaveBlockShareToMyLevelAsync(shareIdRaw, accessTokenOverride);
    }

    /// <summary>
    /// 吏꾪뻾 以묒씤 梨꾪똿諛??앹꽦 ?붿껌??痍⑥냼?쒕떎.
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
            EmitFailure("?대? 梨꾪똿諛??앹꽦 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string roomTitle = string.IsNullOrWhiteSpace(roomTitleRaw) ? string.Empty : roomTitleRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomTitle))
        {
            EmitFailure("諛??쒕ぉ???낅젰??二쇱꽭??");
            return;
        }

        if (string.IsNullOrWhiteSpace(maxUserCountRaw))
        {
            EmitFailure("理쒕? ?몄썝???낅젰??二쇱꽭??");
            return;
        }

        if (!int.TryParse(maxUserCountRaw.Trim(), out int maxUserCount) || maxUserCount <= 0)
        {
            EmitFailure("理쒕? ?몄썝? 1 ?댁긽???レ옄?ъ빞 ?⑸땲??");
            return;
        }

        if (string.IsNullOrWhiteSpace(_createRoomEndpoint))
        {
            EmitFailure("梨꾪똿諛??앹꽦 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
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

                // ParseCreateResponse?먯꽌 ?ㅽ뙣 ?대깽?몃? 諛쒗뻾?섏? 紐삵븳 寃쎌슦瑜??鍮꾪븳 湲곕낯 泥섎━.
                EmitFailure("梨꾪똿諛??앹꽦???ㅽ뙣?덉뒿?덈떎.");
            }
        }
        catch (OperationCanceledException)
        {
            OnCreateCanceled?.Invoke();
            Log("Chat room creation canceled by token.");
        }
        catch (Exception e)
        {
            EmitFailure($"梨꾪똿諛??앹꽦 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
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
            EmitListFailure("?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string endpoint = string.IsNullOrWhiteSpace(_listRoomEndpoint)
            ? _createRoomEndpoint
            : _listRoomEndpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitListFailure("梨꾪똿諛?紐⑸줉 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
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
            EmitListFailure($"梨꾪똿諛?紐⑸줉 議고쉶 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
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
            EmitJoinRequestFailure("?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitJoinRequestFailure("?낆옣 ?붿껌 ???諛?ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string endpoint = BuildRoomScopedEndpoint(_joinRequestEndpointTemplate, roomId, "join-request");
        Log($"Join request endpoint resolved. template={_joinRequestEndpointTemplate}, roomIdRaw={roomIdRaw}, roomId={roomId}, endpoint={endpoint}");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitJoinRequestFailure("?낆옣 ?붿껌 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
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
            EmitJoinRequestFailure($"諛??낆옣 ?붿껌 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
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
            EmitJoinRequestsFetchFailure("?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitJoinRequestsFetchFailure("?낆옣 ?붿껌 紐⑸줉 ???諛?ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string endpoint = BuildRoomScopedEndpoint(_joinRequestsEndpointTemplate, roomId, "join-requests");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitJoinRequestsFetchFailure("?낆옣 ?붿껌 紐⑸줉 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
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
            EmitJoinRequestsFetchFailure($"?낆옣 ?붿껌 紐⑸줉 議고쉶 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
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
            EmitJoinRequestDecisionFailure(roomIdRaw, requestIdRaw, approve, "?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitJoinRequestDecisionFailure(roomIdRaw, requestIdRaw, approve, "?낆옣 ?붿껌 泥섎━ ???諛?ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string requestId = string.IsNullOrWhiteSpace(requestIdRaw) ? string.Empty : requestIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            EmitJoinRequestDecisionFailure(roomId, requestIdRaw, approve, "?낆옣 ?붿껌 泥섎━ ????붿껌 ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string decisionEndpoint = BuildJoinRequestDecisionEndpoint(
            _joinRequestDecisionEndpointTemplate,
            roomId,
            requestId);

        if (string.IsNullOrWhiteSpace(decisionEndpoint))
        {
            EmitJoinRequestDecisionFailure(roomId, requestId, approve, "?낆옣 ?붿껌 泥섎━ API URL??鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitJoinRequestDecisionFailure(roomId, requestId, approve, "?낆옣 ?붿껌 泥섎━?먮뒗 濡쒓렇???좏겙???꾩슂?⑸땲??");
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
                reviewComment = approve ? "?뱀씤?⑸땲??" : "嫄곗젅?⑸땲??"
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
                "諛??낆옣 ?붿껌 泥섎━???ㅽ뙣?덉뒿?덈떎.");

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
            EmitJoinRequestDecisionFailure(roomId, requestId, approve, $"?낆옣 ?붿껌 泥섎━ 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
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
            EmitMyJoinRequestStatusFetchFailure(requestIdRaw, "?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string requestId = string.IsNullOrWhiteSpace(requestIdRaw) ? string.Empty : requestIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            EmitMyJoinRequestStatusFetchFailure(requestIdRaw, "?낆옣 ?붿껌 ?곹깭 議고쉶 ????붿껌 ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string endpoint = BuildRequestScopedEndpoint(_myJoinRequestStatusEndpointTemplate, requestId);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitMyJoinRequestStatusFetchFailure(requestId, "?낆옣 ?붿껌 ?곹깭 議고쉶 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitMyJoinRequestStatusFetchFailure(requestId, "?낆옣 ?붿껌 ?곹깭 議고쉶?먮뒗 濡쒓렇???좏겙???꾩슂?⑸땲??");
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
            EmitMyJoinRequestStatusFetchFailure(requestId, $"?낆옣 ?붿껌 ?곹깭 議고쉶 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task FetchBlockSharesAsync(
        string roomIdRaw,
        int page,
        int size,
        string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitBlockShareListFetchFailure(roomIdRaw, page, size, "?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitBlockShareListFetchFailure(roomIdRaw, page, size, "釉붾줉 怨듭쑀 紐⑸줉 ???諛?ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        int normalizedPage = page < 1 ? 1 : page;
        int normalizedSize = size < 1 ? 20 : size;

        string baseEndpoint = BuildRoomScopedEndpoint(_blockShareEndpointTemplate, roomId, "block-shares");
        if (string.IsNullOrWhiteSpace(baseEndpoint))
        {
            EmitBlockShareListFetchFailure(roomId, normalizedPage, normalizedSize, "釉붾줉 怨듭쑀 紐⑸줉 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string endpoint = $"{baseEndpoint}?page={normalizedPage}&size={normalizedSize}";
        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitBlockShareListFetchFailure(roomId, normalizedPage, normalizedSize, "釉붾줉 怨듭쑀 紐⑸줉 議고쉶?먮뒗 濡쒓렇???좏겙???꾩슂?⑸땲??");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnBlockShareListFetchStarted?.Invoke(roomId, normalizedPage, normalizedSize);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnBlockShareListFetchCanceled?.Invoke(roomId, normalizedPage, normalizedSize);
                    Log($"Block share list fetch canceled. roomId={roomId}, page={normalizedPage}, size={normalizedSize}");
                    return;
                }

                string responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Log(
                    $"Block share list raw response. result={request.result}, code={request.responseCode}, error={request.error}, body={responseBody}");

                ChatRoomBlockShareListInfo listInfo = ParseBlockShareListResponse(
                    request,
                    roomId,
                    normalizedPage,
                    normalizedSize);

                if (listInfo != null)
                {
                    OnBlockShareListFetchSucceeded?.Invoke(listInfo);
                    Log(
                        $"Block share list fetched. roomId={roomId}, page={listInfo.Page}, size={listInfo.Size}, count={listInfo.Items.Length}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            OnBlockShareListFetchCanceled?.Invoke(roomId, normalizedPage, normalizedSize);
            Log($"Block share list fetch canceled by token. roomId={roomId}, page={normalizedPage}, size={normalizedSize}");
        }
        catch (Exception e)
        {
            EmitBlockShareListFetchFailure(roomId, normalizedPage, normalizedSize, $"釉붾줉 怨듭쑀 紐⑸줉 議고쉶 以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task UploadBlockShareAsync(
        string roomIdRaw,
        int userLevelSeq,
        string messageRaw,
        string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitBlockShareUploadFailure(roomIdRaw, userLevelSeq, "?대? ?ㅻⅨ 梨꾪똿 ?붿껌??泥섎━ 以묒엯?덈떎.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitBlockShareUploadFailure(roomIdRaw, userLevelSeq, "釉붾줉 怨듭쑀 ???諛?ID媛 鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        if (userLevelSeq <= 0)
        {
            EmitBlockShareUploadFailure(roomId, userLevelSeq, "userLevelSeq??1 ?댁긽??媛믪씠?댁빞 ?⑸땲??");
            return;
        }

        string endpoint = BuildRoomScopedEndpoint(_blockShareEndpointTemplate, roomId, "block-shares");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitBlockShareUploadFailure(roomId, userLevelSeq, "釉붾줉 怨듭쑀 API URL??鍮꾩뼱 ?덉뒿?덈떎.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitBlockShareUploadFailure(roomId, userLevelSeq, "釉붾줉 怨듭쑀?먮뒗 濡쒓렇???좏겙???꾩슂?⑸땲??");
            return;
        }

        string message = string.IsNullOrWhiteSpace(messageRaw) ? string.Empty : messageRaw.Trim();

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnBlockShareUploadStarted?.Invoke(roomId, userLevelSeq, message);

            var payload = new BlockShareUploadRequestPayload
            {
                userLevelSeq = userLevelSeq,
                message = message
            };

            string requestJson = JsonUtility.ToJson(payload);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(requestJson);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnBlockShareUploadCanceled?.Invoke(roomId, userLevelSeq);
                    Log($"Block share upload canceled. roomId={roomId}, userLevelSeq={userLevelSeq}");
                    return;
                }

                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                LogBlockShareSavePayloadData(body);
                BasicServiceResponse response = TryParseJson<BasicServiceResponse>(body);
                bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    EmitBlockShareUploadFailure(roomId, userLevelSeq, "?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
                    Log($"Block share upload network failure. roomId={roomId}, userLevelSeq={userLevelSeq}, error={request.error}");
                    return;
                }

                if (!httpSuccess || HasExplicitFailureFlag(body))
                {
                    string errorMessage = FirstNonEmpty(
                        response != null ? response.message : null,
                        response != null ? response.error : null,
                        request.error,
                        $"HTTP {request.responseCode}",
                        "釉붾줉 怨듭쑀 ?낅줈?쒖뿉 ?ㅽ뙣?덉뒿?덈떎.");

                    EmitBlockShareUploadFailure(roomId, userLevelSeq, errorMessage);
                    Log(
                        $"Block share upload failed. roomId={roomId}, userLevelSeq={userLevelSeq}, code={request.responseCode}, body={body}, error={errorMessage}");
                    return;
                }

                var result = new ChatRoomBlockShareUploadInfo
                {
                    RoomId = FirstNonEmpty(
                        response != null ? response.roomId : null,
                        response != null ? response.room_id : null,
                        ExtractJsonScalarAsString(body, "roomId"),
                        ExtractJsonScalarAsString(body, "room_id"),
                        roomId),
                    UserLevelSeq = userLevelSeq,
                    Message = message,
                    BlockShareId = FirstNonEmpty(
                        response != null ? response.blockShareId : null,
                        response != null ? response.block_share_id : null,
                        response != null ? response.id : null,
                        ExtractJsonScalarAsString(body, "blockShareId"),
                        ExtractJsonScalarAsString(body, "block_share_id"),
                        ExtractJsonScalarAsString(body, "id"),
                        string.Empty),
                    CreatedAtUtc = FirstNonEmpty(
                        response != null ? response.createdAt : null,
                        response != null ? response.created_at : null,
                        ExtractJsonScalarAsString(body, "createdAt"),
                        ExtractJsonScalarAsString(body, "created_at"),
                        string.Empty),
                    ResponseCode = request.responseCode,
                    ResponseBody = body
                };

                OnBlockShareUploadSucceeded?.Invoke(result);
                Log(
                    $"Block share upload succeeded. roomId={result.RoomId}, userLevelSeq={result.UserLevelSeq}, blockShareId={result.BlockShareId}, code={result.ResponseCode}");
            }
        }
        catch (OperationCanceledException)
        {
            OnBlockShareUploadCanceled?.Invoke(roomId, userLevelSeq);
            Log($"Block share upload canceled by token. roomId={roomId}, userLevelSeq={userLevelSeq}");
        }
        catch (Exception e)
        {
            EmitBlockShareUploadFailure(roomId, userLevelSeq, $"釉붾줉 怨듭쑀 ?낅줈??以??덉쇅媛 諛쒖깮?덉뒿?덈떎. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task FetchBlockShareDetailAsync(
        string roomIdRaw,
        string shareIdRaw,
        string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitBlockShareDetailFetchFailure(roomIdRaw, shareIdRaw, "??? ??삘뀲 筌?쑵???遺욧퍕??筌ｌ꼶??餓λ쵐???덈뼄.");
            return;
        }

        string roomId = string.IsNullOrWhiteSpace(roomIdRaw) ? string.Empty : roomIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            EmitBlockShareDetailFetchFailure(roomIdRaw, shareIdRaw, "?됰뗀以??⑤벊? ??곸뵠 ??????獄?ID揶쎛 ??쑴堉???됰뮸??덈뼄.");
            return;
        }

        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
        {
            EmitBlockShareDetailFetchFailure(roomId, shareIdRaw, "?됰뗀以??⑤벊? ??곸뵠 ??????shareId揶쎛 ??쑴堉???됰뮸??덈뼄.");
            return;
        }

        string endpoint = BuildRoomShareScopedEndpoint(_blockShareDetailEndpointTemplate, roomId, shareId);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitBlockShareDetailFetchFailure(roomId, shareId, "?됰뗀以??⑤벊? ??곸뵠 API URL????쑴堉???됰뮸??덈뼄.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitBlockShareDetailFetchFailure(roomId, shareId, "?됰뗀以??⑤벊? ??곸뵠?癒?뮉 嚥≪뮄????醫뤾쿃???袁⑹뒄??몃빍??");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnBlockShareDetailFetchStarted?.Invoke(roomId, shareId);
            Log(
                $"Block share detail request. method=GET, endpoint={endpoint}, roomId={roomId}, shareId={shareId}, authAttached={!string.IsNullOrWhiteSpace(accessToken)}");

            using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Accept", "application/json, application/xml, text/xml");
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnBlockShareDetailFetchCanceled?.Invoke(roomId, shareId);
                    Log($"Block share detail fetch canceled. roomId={roomId}, shareId={shareId}");
                    return;
                }

                string responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Log(
                    $"Block share detail raw response. result={request.result}, code={request.responseCode}, error={request.error}, body={responseBody}");
                LogBlockShareDetailPayloadData(responseBody);

                ChatRoomBlockShareInfo detailInfo = ParseBlockShareDetailResponse(request, roomId, shareId);
                if (detailInfo != null)
                {
                    OnBlockShareDetailFetchSucceeded?.Invoke(detailInfo);
                    Log(
                        $"Block share detail fetched. roomId={detailInfo.RoomId}, shareId={detailInfo.BlockShareId}, userLevelSeq={detailInfo.UserLevelSeq}");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            OnBlockShareDetailFetchCanceled?.Invoke(roomId, shareId);
            Log($"Block share detail fetch canceled by token. roomId={roomId}, shareId={shareId}");
        }
        catch (Exception e)
        {
            EmitBlockShareDetailFetchFailure(roomId, shareId, $"?됰뗀以??⑤벊? ??곸뵠 鈺곌퀬??餓???됱뇚揶쎛 獄쏆뮇源??됰뮸??덈뼄. ({e.Message})");
        }
        finally
        {
            IsBusy = false;
            _requestCancellation?.Dispose();
            _requestCancellation = null;
        }
    }

    private async Task SaveBlockShareToMyLevelAsync(string shareIdRaw, string accessTokenOverride)
    {
        if (IsBusy)
        {
            EmitBlockShareSaveFailure(shareIdRaw, "??? ??삘뀲 筌?쑵???遺욧퍕??筌ｌ꼶??餓λ쵐???덈뼄.");
            return;
        }

        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
        {
            EmitBlockShareSaveFailure(shareIdRaw, "?됰뗀以??⑤벊? ???몄퐧??shareId揶쎛 ??쑴堉???됰뮸??덈뼄.");
            return;
        }

        string endpoint = BuildShareScopedEndpoint(
            _saveBlockShareToMyLevelEndpointTemplate,
            shareId,
            "save-to-my-level");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            EmitBlockShareSaveFailure(shareId, "?됰뗀以??⑤벊? ???몄퐧 API URL????쑴堉???됰뮸??덈뼄.");
            return;
        }

        string accessToken = ResolveAccessToken(accessTokenOverride);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            EmitBlockShareSaveFailure(shareId, "?됰뗀以??⑤벊? ???몄퐧?癒?뮉 嚥≪뮄????醫뤾쿃???袁⑹뒄??몃빍??");
            return;
        }

        IsBusy = true;
        _requestCancellation = new CancellationTokenSource();

        try
        {
            OnBlockShareSaveStarted?.Invoke(shareId);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes("{}");
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.Max(1, _requestTimeoutSeconds);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                bool isCanceled = await SendRequestAsync(request, _requestCancellation.Token);
                if (isCanceled)
                {
                    OnBlockShareSaveCanceled?.Invoke(shareId);
                    Log($"Block share save canceled. shareId={shareId}");
                    return;
                }

                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                BasicServiceResponse response = TryParseJson<BasicServiceResponse>(body);
                bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    EmitBlockShareSaveFailure(shareId, "??쎈뱜??곌쾿 ??살첒揶쎛 獄쏆뮇源??됰뮸??덈뼄. ?醫롫뻻 ????쇰뻻 ??뺣즲??雅뚯눘苑??");
                    Log($"Block share save network failure. shareId={shareId}, error={request.error}");
                    return;
                }

                if (!httpSuccess || HasExplicitFailureFlag(body))
                {
                    string errorMessage = FirstNonEmpty(
                        response != null ? response.message : null,
                        response != null ? response.error : null,
                        request.error,
                        $"HTTP {request.responseCode}",
                        "?됰뗀以??⑤벊? ???몄퐧???ㅽ뙣?덉뒿?덈떎.");

                    EmitBlockShareSaveFailure(shareId, errorMessage);
                    Log($"Block share save failed. shareId={shareId}, code={request.responseCode}, body={body}, error={errorMessage}");
                    return;
                }

                var result = new ChatRoomBlockShareSaveInfo
                {
                    ShareId = FirstNonEmpty(
                        ExtractJsonScalarAsString(body, "shareId"),
                        ExtractJsonScalarAsString(body, "blockShareId"),
                        ExtractJsonScalarAsString(body, "block_share_id"),
                        ExtractJsonScalarAsString(body, "id"),
                        shareId),
                    SavedUserLevelSeq = FirstPositive(
                        ParseJsonInt(body, "userLevelSeq"),
                        ParseJsonInt(body, "seq"),
                        ParseJsonInt(body, "level"),
                        0),
                    Message = FirstNonEmpty(
                        response != null ? response.message : null,
                        response != null ? response.error : null,
                        string.Empty),
                    ResponseCode = request.responseCode,
                    ResponseBody = body
                };

                OnBlockShareSaveSucceeded?.Invoke(result);
                Log($"Block share saved to my level. shareId={result.ShareId}, savedSeq={result.SavedUserLevelSeq}, code={result.ResponseCode}");
            }
        }
        catch (OperationCanceledException)
        {
            OnBlockShareSaveCanceled?.Invoke(shareId);
            Log($"Block share save canceled by token. shareId={shareId}");
        }
        catch (Exception e)
        {
            EmitBlockShareSaveFailure(shareId, $"?됰뗀以??⑤벊? ???몄퐧 鈺곌퀬??餓???됱뇚揶쎛 獄쏆뮇源??됰뮸??덈뼄. ({e.Message})");
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
                ErrorMessage = "?낆옣 ?붿껌 泥섎━ API URL??鍮꾩뼱 ?덉뒿?덈떎."
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
                    ErrorMessage = "?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??"
                };
            }

            if (!httpSuccess || HasExplicitFailureFlag(body))
            {
                string errorMessage = FirstNonEmpty(
                    response != null ? response.message : null,
                    response != null ? response.error : null,
                    request.error,
                    $"HTTP {request.responseCode}",
                    "諛??낆옣 ?붿껌 泥섎━???ㅽ뙣?덉뒿?덈떎.");

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
            EmitFailure("?붿껌 媛앹껜媛 ?놁뒿?덈떎.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitFailure("?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
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
                "梨꾪똿諛??앹꽦???ㅽ뙣?덉뒿?덈떎.");

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
            EmitFailure("梨꾪똿諛?ID媛 ?묐떟???놁뒿?덈떎.");
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
            EmitListFailure("?붿껌 媛앹껜媛 ?놁뒿?덈떎.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitListFailure("?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
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
                "梨꾪똿諛?紐⑸줉 議고쉶???ㅽ뙣?덉뒿?덈떎.");

            EmitListFailure(errorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<ChatRoomSummaryInfo>();

        ChatRoomPayload[] payloads = ExtractRoomPayloads(body);
        if (payloads == null)
        {
            EmitListFailure("梨꾪똿諛?紐⑸줉 ?묐떟???댁꽍?????놁뒿?덈떎.");
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
            EmitJoinRequestFailure("?붿껌 媛앹껜媛 ?놁뒿?덈떎.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitJoinRequestFailure("?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
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
                "諛??낆옣 ?붿껌???ㅽ뙣?덉뒿?덈떎.");

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
            EmitMyJoinRequestStatusFetchFailure(requestedRequestId, "?붿껌 媛앹껜媛 ?놁뒿?덈떎.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitMyJoinRequestStatusFetchFailure(requestedRequestId, "?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
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
                "?낆옣 ?붿껌 ?곹깭 議고쉶???ㅽ뙣?덉뒿?덈떎.");

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
            EmitJoinRequestsFetchFailure("?붿껌 媛앹껜媛 ?놁뒿?덈떎.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitJoinRequestsFetchFailure("?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
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
                "諛??낆옣 ?붿껌 紐⑸줉 議고쉶???ㅽ뙣?덉뒿?덈떎.");

            EmitJoinRequestsFetchFailure(errorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<ChatRoomJoinRequestInfo>();

        JoinRequestPayload[] payloads = ExtractJoinRequestPayloads(body);
        if (payloads == null)
        {
            EmitJoinRequestsFetchFailure("諛??낆옣 ?붿껌 紐⑸줉 ?묐떟???댁꽍?????놁뒿?덈떎.");
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

    private ChatRoomBlockShareListInfo ParseBlockShareListResponse(
        UnityWebRequest request,
        string roomId,
        int requestedPage,
        int requestedSize)
    {
        if (request == null)
        {
            EmitBlockShareListFetchFailure(roomId, requestedPage, requestedSize, "?붿껌 媛앹껜媛 ?놁뒿?덈떎.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitBlockShareListFetchFailure(roomId, requestedPage, requestedSize, "?ㅽ듃?뚰겕 ?ㅻ쪟媛 諛쒖깮?덉뒿?덈떎. ?좎떆 ???ㅼ떆 ?쒕룄??二쇱꽭??");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        BasicServiceResponse response = TryParseJson<BasicServiceResponse>(body);
        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

        if (!httpSuccess || HasExplicitFailureFlag(body))
        {
            string errorMessage = FirstNonEmpty(
                response != null ? response.message : null,
                response != null ? response.error : null,
                $"HTTP {request.responseCode}",
                "釉붾줉 怨듭쑀 紐⑸줉 議고쉶???ㅽ뙣?덉뒿?덈떎.");

            EmitBlockShareListFetchFailure(roomId, requestedPage, requestedSize, errorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChatRoomBlockShareListInfo
            {
                RoomId = roomId,
                Page = requestedPage,
                Size = requestedSize,
                TotalCount = 0,
                TotalPages = 0,
                Items = Array.Empty<ChatRoomBlockShareInfo>(),
                ResponseCode = request.responseCode,
                ResponseBody = body
            };
        }

        BlockSharePayload[] payloads = ExtractBlockSharePayloads(body);
        if (payloads == null)
        {
            Log(
                $"Block share list parse failed. roomId={roomId}, page={requestedPage}, size={requestedSize}, body={TruncateForLog(body)}");
            EmitBlockShareListFetchFailure(roomId, requestedPage, requestedSize, "釉붾줉 怨듭쑀 紐⑸줉 ?묐떟???댁꽍?????놁뒿?덈떎.");
            return null;
        }

        var items = new List<ChatRoomBlockShareInfo>(payloads.Length);
        for (int i = 0; i < payloads.Length; i++)
        {
            ChatRoomBlockShareInfo info = ToBlockShareInfo(payloads[i], roomId);
            if (info != null)
                items.Add(info);
        }

        int page = FirstPositive(
            ParseJsonInt(body, "page"),
            ParseJsonInt(body, "currentPage"),
            requestedPage);

        int size = FirstPositive(
            ParseJsonInt(body, "size"),
            ParseJsonInt(body, "pageSize"),
            requestedSize);

        int totalCount = FirstPositive(
            ParseJsonInt(body, "totalCount"),
            ParseJsonInt(body, "totalElements"),
            ParseJsonInt(body, "total"),
            items.Count);

        int totalPages = FirstPositive(
            ParseJsonInt(body, "totalPages"),
            ParseJsonInt(body, "pageCount"),
            totalCount > 0 && size > 0 ? Mathf.CeilToInt(totalCount / (float)size) : 0);

        return new ChatRoomBlockShareListInfo
        {
            RoomId = roomId,
            Page = page,
            Size = size,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items.ToArray(),
            ResponseCode = request.responseCode,
            ResponseBody = body
        };
    }

    private ChatRoomBlockShareInfo ParseBlockShareDetailResponse(
        UnityWebRequest request,
        string roomId,
        string requestedShareId)
    {
        if (request == null)
        {
            EmitBlockShareDetailFetchFailure(roomId, requestedShareId, "?遺욧퍕 揶쏆빘猿쒎첎? ??곷뮸??덈뼄.");
            return null;
        }

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            EmitBlockShareDetailFetchFailure(roomId, requestedShareId, "??쎈뱜??곌쾿 ??살첒揶쎛 獄쏆뮇源??됰뮸??덈뼄. ?醫롫뻻 ????쇰뻻 ??뺣즲??雅뚯눘苑??");
            return null;
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        BasicServiceResponse response = TryParseJson<BasicServiceResponse>(body);
        bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;

        if (!httpSuccess || HasExplicitFailureFlag(body))
        {
            string errorMessage = FirstNonEmpty(
                response != null ? response.message : null,
                response != null ? response.error : null,
                $"HTTP {request.responseCode}",
                "?됰뗀以??⑤벊? ??곸뵠???ㅽ뙣?덉뒿?덈떎.");

            EmitBlockShareDetailFetchFailure(roomId, requestedShareId, errorMessage);
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChatRoomBlockShareInfo
            {
                BlockShareId = requestedShareId,
                RoomId = roomId,
                UserId = string.Empty,
                UserLevelSeq = 0,
                Message = string.Empty,
                CreatedAtUtc = string.Empty
            };
        }

        BlockShareSingleResponse singleResponse = TryParseJson<BlockShareSingleResponse>(body);
        BlockSharePayload payload = FirstNonEmptyBlockSharePayload(
            singleResponse != null ? singleResponse.blockShare : null,
            singleResponse != null ? singleResponse.share : null,
            singleResponse != null ? singleResponse.item : null,
            singleResponse != null ? singleResponse.data : null);

        if (payload == null)
        {
            BlockSharePayload[] payloads = ExtractBlockSharePayloads(body);
            if (payloads != null && payloads.Length > 0)
            {
                for (int i = 0; i < payloads.Length; i++)
                {
                    if (IsMeaningfulBlockSharePayload(payloads[i]))
                    {
                        payload = payloads[i];
                        break;
                    }
                }

                if (payload == null)
                    payload = payloads[0];
            }
        }

        ChatRoomBlockShareInfo info = ToBlockShareInfo(payload, roomId);
        if (info == null)
        {
            info = new ChatRoomBlockShareInfo
            {
                BlockShareId = FirstNonEmpty(
                    ExtractJsonScalarAsString(body, "shareId"),
                    ExtractJsonScalarAsString(body, "blockShareId"),
                    ExtractJsonScalarAsString(body, "block_share_id"),
                    ExtractJsonScalarAsString(body, "id"),
                    requestedShareId),
                RoomId = FirstNonEmpty(
                    ExtractJsonScalarAsString(body, "roomId"),
                    ExtractJsonScalarAsString(body, "room_id"),
                    roomId),
                UserId = FirstNonEmpty(
                    ExtractJsonScalarAsString(body, "userId"),
                    ExtractJsonScalarAsString(body, "user_id"),
                    ExtractJsonScalarAsString(body, "senderUserId"),
                    ExtractJsonScalarAsString(body, "sender_user_id"),
                    ExtractJsonScalarAsString(body, "senderUserName"),
                    ExtractJsonScalarAsString(body, "sender_user_name"),
                    ExtractJsonScalarAsString(body, "requesterUserId"),
                    ExtractJsonScalarAsString(body, "requester_user_id"),
                    string.Empty),
                UserLevelSeq = FirstPositive(
                    ParseJsonInt(body, "userLevelSeq"),
                    ParseJsonInt(body, "user_level_seq"),
                    ParseJsonInt(body, "sourceUserLevelSeq"),
                    ParseJsonInt(body, "source_user_level_seq"),
                    ParseJsonInt(body, "level"),
                    0),
                Message = FirstNonEmpty(ExtractJsonScalarAsString(body, "message"), string.Empty),
                CreatedAtUtc = FirstNonEmpty(
                    ExtractJsonScalarAsString(body, "createdAt"),
                    ExtractJsonScalarAsString(body, "created_at"),
                    string.Empty)
            };
        }

        if (string.IsNullOrWhiteSpace(info.BlockShareId))
            info.BlockShareId = requestedShareId;

        if (string.IsNullOrWhiteSpace(info.RoomId))
            info.RoomId = roomId;

        if (string.IsNullOrWhiteSpace(info.BlockShareId))
        {
            Log(
                $"Block share detail parse failed. roomId={roomId}, shareId={requestedShareId}, body={TruncateForLog(body)}");
            EmitBlockShareDetailFetchFailure(roomId, requestedShareId, "?됰뗀以??⑤벊? ??곸뵠 ?묐떟???댁꽍?????놁뒿?덈떎.");
            return null;
        }

        return info;
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

    private static BlockSharePayload[] ExtractBlockSharePayloads(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<BlockSharePayload>();

        string trimmedBody = body.Trim();

        if (trimmedBody.StartsWith("[", StringComparison.Ordinal))
        {
            return ParseBlockSharePayloadArray(trimmedBody);
        }

        BlockShareListResponse listResponse = TryParseJson<BlockShareListResponse>(trimmedBody);
        if (listResponse != null)
        {
            if (listResponse.shares != null)
                return listResponse.shares;
            if (listResponse.blockShares != null)
                return listResponse.blockShares;
            if (listResponse.list != null)
                return listResponse.list;
            if (listResponse.items != null)
                return listResponse.items;
            if (listResponse.content != null)
                return listResponse.content;
            if (listResponse.data != null)
                return listResponse.data;
        }

        BlockShareDataObjectResponse dataObjectResponse = TryParseJson<BlockShareDataObjectResponse>(trimmedBody);
        if (dataObjectResponse != null && dataObjectResponse.data != null)
        {
            if (dataObjectResponse.data.shares != null)
                return dataObjectResponse.data.shares;
            if (dataObjectResponse.data.blockShares != null)
                return dataObjectResponse.data.blockShares;
            if (dataObjectResponse.data.list != null)
                return dataObjectResponse.data.list;
            if (dataObjectResponse.data.items != null)
                return dataObjectResponse.data.items;
            if (dataObjectResponse.data.content != null)
                return dataObjectResponse.data.content;
        }

        string[] candidateArrayKeys =
        {
            "shares",
            "blockShares",
            "list",
            "items",
            "content",
            "data",
            "records",
            "rows",
            "result"
        };

        BlockSharePayload[] emptyCandidate = null;

        for (int i = 0; i < candidateArrayKeys.Length; i++)
        {
            string arrayJson = ExtractJsonArrayByKey(trimmedBody, candidateArrayKeys[i]);
            BlockSharePayload[] payloads = ParseBlockSharePayloadArray(arrayJson);
            if (payloads == null)
                continue;

            if (payloads.Length == 0)
            {
                if (emptyCandidate == null)
                    emptyCandidate = payloads;

                continue;
            }

            if (ContainsMeaningfulBlockSharePayload(payloads))
                return payloads;

            if (emptyCandidate == null)
                emptyCandidate = payloads;
        }

        BlockSharePayload[] firstArrayPayloads = ParseBlockSharePayloadArray(ExtractFirstJsonArray(trimmedBody));
        if (firstArrayPayloads != null)
        {
            if (firstArrayPayloads.Length == 0 || ContainsMeaningfulBlockSharePayload(firstArrayPayloads))
                return firstArrayPayloads;

            if (emptyCandidate == null)
                emptyCandidate = firstArrayPayloads;
        }

        return emptyCandidate;
    }

    private static BlockSharePayload FirstNonEmptyBlockSharePayload(params BlockSharePayload[] payloads)
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

    private static BlockSharePayload[] ParseBlockSharePayloadArray(string arrayJson)
    {
        if (string.IsNullOrWhiteSpace(arrayJson))
            return null;

        string trimmedArray = arrayJson.Trim();
        if (!trimmedArray.StartsWith("[", StringComparison.Ordinal))
            return null;

        string wrapped = $"{{\"items\":{trimmedArray}}}";
        BlockShareArrayWrapper wrappedResponse = TryParseJson<BlockShareArrayWrapper>(wrapped);
        return wrappedResponse != null ? wrappedResponse.items : null;
    }

    private static bool ContainsMeaningfulBlockSharePayload(BlockSharePayload[] payloads)
    {
        if (payloads == null || payloads.Length == 0)
            return false;

        for (int i = 0; i < payloads.Length; i++)
        {
            if (IsMeaningfulBlockSharePayload(payloads[i]))
                return true;
        }

        return false;
    }

    private static bool IsMeaningfulBlockSharePayload(BlockSharePayload payload)
    {
        if (payload == null)
            return false;

        return !string.IsNullOrWhiteSpace(FirstNonEmpty(
                   payload.blockShareId,
                   payload.block_share_id,
                   payload.id,
                   payload.shareId > 0 ? payload.shareId.ToString() : null,
                   payload.share_id > 0 ? payload.share_id.ToString() : null)) ||
               !string.IsNullOrWhiteSpace(FirstNonEmpty(payload.roomId, payload.room_id)) ||
               !string.IsNullOrWhiteSpace(FirstNonEmpty(
                   payload.userId,
                   payload.user_id,
                   payload.senderUserId,
                   payload.sender_user_id,
                   payload.requesterUserId,
                   payload.requester_user_id,
                   payload.senderUserName,
                   payload.sender_user_name)) ||
               FirstPositive(
                   payload.userLevelSeq,
                   payload.user_level_seq,
                   payload.sourceUserLevelSeq,
                   payload.source_user_level_seq,
                   payload.level,
                   0) > 0 ||
               !string.IsNullOrWhiteSpace(payload.message) ||
               !string.IsNullOrWhiteSpace(FirstNonEmpty(payload.createdAt, payload.created_at));
    }

    private static string ExtractJsonArrayByKey(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            return null;

        string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*\\[";
        Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        int arrayStart = match.Index + match.Length - 1;
        int arrayEnd = FindMatchingJsonBracket(json, arrayStart, '[', ']');
        if (arrayEnd < 0 || arrayEnd < arrayStart)
            return null;

        return json.Substring(arrayStart, arrayEnd - arrayStart + 1);
    }

    private static string ExtractFirstJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c != '[')
                continue;

            int endIndex = FindMatchingJsonBracket(json, i, '[', ']');
            if (endIndex < i)
                return null;

            return json.Substring(i, endIndex - i + 1);
        }

        return null;
    }

    private static int FindMatchingJsonBracket(string json, int startIndex, char openChar, char closeChar)
    {
        if (string.IsNullOrWhiteSpace(json))
            return -1;

        if (startIndex < 0 || startIndex >= json.Length || json[startIndex] != openChar)
            return -1;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startIndex; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == openChar)
            {
                depth++;
                continue;
            }

            if (c != closeChar)
                continue;

            depth--;
            if (depth == 0)
                return i;

            if (depth < 0)
                return -1;
        }

        return -1;
    }

    private static ChatRoomBlockShareInfo ToBlockShareInfo(BlockSharePayload payload, string fallbackRoomId)
    {
        if (payload == null)
            return null;

        string blockShareId = FirstNonEmpty(
            payload.blockShareId,
            payload.block_share_id,
            payload.id,
            payload.shareId > 0 ? payload.shareId.ToString() : null,
            payload.share_id > 0 ? payload.share_id.ToString() : null);
        string roomId = FirstNonEmpty(payload.roomId, payload.room_id, fallbackRoomId);
        string userId = FirstNonEmpty(
            payload.userId,
            payload.user_id,
            payload.senderUserId,
            payload.sender_user_id,
            payload.requesterUserId,
            payload.requester_user_id,
            payload.senderUserName,
            payload.sender_user_name,
            string.Empty);
        int userLevelSeq = FirstPositive(
            payload.userLevelSeq,
            payload.user_level_seq,
            payload.sourceUserLevelSeq,
            payload.source_user_level_seq,
            payload.level,
            0);
        string message = FirstNonEmpty(payload.message, string.Empty);
        string createdAt = FirstNonEmpty(payload.createdAt, payload.created_at, string.Empty);

        if (string.IsNullOrWhiteSpace(blockShareId) &&
            string.IsNullOrWhiteSpace(roomId) &&
            string.IsNullOrWhiteSpace(userId) &&
            userLevelSeq <= 0 &&
            string.IsNullOrWhiteSpace(message) &&
            string.IsNullOrWhiteSpace(createdAt))
        {
            return null;
        }

        return new ChatRoomBlockShareInfo
        {
            BlockShareId = blockShareId,
            RoomId = roomId,
            UserId = userId,
            UserLevelSeq = userLevelSeq,
            Message = message,
            CreatedAtUtc = createdAt
        };
    }

    private static int ParseJsonInt(string json, string key)
    {
        string raw = ExtractJsonScalarAsString(json, key);
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        if (int.TryParse(raw.Trim(), out int value))
            return value;

        return 0;
    }

    private void LogBlockShareDetailPayloadData(string responseBody)
    {
        if (!_debugLog)
            return;

        string jsonData = TryExtractBlockShareDetailJson(responseBody);
        string xmlData = TryExtractBlockShareDetailXml(responseBody);

        LogGreen($"Block share detail JSON data:\n{FormatPayloadLog(jsonData)}");
        LogGreen($"Block share detail XML data:\n{FormatPayloadLog(xmlData)}");
    }

    private void LogBlockShareSavePayloadData(string responseBody)
    {
        if (!_debugLog)
            return;

        string jsonData = TryExtractBlockShareDetailJson(responseBody);
        string xmlData = TryExtractBlockShareDetailXml(responseBody);

        LogGreen($"Block share save JSON data:\n{FormatPayloadLog(jsonData)}");
        LogGreen($"Block share save XML data:\n{FormatPayloadLog(xmlData)}");
    }

    private static string TryExtractBlockShareDetailJson(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        string trimmed = responseBody.Trim();
        if (LooksLikeJson(trimmed))
            return trimmed;

        string jsonFromField = ExtractFirstJsonScalarByKeys(trimmed, _detailJsonCandidateKeys);
        if (LooksLikeJson(jsonFromField))
            return jsonFromField.Trim();

        return null;
    }

    private static string TryExtractBlockShareDetailXml(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        string trimmed = responseBody.Trim();
        if (LooksLikeXml(trimmed))
            return trimmed;

        string xmlFromField = ExtractFirstJsonScalarByKeys(trimmed, _detailXmlCandidateKeys);
        if (LooksLikeXml(xmlFromField))
            return xmlFromField.Trim();

        string xmlFromRawText = TryFindXmlFragment(trimmed);
        if (LooksLikeXml(xmlFromRawText))
            return xmlFromRawText.Trim();

        return null;
    }

    private static string ExtractFirstJsonScalarByKeys(string json, string[] candidateKeys)
    {
        if (string.IsNullOrWhiteSpace(json) || candidateKeys == null || candidateKeys.Length == 0)
            return null;

        for (int i = 0; i < candidateKeys.Length; i++)
        {
            string key = candidateKeys[i];
            if (string.IsNullOrWhiteSpace(key))
                continue;

            string value = ExtractJsonScalarAsString(json, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static bool LooksLikeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        return (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
               (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal));
    }

    private static bool LooksLikeXml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            return true;

        return Regex.IsMatch(
            trimmed,
            @"^<(?<tag>[A-Za-z_][\w\-\.:]*)\b[^>]*>[\s\S]*</\k<tag>>$",
            RegexOptions.Singleline);
    }

    private static string TryFindXmlFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        Match fragment = Regex.Match(
            value,
            @"<(?<tag>[A-Za-z_][\w\-\.:]*)\b[^>]*>[\s\S]*?</\k<tag>>",
            RegexOptions.Singleline);

        if (fragment.Success)
            return fragment.Value;

        return null;
    }

    private static string FormatPayloadLog(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return "(not found)";

        return TruncateForLog(payload.Trim(), 4000);
    }

    private void LogGreen(string message)
    {
        if (!_debugLog)
            return;

        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        Debug.Log($"<color=#00FF66>[ChatRoomManager] {text}</color>");
    }

    private static string TruncateForLog(string value, int maxLength = 1200)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        int safeMax = Mathf.Max(32, maxLength);
        if (value.Length <= safeMax)
            return value;

        return $"{value.Substring(0, safeMax)}...(truncated, len={value.Length})";
    }

    private static int FirstPositive(params int[] values)
    {
        if (values == null)
            return 0;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > 0)
                return values[i];
        }

        return 0;
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

    private static string BuildRoomShareScopedEndpoint(string endpointTemplate, string roomId, string shareId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(shareId))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(endpointTemplate))
            return string.Empty;

        string encodedRoomId = UnityWebRequest.EscapeURL(roomId.Trim());
        string encodedShareId = UnityWebRequest.EscapeURL(shareId.Trim());
        string resolved = endpointTemplate.Trim();

        if (resolved.IndexOf("{roomId}", StringComparison.Ordinal) >= 0)
            resolved = resolved.Replace("{roomId}", encodedRoomId);
        else
            resolved = $"{resolved.TrimEnd('/')}/{encodedRoomId}";

        if (resolved.IndexOf("{shareId}", StringComparison.Ordinal) >= 0)
            resolved = resolved.Replace("{shareId}", encodedShareId);
        else
            resolved = $"{resolved.TrimEnd('/')}/{encodedShareId}";

        return resolved;
    }

    private static string BuildShareScopedEndpoint(
        string endpointTemplate,
        string shareId,
        string fallbackSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(shareId))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(endpointTemplate))
            return string.Empty;

        string encodedShareId = UnityWebRequest.EscapeURL(shareId.Trim());
        string resolved = endpointTemplate.Trim();

        if (resolved.IndexOf("{shareId}", StringComparison.Ordinal) >= 0)
        {
            resolved = resolved.Replace("{shareId}", encodedShareId);
        }
        else
        {
            resolved = $"{resolved.TrimEnd('/')}/{encodedShareId}";
        }

        if (string.IsNullOrWhiteSpace(fallbackSuffix))
            return resolved;

        string suffix = fallbackSuffix.StartsWith("/", StringComparison.Ordinal)
            ? fallbackSuffix
            : $"/{fallbackSuffix}";

        if (resolved.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return resolved;

        return $"{resolved.TrimEnd('/')}{suffix}";
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
            ? "梨꾪똿諛??앹꽦???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnCreateFailed?.Invoke(message);
        Log($"Chat room create failed: {message}");
    }

    private void EmitListFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "梨꾪똿諛?紐⑸줉 議고쉶???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnListFailed?.Invoke(message);
        Log($"Chat room list fetch failed: {message}");
    }

    private void EmitJoinRequestFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "諛??낆옣 ?붿껌???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnJoinRequestFailed?.Invoke(message);
        Log($"Join request failed: {message}");
    }

    private void EmitJoinRequestsFetchFailure(string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "諛??낆옣 ?붿껌 紐⑸줉 議고쉶???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnJoinRequestsFetchFailed?.Invoke(message);
        Log($"Join request list fetch failed: {message}");
    }

    private void EmitJoinRequestDecisionFailure(string roomId, string requestId, bool approve, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "諛??낆옣 ?붿껌 泥섎━???ㅽ뙣?덉뒿?덈떎."
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
            ? "?낆옣 ?붿껌 ?곹깭 議고쉶???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnMyJoinRequestStatusFetchFailed?.Invoke(requestId ?? string.Empty, message);
        Log($"My join request status fetch failed: requestId={requestId}, message={message}");
    }

    private void EmitBlockShareListFetchFailure(string roomId, int page, int size, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "釉붾줉 怨듭쑀 紐⑸줉 議고쉶???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnBlockShareListFetchFailed?.Invoke(roomId ?? string.Empty, page, size, message);
        Log($"Block share list fetch failed: roomId={roomId}, page={page}, size={size}, message={message}");
    }

    private void EmitBlockShareUploadFailure(string roomId, int userLevelSeq, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "釉붾줉 怨듭쑀 ?낅줈?쒖뿉 ?ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnBlockShareUploadFailed?.Invoke(roomId ?? string.Empty, userLevelSeq, message);
        Log($"Block share upload failed: roomId={roomId}, userLevelSeq={userLevelSeq}, message={message}");
    }

    private void EmitBlockShareDetailFetchFailure(string roomId, string shareId, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "釉붾줉 怨듭쑀 ?곸꽭 議고쉶???ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnBlockShareDetailFetchFailed?.Invoke(roomId ?? string.Empty, shareId ?? string.Empty, message);
        Log($"Block share detail fetch failed: roomId={roomId}, shareId={shareId}, message={message}");
    }

    private void EmitBlockShareSaveFailure(string shareId, string userMessage)
    {
        string message = string.IsNullOrWhiteSpace(userMessage)
            ? "釉붾줉 怨듭쑀 ?곗씠?곕? ??ν빀?섎뒗 ?곹깭濡??ㅽ뙣?덉뒿?덈떎."
            : userMessage;

        OnBlockShareSaveFailed?.Invoke(shareId ?? string.Empty, message);
        Log($"Block share save failed: shareId={shareId}, message={message}");
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

    [Serializable]
    private class BlockShareUploadRequestPayload
    {
        public int userLevelSeq;
        public string message;
    }

    [Serializable]
    private class BasicServiceResponse
    {
        public bool success;
        public bool isSuccess;
        public string message;
        public string error;
        public string id;
        public string roomId;
        public string room_id;
        public string blockShareId;
        public string block_share_id;
        public string createdAt;
        public string created_at;
    }

    [Serializable]
    private class BlockSharePayload
    {
        public int shareId;
        public int share_id;
        public string blockShareId;
        public string block_share_id;
        public string id;
        public string roomId;
        public string room_id;
        public string userId;
        public string user_id;
        public string senderUserId;
        public string sender_user_id;
        public string senderUserName;
        public string sender_user_name;
        public string requesterUserId;
        public string requester_user_id;
        public int userLevelSeq;
        public int user_level_seq;
        public int sourceUserLevelSeq;
        public int source_user_level_seq;
        public int level;
        public string message;
        public string createdAt;
        public string created_at;
    }

    [Serializable]
    private class BlockShareArrayWrapper
    {
        public BlockSharePayload[] items;
    }

    [Serializable]
    private class BlockShareListResponse
    {
        public BlockSharePayload[] shares;
        public BlockSharePayload[] blockShares;
        public BlockSharePayload[] list;
        public BlockSharePayload[] items;
        public BlockSharePayload[] content;
        public BlockSharePayload[] data;
    }

    [Serializable]
    private class BlockShareDataObjectResponse
    {
        public BlockShareDataPayload data;
    }

    [Serializable]
    private class BlockShareDataPayload
    {
        public BlockSharePayload[] shares;
        public BlockSharePayload[] blockShares;
        public BlockSharePayload[] list;
        public BlockSharePayload[] items;
        public BlockSharePayload[] content;
    }

    [Serializable]
    private class BlockShareSingleResponse
    {
        public BlockSharePayload data;
        public BlockSharePayload item;
        public BlockSharePayload blockShare;
        public BlockSharePayload share;
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

[Serializable]
public sealed class ChatRoomBlockShareUploadInfo
{
    public string RoomId;
    public int UserLevelSeq;
    public string Message;
    public string BlockShareId;
    public string CreatedAtUtc;
    public long ResponseCode;
    public string ResponseBody;
}

[Serializable]
public sealed class ChatRoomBlockShareInfo
{
    public string BlockShareId;
    public string RoomId;
    public string UserId;
    public int UserLevelSeq;
    public string Message;
    public string CreatedAtUtc;
}

[Serializable]
public sealed class ChatRoomBlockShareListInfo
{
    public string RoomId;
    public int Page;
    public int Size;
    public int TotalCount;
    public int TotalPages;
    public ChatRoomBlockShareInfo[] Items;
    public long ResponseCode;
    public string ResponseBody;
}

[Serializable]
public sealed class ChatRoomBlockShareSaveInfo
{
    public string ShareId;
    public int SavedUserLevelSeq;
    public string Message;
    public long ResponseCode;
    public string ResponseBody;
}

