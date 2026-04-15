using System;
using System.Collections;
using System.Collections.Generic;
using Auth;
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

    private readonly List<ChatRoomJoinRequestInfo> _latestRequests = new List<ChatRoomJoinRequestInfo>();
    private readonly List<HostJoinRequestItemUI> _spawnedItems = new List<HostJoinRequestItemUI>();
    private readonly HashSet<string> _approvedClientKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private ChatRoomManager _chatRoomManager;
    private Coroutine _pollingCoroutine;
    private Coroutine _pendingFetchWhenIdleCoroutine;
    private bool _isBound;
    private bool _fetchInProgress;
    private string _trackedRoomId = string.Empty;
    private string _lastError = string.Empty;
    private string _activeDecisionRequestKey = string.Empty;

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
        TryEnsureAndBindManager();
        DisableLegacyGuiMonitorIfNeeded();
        RefreshHostVisibility();
        RefreshAllUi();

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
        StopPendingFetchWhenIdle();
        UnbindButtons();
        UnbindManagerEvents();
        _fetchInProgress = false;
        _activeDecisionRequestKey = string.Empty;
    }

    public void StartPolling()
    {
        if (_pollingCoroutine != null)
            return;

        _pollingCoroutine = StartCoroutine(PollJoinRequestsLoop());
        SetStatus("Join request polling started.");
    }

    public void StopPolling()
    {
        if (_pollingCoroutine == null)
            return;

        StopCoroutine(_pollingCoroutine);
        _pollingCoroutine = null;
        SetStatus("Join request polling stopped.");
    }

    public void FetchNow()
    {
        TryFetchJoinRequests();
    }

    private IEnumerator PollJoinRequestsLoop()
    {
        while (enabled)
        {
            float interval = Mathf.Max(MinPollIntervalSeconds, _pollIntervalSeconds);
            yield return new WaitForSeconds(interval);

            if (IsPanelVisible())
                TryFetchJoinRequests();
        }

        _pollingCoroutine = null;
    }

    private void TryFetchJoinRequests()
    {
        RefreshHostVisibility();

        if (_hostOnly && !CanCurrentUserManageHostRequests(out string reason))
        {
            SetStatus($"Host check failed. Fetch skipped. reason={reason}");
            RefreshInteractableState();
            return;
        }

        if (_fetchInProgress)
        {
            SetStatus("Join request fetch is already in progress.");
            RefreshInteractableState();
            return;
        }

        if (!TryEnsureAndBindManager())
        {
            SetStatus("ChatRoomManager is missing.");
            RefreshInteractableState();
            return;
        }

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("Room ID is empty. Set RoomSessionContext or override.");
            RefreshInteractableState();
            return;
        }

        if (_chatRoomManager.IsBusy)
        {
            QueueFetchWhenManagerIsIdle();
            SetStatus("ChatRoomManager is busy. Fetch queued.");
            RefreshInteractableState();
            return;
        }

        ResetTrackingIfRoomChanged(roomId);
        _lastError = string.Empty;
        _fetchInProgress = true;
        RefreshInteractableState();

        _chatRoomManager.FetchJoinRequests(roomId, ResolveTokenOverride());
        SetStatus($"Join request fetch requested. roomId={roomId}");
    }

    private bool TryEnsureAndBindManager()
    {
        if (_chatRoomManager == null)
            _chatRoomManager = ChatRoomManager.Instance;

        if (_chatRoomManager == null && _createChatRoomManagerIfMissing)
        {
            var managerObject = new GameObject("ChatRoomManager (Runtime)");
            _chatRoomManager = managerObject.AddComponent<ChatRoomManager>();
            Log("ChatRoomManager instance was missing. Created runtime ChatRoomManager.");
        }

        if (_chatRoomManager == null)
            return false;

        if (_isBound)
            return true;

        _chatRoomManager.OnJoinRequestsFetchStarted += HandleJoinRequestsFetchStarted;
        _chatRoomManager.OnJoinRequestsFetchSucceeded += HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed += HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestsFetchCanceled += HandleJoinRequestsFetchCanceled;
        _chatRoomManager.OnJoinRequestDecisionStarted += HandleJoinRequestDecisionStarted;
        _chatRoomManager.OnJoinRequestDecisionSucceeded += HandleJoinRequestDecisionSucceeded;
        _chatRoomManager.OnJoinRequestDecisionFailed += HandleJoinRequestDecisionFailed;
        _chatRoomManager.OnJoinRequestDecisionCanceled += HandleJoinRequestDecisionCanceled;
        _isBound = true;
        return true;
    }

    private void UnbindManagerEvents()
    {
        if (!_isBound || _chatRoomManager == null)
            return;

        _chatRoomManager.OnJoinRequestsFetchStarted -= HandleJoinRequestsFetchStarted;
        _chatRoomManager.OnJoinRequestsFetchSucceeded -= HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed -= HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestsFetchCanceled -= HandleJoinRequestsFetchCanceled;
        _chatRoomManager.OnJoinRequestDecisionStarted -= HandleJoinRequestDecisionStarted;
        _chatRoomManager.OnJoinRequestDecisionSucceeded -= HandleJoinRequestDecisionSucceeded;
        _chatRoomManager.OnJoinRequestDecisionFailed -= HandleJoinRequestDecisionFailed;
        _chatRoomManager.OnJoinRequestDecisionCanceled -= HandleJoinRequestDecisionCanceled;
        _isBound = false;
    }

    private void HandleJoinRequestsFetchStarted(string roomId)
    {
        string targetRoomId = ResolveTargetRoomId();
        if (!IsSameRoom(targetRoomId, roomId))
            return;

        _fetchInProgress = true;
        RefreshInteractableState();
        SetStatus($"Join request fetch started. roomId={roomId}");
    }

    private void HandleJoinRequestsFetchSucceeded(ChatRoomJoinRequestInfo[] requests)
    {
        _fetchInProgress = false;
        _lastError = string.Empty;
        _latestRequests.Clear();

        if (requests != null)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                ChatRoomJoinRequestInfo request = requests[i];
                if (request == null)
                    continue;

                _latestRequests.Add(request);

                if (IsApprovedStatus(request.Status))
                    TrackApprovedClient(request);
            }
        }

        RefreshAllUi();
        SetStatus($"Join requests updated. pending={GetPendingRequestCount()}");
    }

    private void HandleJoinRequestsFetchFailed(string message)
    {
        _fetchInProgress = false;
        _lastError = string.IsNullOrWhiteSpace(message) ? "Join request fetch failed." : message;
        RefreshInteractableState();
        SetStatus($"Join request fetch failed. message={_lastError}");
    }

    private void HandleJoinRequestsFetchCanceled()
    {
        _fetchInProgress = false;
        RefreshInteractableState();
        SetStatus("Join request fetch canceled.");
    }

    private void HandleJoinRequestDecisionStarted(string roomId, string requestId, bool approve)
    {
        RefreshInteractableState();
        SetStatus($"Join request decision started. action={(approve ? "ACCEPT" : "REJECT")}, requestId={requestId}");
    }

    private void HandleJoinRequestDecisionSucceeded(ChatRoomJoinRequestDecisionInfo info)
    {
        ClearActiveDecision(info != null ? info.RoomId : string.Empty, info != null ? info.RequestId : string.Empty);

        string action = info != null && info.Approved ? "ACCEPT" : "REJECT";
        long code = info != null ? info.ResponseCode : 0;

        if (info != null)
        {
            if (info.Approved)
                TrackApprovedClient(info.RoomId, info.RequestId);

            UpdateLatestRequestStatus(info.RoomId, info.RequestId, info.Approved ? "APPROVED" : "REJECTED");
        }

        RefreshAllUi();
        SetStatus($"Join request decision succeeded. action={action}, requestId={info?.RequestId}, code={code}");

        QueueFetchWhenManagerIsIdle();
    }

    private void HandleJoinRequestDecisionFailed(string roomId, string requestId, bool approve, string message)
    {
        ClearActiveDecision(roomId, requestId);

        string action = approve ? "ACCEPT" : "REJECT";
        _lastError = string.IsNullOrWhiteSpace(message) ? "Join request decision failed." : message;
        RefreshInteractableState();
        SetStatus($"Join request decision failed. action={action}, requestId={requestId}, message={_lastError}");
    }

    private void HandleJoinRequestDecisionCanceled(string roomId, string requestId, bool approve)
    {
        ClearActiveDecision(roomId, requestId);
        RefreshInteractableState();
        SetStatus($"Join request decision canceled. action={(approve ? "ACCEPT" : "REJECT")}, requestId={requestId}");
    }

    private void TryDecideJoinRequest(ChatRoomJoinRequestInfo request, bool approve)
    {
        if (request == null)
            return;

        if (_hostOnly && !CanCurrentUserManageHostRequests(out string reason))
        {
            SetStatus($"Host check failed. Decision blocked. reason={reason}");
            return;
        }

        if (!TryEnsureAndBindManager())
        {
            SetStatus("ChatRoomManager is missing.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_activeDecisionRequestKey))
        {
            SetStatus("A join request decision is already in progress.");
            RefreshInteractableState();
            return;
        }

        if (_chatRoomManager.IsBusy)
        {
            SetStatus("ChatRoomManager is busy. Decision skipped.");
            RefreshInteractableState();
            return;
        }

        string roomId = !string.IsNullOrWhiteSpace(request.RoomId)
            ? request.RoomId.Trim()
            : ResolveTargetRoomId();
        string requestId = string.IsNullOrWhiteSpace(request.RequestId) ? string.Empty : request.RequestId.Trim();

        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(requestId))
        {
            SetStatus("RoomId or RequestId is empty. Decision cannot be sent.");
            return;
        }

        _activeDecisionRequestKey = BuildJoinRequestKey(request);
        if (string.IsNullOrWhiteSpace(_activeDecisionRequestKey))
            _activeDecisionRequestKey = requestId;

        _lastError = string.Empty;
        RefreshInteractableState();
        SetStatus($"Join request decision requested. action={(approve ? "ACCEPT" : "REJECT")}, requestId={requestId}");

        _chatRoomManager.DecideJoinRequest(roomId, requestId, approve, ResolveTokenOverride());
    }

    private void QueueFetchWhenManagerIsIdle()
    {
        if (_pendingFetchWhenIdleCoroutine != null)
            return;

        _pendingFetchWhenIdleCoroutine = StartCoroutine(FetchWhenManagerIsIdle());
    }

    private void StopPendingFetchWhenIdle()
    {
        if (_pendingFetchWhenIdleCoroutine == null)
            return;

        StopCoroutine(_pendingFetchWhenIdleCoroutine);
        _pendingFetchWhenIdleCoroutine = null;
    }

    private IEnumerator FetchWhenManagerIsIdle()
    {
        while (enabled)
        {
            if (_chatRoomManager == null && !TryEnsureAndBindManager())
                break;

            if (_chatRoomManager != null &&
                !_chatRoomManager.IsBusy &&
                !_fetchInProgress &&
                string.IsNullOrWhiteSpace(_activeDecisionRequestKey))
            {
                _pendingFetchWhenIdleCoroutine = null;
                FetchNow();
                yield break;
            }

            yield return null;
        }

        _pendingFetchWhenIdleCoroutine = null;
    }

    private void ResetTrackingIfRoomChanged(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return;

        if (string.Equals(_trackedRoomId, roomId, StringComparison.Ordinal))
            return;

        _trackedRoomId = roomId;
        _latestRequests.Clear();
        _approvedClientKeys.Clear();
        _activeDecisionRequestKey = string.Empty;
        RefreshAllUi();
        SetStatus($"Tracking room changed. roomId={roomId}");
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
            _currentMemberCountText.text = FormatCountText(_currentMemberCountFormat, GetRoomMemberCount());

        if (_pendingRequestCountText != null)
            _pendingRequestCountText.text = FormatCountText(_pendingRequestCountFormat, GetPendingRequestCount());
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
                ChatRoomJoinRequestInfo request = _latestRequests[i];
                if (request == null || !IsPendingStatus(request.Status))
                    continue;

                pendingCount++;
                HostJoinRequestItemUI item = Instantiate(_requestItemPrefab, _requestListContent);
                item.gameObject.SetActive(true);
                item.Configure(request, HandleItemAcceptClicked, HandleItemRejectClicked);
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
        bool decisionBusy = !string.IsNullOrWhiteSpace(_activeDecisionRequestKey);
        bool interactable = hostAllowed && !_fetchInProgress && !decisionBusy;

        if (_refreshButton != null)
            _refreshButton.interactable = hostAllowed && !_fetchInProgress;

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

        HostJoinRequestMonitorGUI[] legacyMonitors = FindObjectsOfType<HostJoinRequestMonitorGUI>();
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

    private void HandleItemAcceptClicked(ChatRoomJoinRequestInfo request)
    {
        TryDecideJoinRequest(request, true);
    }

    private void HandleItemRejectClicked(ChatRoomJoinRequestInfo request)
    {
        TryDecideJoinRequest(request, false);
    }

    private void TrackApprovedClient(string roomId, string requestId)
    {
        ChatRoomJoinRequestInfo request = FindLatestRequest(roomId, requestId);
        if (request != null)
        {
            TrackApprovedClient(request);
            return;
        }

        string fallbackKey = string.IsNullOrWhiteSpace(requestId) ? string.Empty : requestId.Trim();
        if (!string.IsNullOrWhiteSpace(fallbackKey))
            _approvedClientKeys.Add(fallbackKey);
    }

    private void TrackApprovedClient(ChatRoomJoinRequestInfo request)
    {
        if (request != null &&
            !string.IsNullOrWhiteSpace(request.RequestUserId) &&
            !string.IsNullOrWhiteSpace(request.RequestId))
        {
            _approvedClientKeys.Remove(request.RequestId.Trim());
        }

        string key = BuildApprovedClientKey(request);
        if (!string.IsNullOrWhiteSpace(key))
            _approvedClientKeys.Add(key);
    }

    private ChatRoomJoinRequestInfo FindLatestRequest(string roomId, string requestId)
    {
        string normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? string.Empty : requestId.Trim();
        string normalizedRoomId = string.IsNullOrWhiteSpace(roomId) ? string.Empty : roomId.Trim();

        for (int i = 0; i < _latestRequests.Count; i++)
        {
            ChatRoomJoinRequestInfo request = _latestRequests[i];
            if (request == null)
                continue;

            string currentRequestId = string.IsNullOrWhiteSpace(request.RequestId) ? string.Empty : request.RequestId.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedRequestId) &&
                string.Equals(currentRequestId, normalizedRequestId, StringComparison.Ordinal))
            {
                return request;
            }

            if (string.IsNullOrWhiteSpace(normalizedRequestId) &&
                !string.IsNullOrWhiteSpace(normalizedRoomId) &&
                string.Equals(
                    string.IsNullOrWhiteSpace(request.RoomId) ? string.Empty : request.RoomId.Trim(),
                    normalizedRoomId,
                    StringComparison.Ordinal))
            {
                return request;
            }
        }

        return null;
    }

    private void UpdateLatestRequestStatus(string roomId, string requestId, string status)
    {
        ChatRoomJoinRequestInfo request = FindLatestRequest(roomId, requestId);
        if (request != null)
            request.Status = status;
    }

    private void ClearActiveDecision(string roomId, string requestId)
    {
        if (string.IsNullOrWhiteSpace(_activeDecisionRequestKey))
            return;

        string key = BuildJoinRequestKey(new ChatRoomJoinRequestInfo
        {
            RoomId = roomId,
            RequestId = requestId
        });

        if (string.IsNullOrWhiteSpace(key) ||
            string.Equals(_activeDecisionRequestKey, key, StringComparison.Ordinal))
        {
            _activeDecisionRequestKey = string.Empty;
        }
    }

    private bool CanCurrentUserManageHostRequests(out string reason)
    {
        reason = string.Empty;

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom == null)
        {
            reason = "RoomSessionContext.CurrentRoom is null";
            return false;
        }

        string hostUserId = string.IsNullOrWhiteSpace(currentRoom.HostUserId)
            ? string.Empty
            : currentRoom.HostUserId.Trim();
        if (string.IsNullOrWhiteSpace(hostUserId))
        {
            reason = "HostUserId is empty";
            return false;
        }

        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
        {
            reason = "Current user is not resolved";
            return false;
        }

        string currentUserId = string.IsNullOrWhiteSpace(AuthManager.Instance.CurrentUser.userId)
            ? string.Empty
            : AuthManager.Instance.CurrentUser.userId.Trim();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            reason = "Current user id is empty";
            return false;
        }

        bool isHost = string.Equals(hostUserId, currentUserId, StringComparison.Ordinal);
        reason = isHost
            ? "Current user matches HostUserId"
            : $"Current user does not match HostUserId (host={hostUserId}, me={currentUserId})";
        return isHost;
    }

    private string ResolveTargetRoomId()
    {
        if (!string.IsNullOrWhiteSpace(_roomIdOverride))
            return _roomIdOverride.Trim();

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom != null && !string.IsNullOrWhiteSpace(currentRoom.RoomId))
            return currentRoom.RoomId.Trim();

        return string.Empty;
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_accessTokenOverride)
            ? null
            : _accessTokenOverride.Trim();
    }

    private int GetRoomMemberCount()
    {
        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom == null || string.IsNullOrWhiteSpace(currentRoom.RoomId))
            return 0;

        var approvedUsers = new HashSet<string>(_approvedClientKeys, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _latestRequests.Count; i++)
        {
            ChatRoomJoinRequestInfo request = _latestRequests[i];
            if (request == null || !IsApprovedStatus(request.Status))
                continue;

            string userKey = BuildApprovedClientKey(request);

            if (!string.IsNullOrWhiteSpace(userKey))
                approvedUsers.Add(userKey);
        }

        return 1 + approvedUsers.Count;
    }

    private int GetPendingRequestCount()
    {
        int pendingCount = 0;

        for (int i = 0; i < _latestRequests.Count; i++)
        {
            ChatRoomJoinRequestInfo request = _latestRequests[i];
            if (request != null && IsPendingStatus(request.Status))
                pendingCount++;
        }

        return pendingCount;
    }

    private static string BuildJoinRequestKey(ChatRoomJoinRequestInfo info)
    {
        if (info == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(info.RequestId))
            return info.RequestId.Trim();

        string roomId = string.IsNullOrWhiteSpace(info.RoomId) ? string.Empty : info.RoomId.Trim();
        string userId = string.IsNullOrWhiteSpace(info.RequestUserId) ? string.Empty : info.RequestUserId.Trim();
        string createdAt = string.IsNullOrWhiteSpace(info.CreatedAtUtc) ? string.Empty : info.CreatedAtUtc.Trim();
        return $"{roomId}|{userId}|{createdAt}";
    }

    private static string BuildApprovedClientKey(ChatRoomJoinRequestInfo info)
    {
        if (info == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(info.RequestUserId))
            return info.RequestUserId.Trim();

        if (!string.IsNullOrWhiteSpace(info.RequestId))
            return info.RequestId.Trim();

        return BuildJoinRequestKey(info);
    }

    private static bool IsPendingStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return true;

        string normalized = status.Trim().ToUpperInvariant();
        return normalized == "REQUESTED" || normalized == "PENDING";
    }

    private static bool IsApprovedStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        string normalized = status.Trim().ToUpperInvariant();
        return normalized == "APPROVED" || normalized == "ACCEPTED";
    }

    private static bool IsSameRoom(string expectedRoomId, string incomingRoomId)
    {
        string left = string.IsNullOrWhiteSpace(expectedRoomId) ? string.Empty : expectedRoomId.Trim();
        string right = string.IsNullOrWhiteSpace(incomingRoomId) ? string.Empty : incomingRoomId.Trim();
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string FormatCountText(string format, int value)
    {
        if (string.IsNullOrWhiteSpace(format))
            return value.ToString();

        try
        {
            return string.Format(format, value);
        }
        catch (FormatException)
        {
            return value.ToString();
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
