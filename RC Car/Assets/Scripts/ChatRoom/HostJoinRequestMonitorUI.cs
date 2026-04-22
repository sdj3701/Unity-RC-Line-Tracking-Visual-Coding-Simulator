using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Auth;
using Fusion;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HostJoinRequestMonitorUI : MonoBehaviour
{
    [Header("Main Panel")]
    [SerializeField] private GameObject _mainPanel;
    [SerializeField] private bool _hidePanelWhenNotHost = false;

    [Header("Legacy Replacement")]
    [SerializeField] private bool _disableLegacyGuiMonitorOnEnable = true;

    [Header("Text")]
    [SerializeField] private TMP_Text _currentMemberCountText;
    [SerializeField] private TMP_Text _pendingRequestCountText;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private string _currentMemberCountFormat = "{0}";
    [SerializeField] private string _pendingRequestCountFormat = "{0}";

    [Header("List UI")]
    [SerializeField] private ScrollRect _requestScrollRect;
    [SerializeField] private Transform _requestListContent;
    [SerializeField] private HostJoinRequestItemUI _requestItemPrefab;
    [SerializeField] private GameObject _emptyStateObject;
    [SerializeField] private bool _hideItemTemplateOnAwake = true;

    [Header("Buttons")]
    [SerializeField] private Button _refreshButton;

    [Header("Polling")]
    [SerializeField] private bool _autoStartPolling = true;
    [SerializeField] private bool _fetchOnEnable = true;
    [SerializeField] private float _pollIntervalSeconds = 5f;
    [SerializeField] private bool _hostOnly = true;
    [SerializeField] private bool _createChatRoomManagerIfMissing = true;

    [Header("Request Target")]
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private string _accessTokenOverride = string.Empty;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private const float MinPollIntervalSeconds = 0.5f;

    private readonly List<FusionPendingJoinRequestInfo> _latestRequests = new List<FusionPendingJoinRequestInfo>();
    private readonly List<HostJoinRequestItemUI> _spawnedItems = new List<HostJoinRequestItemUI>();

    private FusionConnectionManager _connectionManager;
    private ChatRoomManager _chatRoomManager;
    private Coroutine _pollingCoroutine;
    private bool _isBound;
    private bool _isChatRoomBound;
    private int _currentPlayerCount;
    private int _currentMaxPlayers;
    private TaskCompletionSource<ChatRoomJoinRequestInfo[]> _joinRequestsFetchTcs;
    private TaskCompletionSource<ChatRoomJoinRequestDecisionInfo> _joinRequestDecisionTcs;

    private void Awake()
    {
        TryResolveListContent();

        if (_hideItemTemplateOnAwake && _requestItemPrefab != null)
            _requestItemPrefab.gameObject.SetActive(false);

        RefreshAllUi();
    }

    private void OnEnable()
    {
        BindButtons();
        TryEnsureAndBindConnectionManager();
        DisableLegacyGuiMonitorIfNeeded();
        RefreshHostVisibility();
        RefreshFromFusionState();

        if (_fetchOnEnable)
            FetchNow();

        if (_autoStartPolling)
            StartPolling();

        if (_disableLegacyGuiMonitorOnEnable)
            StartCoroutine(DisableLegacyGuiMonitorNextFrame());
    }

    private void OnDisable()
    {
        StopPolling();
        UnbindButtons();
        UnbindConnectionManager();
        UnbindChatRoomManager();
    }

    public void StartPolling()
    {
        if (_pollingCoroutine != null)
            return;

        _pollingCoroutine = StartCoroutine(PollFusionStateLoop());
        SetStatus("Photon join request monitor started.");
    }

    public void StopPolling()
    {
        if (_pollingCoroutine == null)
            return;

        StopCoroutine(_pollingCoroutine);
        _pollingCoroutine = null;
        SetStatus("Photon join request monitor stopped.");
    }

    public void FetchNow()
    {
        RefreshFromFusionState();
    }

    private IEnumerator PollFusionStateLoop()
    {
        while (enabled)
        {
            float interval = Mathf.Max(MinPollIntervalSeconds, _pollIntervalSeconds);
            yield return new WaitForSeconds(interval);

            if (IsPanelVisible())
                RefreshFromFusionState();
        }

        _pollingCoroutine = null;
    }

    private bool TryEnsureAndBindConnectionManager()
    {
        FusionConnectionManager manager = FusionConnectionManager.GetOrCreate();
        if (_connectionManager == manager && _isBound)
            return true;

        UnbindConnectionManager();
        _connectionManager = manager;

        if (_connectionManager == null)
            return false;

        _connectionManager.OnPendingJoinRequestsChanged += HandlePendingJoinRequestsChanged;
        _connectionManager.OnPlayerCountChanged += HandlePlayerCountChanged;
        _connectionManager.OnStatusChanged += HandleStatusChanged;
        _isBound = true;
        return true;
    }

    private void UnbindConnectionManager()
    {
        if (!_isBound || _connectionManager == null)
            return;

        _connectionManager.OnPendingJoinRequestsChanged -= HandlePendingJoinRequestsChanged;
        _connectionManager.OnPlayerCountChanged -= HandlePlayerCountChanged;
        _connectionManager.OnStatusChanged -= HandleStatusChanged;
        _connectionManager = null;
        _isBound = false;
    }

    private void RefreshFromFusionState()
    {
        RefreshHostVisibility();

        if (_hostOnly && !CanCurrentUserManageHostRequests(out string reason))
        {
            SetStatus($"Host check failed. reason={reason}");
            RefreshInteractableState();
            return;
        }

        if (!TryEnsureAndBindConnectionManager())
        {
            SetStatus("FusionConnectionManager is missing.");
            RefreshInteractableState();
            return;
        }

        NetworkRunner runner = _connectionManager.Runner;
        if (runner != null && runner.IsRunning && !runner.IsShutdown)
        {
            FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
            int fallbackPlayerCount = context != null ? context.PlayerCount : (runner.IsServer ? 1 : 0);
            int fallbackMaxPlayers = context != null ? context.MaxPlayers : 0;
            _currentPlayerCount = FusionPlayerCountUtility.ResolveCurrentPlayerCount(runner, fallbackPlayerCount);
            _currentMaxPlayers = FusionPlayerCountUtility.ResolveMaxPlayers(runner, fallbackMaxPlayers);
        }
        else
        {
            FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
            _currentPlayerCount = context != null ? context.PlayerCount : 0;
            _currentMaxPlayers = context != null ? context.MaxPlayers : 0;
        }

        _latestRequests.Clear();
        IReadOnlyList<FusionPendingJoinRequestInfo> requests = _connectionManager.PendingJoinRequests;
        if (requests != null)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                FusionPendingJoinRequestInfo request = requests[i];
                if (request != null)
                    _latestRequests.Add(request);
            }
        }

        RefreshAllUi();
    }

    private void HandlePendingJoinRequestsChanged(IReadOnlyList<FusionPendingJoinRequestInfo> requests)
    {
        _latestRequests.Clear();

        if (requests != null)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                FusionPendingJoinRequestInfo request = requests[i];
                if (request != null)
                    _latestRequests.Add(request);
            }
        }

        RefreshAllUi();
        SetStatus($"Pending Photon join requests updated. count={_latestRequests.Count}");
    }

    private void HandlePlayerCountChanged(int playerCount, int maxPlayers)
    {
        _currentPlayerCount = playerCount;
        _currentMaxPlayers = maxPlayers;
        RefreshCountTexts();
    }

    private void HandleStatusChanged(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return;

        SetStatus(status);
    }

    private void TryDecideJoinRequest(string requestId, bool approve)
    {
        if (_hostOnly && !CanCurrentUserManageHostRequests(out string reason))
        {
            SetStatus($"Host check failed. Decision blocked. reason={reason}");
            return;
        }

        if (!TryEnsureAndBindConnectionManager())
        {
            SetStatus("FusionConnectionManager is missing.");
            return;
        }

        if (string.IsNullOrWhiteSpace(requestId))
        {
            SetStatus("Photon requestId is empty.");
            return;
        }

        FusionPendingJoinRequestInfo photonRequest = FindPhotonRequest(requestId);

        bool handled = approve
            ? _connectionManager.ApproveJoinRequest(requestId)
            : _connectionManager.RejectJoinRequest(requestId);

        SetStatus(handled
            ? $"Photon join request {(approve ? "accepted" : "rejected")}. requestId={requestId}"
            : $"Photon join request not found. requestId={requestId}");

        if (!handled || photonRequest == null)
            return;

        _ = MirrorApiJoinDecisionAsync(photonRequest, approve);
    }

    private void RefreshAllUi()
    {
        RefreshCountTexts();
        RebuildRequestListUi();
        RefreshInteractableState();
    }

    private void RefreshCountTexts()
    {
        if (_currentMemberCountText != null)
            _currentMemberCountText.text = FormatCountText(_currentMemberCountFormat, GetRoomMemberCount(), _currentMaxPlayers);

        if (_pendingRequestCountText != null)
            _pendingRequestCountText.text = FormatCountText(_pendingRequestCountFormat, GetPendingRequestCount(), 0);
    }

    private void RebuildRequestListUi()
    {
        ClearRequestListUi();
        TryResolveListContent();

        int pendingCount = 0;

        if (_requestItemPrefab != null && _requestListContent != null)
        {
            for (int i = 0; i < _latestRequests.Count; i++)
            {
                FusionPendingJoinRequestInfo request = _latestRequests[i];
                if (request == null)
                    continue;

                pendingCount++;
                HostJoinRequestItemUI item = Instantiate(_requestItemPrefab, _requestListContent);
                item.gameObject.SetActive(true);
                item.ConfigurePhoton(request, HandleItemAcceptClicked, HandleItemRejectClicked);
                _spawnedItems.Add(item);
            }
        }

        if (_emptyStateObject != null)
            _emptyStateObject.SetActive(pendingCount == 0);
    }

    private void ClearRequestListUi()
    {
        for (int i = 0; i < _spawnedItems.Count; i++)
        {
            if (_spawnedItems[i] != null)
                Destroy(_spawnedItems[i].gameObject);
        }

        _spawnedItems.Clear();
    }

    private void RefreshInteractableState()
    {
        bool hostAllowed = !_hostOnly || CanCurrentUserManageHostRequests(out _);
        bool interactable = hostAllowed;

        if (_refreshButton != null)
            _refreshButton.interactable = hostAllowed;

        for (int i = 0; i < _spawnedItems.Count; i++)
        {
            if (_spawnedItems[i] != null)
                _spawnedItems[i].SetInteractable(interactable);
        }
    }

    private void RefreshHostVisibility()
    {
        if (!_hidePanelWhenNotHost || _mainPanel == null)
            return;

        bool shouldShow = !_hostOnly || CanCurrentUserManageHostRequests(out _);
        if (_mainPanel.activeSelf != shouldShow)
            _mainPanel.SetActive(shouldShow);
    }

    private IEnumerator DisableLegacyGuiMonitorNextFrame()
    {
        yield return null;
        DisableLegacyGuiMonitorIfNeeded();
    }

    private void DisableLegacyGuiMonitorIfNeeded()
    {
        if (!_disableLegacyGuiMonitorOnEnable)
            return;

        HostJoinRequestMonitorGUI[] legacyMonitors = FindObjectsOfType<HostJoinRequestMonitorGUI>(true);
        for (int i = 0; i < legacyMonitors.Length; i++)
        {
            HostJoinRequestMonitorGUI legacyMonitor = legacyMonitors[i];
            if (legacyMonitor == null || !legacyMonitor.enabled)
                continue;

            legacyMonitor.enabled = false;
            Log("Disabled legacy HostJoinRequestMonitorGUI instance.");
        }
    }

    private void TryResolveListContent()
    {
        if (_requestListContent != null)
            return;

        if (_requestScrollRect != null && _requestScrollRect.content != null)
            _requestListContent = _requestScrollRect.content;
    }

    private bool IsPanelVisible()
    {
        return _mainPanel == null || _mainPanel.activeInHierarchy;
    }

    private void BindButtons()
    {
        if (_refreshButton != null)
        {
            _refreshButton.onClick.RemoveListener(FetchNow);
            _refreshButton.onClick.AddListener(FetchNow);
        }
    }

    private void UnbindButtons()
    {
        if (_refreshButton != null)
            _refreshButton.onClick.RemoveListener(FetchNow);
    }

    private void HandleItemAcceptClicked(string requestId)
    {
        TryDecideJoinRequest(requestId, true);
    }

    private void HandleItemRejectClicked(string requestId)
    {
        TryDecideJoinRequest(requestId, false);
    }

    private async Task MirrorApiJoinDecisionAsync(FusionPendingJoinRequestInfo photonRequest, bool approve)
    {
        if (photonRequest == null || string.IsNullOrWhiteSpace(photonRequest.UserId))
        {
            Log("API join decision skipped because Photon userId is empty.");
            return;
        }

        string apiRoomId = ResolveApiRoomId();
        if (string.IsNullOrWhiteSpace(apiRoomId))
        {
            Log("API join decision skipped because apiRoomId is empty.");
            return;
        }

        if (!TryEnsureAndBindChatRoomManager())
        {
            Log("API join decision skipped because ChatRoomManager is missing.");
            return;
        }

        string accessToken = ResolveAccessTokenOverride();
        ChatRoomJoinRequestInfo apiRequest = await FindPendingApiJoinRequestAsync(
            apiRoomId,
            photonRequest.UserId,
            accessToken);

        if (apiRequest == null || string.IsNullOrWhiteSpace(apiRequest.RequestId))
        {
            Log($"API join request not found for user={photonRequest.UserId}, apiRoomId={apiRoomId}");
            return;
        }

        ChatRoomJoinRequestDecisionInfo decision = await DecideApiJoinRequestAsync(
            apiRoomId,
            apiRequest.RequestId,
            approve,
            accessToken);

        if (decision == null)
            return;

        SetStatus($"Photon/API join request {(approve ? "accepted" : "rejected")}. user={photonRequest.UserId}");
    }

    private async Task<ChatRoomJoinRequestInfo> FindPendingApiJoinRequestAsync(
        string apiRoomId,
        string userId,
        string accessToken)
    {
        string normalizedUserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        if (string.IsNullOrWhiteSpace(apiRoomId) || string.IsNullOrWhiteSpace(normalizedUserId))
            return null;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            ChatRoomJoinRequestInfo[] requests = await FetchApiJoinRequestsAsync(apiRoomId, accessToken);
            if (requests != null)
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    ChatRoomJoinRequestInfo request = requests[i];
                    if (request == null || string.IsNullOrWhiteSpace(request.RequestUserId))
                        continue;

                    if (!string.Equals(normalizedUserId, request.RequestUserId.Trim(), StringComparison.Ordinal))
                        continue;

                    if (!IsPendingJoinRequestStatus(request.Status))
                        continue;

                    return request;
                }
            }

            await Task.Delay(300);
        }

        return null;
    }

    private async Task<ChatRoomJoinRequestInfo[]> FetchApiJoinRequestsAsync(string apiRoomId, string accessToken)
    {
        if (!await WaitForChatRoomManagerIdleAsync())
            return null;

        _joinRequestsFetchTcs = new TaskCompletionSource<ChatRoomJoinRequestInfo[]>();
        _chatRoomManager.FetchJoinRequests(apiRoomId, accessToken);
        return await _joinRequestsFetchTcs.Task;
    }

    private async Task<ChatRoomJoinRequestDecisionInfo> DecideApiJoinRequestAsync(
        string apiRoomId,
        string requestId,
        bool approve,
        string accessToken)
    {
        if (!await WaitForChatRoomManagerIdleAsync())
            return null;

        _joinRequestDecisionTcs = new TaskCompletionSource<ChatRoomJoinRequestDecisionInfo>();
        _chatRoomManager.DecideJoinRequest(apiRoomId, requestId, approve, accessToken);
        return await _joinRequestDecisionTcs.Task;
    }

    private async Task<bool> WaitForChatRoomManagerIdleAsync(int timeoutMs = 5000)
    {
        if (_chatRoomManager == null)
            return false;

        int waitedMs = 0;
        while (_chatRoomManager.IsBusy && waitedMs < timeoutMs)
        {
            await Task.Delay(100);
            waitedMs += 100;
        }

        return !_chatRoomManager.IsBusy;
    }

    private bool TryEnsureAndBindChatRoomManager()
    {
        if (_chatRoomManager == null)
            _chatRoomManager = ChatRoomManager.Instance;

        if (_chatRoomManager == null && _createChatRoomManagerIfMissing)
        {
            var chatManagerObject = new GameObject("ChatRoomManager (Runtime)");
            _chatRoomManager = chatManagerObject.AddComponent<ChatRoomManager>();
        }

        if (_chatRoomManager == null)
            return false;

        if (_isChatRoomBound)
            return true;

        _chatRoomManager.OnJoinRequestsFetchSucceeded += HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed += HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestsFetchCanceled += HandleJoinRequestsFetchCanceled;
        _chatRoomManager.OnJoinRequestDecisionSucceeded += HandleJoinRequestDecisionSucceeded;
        _chatRoomManager.OnJoinRequestDecisionFailed += HandleJoinRequestDecisionFailed;
        _chatRoomManager.OnJoinRequestDecisionCanceled += HandleJoinRequestDecisionCanceled;
        _isChatRoomBound = true;
        return true;
    }

    private void UnbindChatRoomManager()
    {
        if (!_isChatRoomBound || _chatRoomManager == null)
            return;

        _chatRoomManager.OnJoinRequestsFetchSucceeded -= HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed -= HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestsFetchCanceled -= HandleJoinRequestsFetchCanceled;
        _chatRoomManager.OnJoinRequestDecisionSucceeded -= HandleJoinRequestDecisionSucceeded;
        _chatRoomManager.OnJoinRequestDecisionFailed -= HandleJoinRequestDecisionFailed;
        _chatRoomManager.OnJoinRequestDecisionCanceled -= HandleJoinRequestDecisionCanceled;
        _chatRoomManager = null;
        _isChatRoomBound = false;
    }

    private void HandleJoinRequestsFetchSucceeded(ChatRoomJoinRequestInfo[] requests)
    {
        _joinRequestsFetchTcs?.TrySetResult(requests ?? Array.Empty<ChatRoomJoinRequestInfo>());
        _joinRequestsFetchTcs = null;
    }

    private void HandleJoinRequestsFetchFailed(string message)
    {
        _joinRequestsFetchTcs?.TrySetResult(Array.Empty<ChatRoomJoinRequestInfo>());
        _joinRequestsFetchTcs = null;
        Log($"API join request fetch failed. message={message}");
    }

    private void HandleJoinRequestsFetchCanceled()
    {
        _joinRequestsFetchTcs?.TrySetResult(Array.Empty<ChatRoomJoinRequestInfo>());
        _joinRequestsFetchTcs = null;
    }

    private void HandleJoinRequestDecisionSucceeded(ChatRoomJoinRequestDecisionInfo info)
    {
        _joinRequestDecisionTcs?.TrySetResult(info);
        _joinRequestDecisionTcs = null;
    }

    private void HandleJoinRequestDecisionFailed(string roomId, string requestId, bool approve, string message)
    {
        _joinRequestDecisionTcs?.TrySetResult(null);
        _joinRequestDecisionTcs = null;
        Log($"API join request decision failed. roomId={roomId}, requestId={requestId}, approve={approve}, message={message}");
    }

    private void HandleJoinRequestDecisionCanceled(string roomId, string requestId, bool approve)
    {
        _joinRequestDecisionTcs?.TrySetResult(null);
        _joinRequestDecisionTcs = null;
    }

    private FusionPendingJoinRequestInfo FindPhotonRequest(string requestId)
    {
        string normalized = string.IsNullOrWhiteSpace(requestId) ? string.Empty : requestId.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        for (int i = 0; i < _latestRequests.Count; i++)
        {
            FusionPendingJoinRequestInfo request = _latestRequests[i];
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
                continue;

            if (string.Equals(normalized, request.RequestId.Trim(), StringComparison.Ordinal))
                return request;
        }

        return null;
    }

    private string ResolveApiRoomId()
    {
        return NetworkRoomIdentity.ResolveApiRoomId(_roomIdOverride);
    }

    private string ResolveAccessTokenOverride()
    {
        if (!string.IsNullOrWhiteSpace(_accessTokenOverride))
            return _accessTokenOverride.Trim();

        return AuthManager.Instance != null ? AuthManager.Instance.GetAccessToken() : null;
    }

    private static bool IsPendingJoinRequestStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized == string.Empty ||
               normalized == "REQUESTED" ||
               normalized == "PENDING" ||
               normalized == "WAITING";
    }

    private bool CanCurrentUserManageHostRequests(out string reason)
    {
        reason = string.Empty;

        FusionConnectionManager manager = FusionConnectionManager.Instance;
        NetworkRunner runner = manager != null ? manager.Runner : null;
        if (runner != null && runner.IsRunning && !runner.IsShutdown)
        {
            bool isHostRunner = runner.IsServer;
            reason = isHostRunner
                ? $"Photon runner is server. session={runner.SessionInfo.Name}"
                : $"Photon runner is not server. session={runner.SessionInfo.Name}";
            return isHostRunner;
        }

        FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
        if (context != null)
        {
            bool isHostContext = context.IsHost || context.GameMode == GameMode.Host;
            reason = isHostContext
                ? $"Fusion session context indicates host. session={context.SessionName}"
                : $"Fusion session context indicates client. session={context.SessionName}";
            return isHostContext;
        }

        reason = "Fusion runner/session context unavailable";
        return false;
    }

    private int GetRoomMemberCount()
    {
        return Mathf.Max(0, _currentPlayerCount);
    }

    private int GetPendingRequestCount()
    {
        return _latestRequests.Count;
    }

    private static string FormatCountText(string format, int value, int maxValue)
    {
        if (string.IsNullOrWhiteSpace(format))
            return maxValue > 0 ? $"{value}/{maxValue}" : value.ToString();

        try
        {
            return format.Contains("{1}")
                ? string.Format(format, value, maxValue)
                : string.Format(format, value);
        }
        catch (FormatException)
        {
            return maxValue > 0 ? $"{value}/{maxValue}" : value.ToString();
        }
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;

        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[HostJoinRequestMonitorUI] {text}");
    }

    private void Log(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"[HostJoinRequestMonitorUI] {message}");
    }
}
