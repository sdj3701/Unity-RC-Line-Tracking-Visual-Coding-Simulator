using System;
using System.Collections;
using System.Collections.Generic;
using Auth;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Host-side join request monitor for 03_NetworkCarTest.
/// Polls join request list and renders a simple IMGUI panel.
/// </summary>
public sealed class HostJoinRequestMonitorGUI : MonoBehaviour
{
    [Header("Polling")]
    [SerializeField] private bool _autoStartPolling = true;
    [SerializeField] private float _pollIntervalSeconds = 2f;
    [SerializeField] private bool _hostOnly = true;
    [SerializeField] private bool _createChatRoomManagerIfMissing = true;

    [Header("Request Target")]
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private string _accessTokenOverride = string.Empty;

    [Header("GUI")]
    [SerializeField] private bool _showGui = true;
    [SerializeField] private int _maxVisibleRows = 8;
    [SerializeField] private Vector2 _windowPosition = new Vector2(24f, 24f);
    [SerializeField] private Vector2 _windowSize = new Vector2(1280f, 860f);
    [Tooltip("Recommended for 600x500 window: 24")]
    [SerializeField] private int _windowTitleFontSize = 24;
    [Tooltip("Recommended for 600x500 window: 20")]
    [SerializeField] private int _labelFontSize = 20;
    [Tooltip("Recommended for 600x500 window: 20")]
    [SerializeField] private int _buttonFontSize = 20;
    [Tooltip("Recommended for 600x500 window: 20")]
    [SerializeField] private int _textFieldFontSize = 20;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private const float MinPollIntervalSeconds = 0.5f;
    private const string TargetSceneName = "03_NetworkCarTest";

    private ChatRoomManager _chatRoomManager;
    private Coroutine _pollingCoroutine;
    private bool _isBound;
    private bool _snapshotInitialized;
    private string _trackedRoomId;
    private string _lastError = string.Empty;
    private string _lastStatus = "Idle";
    private DateTime _lastFetchUtc = DateTime.MinValue;
    private Rect _windowRect;
    private Vector2 _scrollPosition;
    private readonly List<ChatRoomJoinRequestInfo> _latestRequests = new List<ChatRoomJoinRequestInfo>();
    private readonly HashSet<string> _seenRequestKeys = new HashSet<string>();
    private readonly HashSet<string> _newRequestKeys = new HashSet<string>();
    private GUIStyle _windowStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _rowLabelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _textFieldStyle;
    private int _cachedWindowFontSize = -1;
    private int _cachedLabelFontSize = -1;
    private int _cachedButtonFontSize = -1;
    private int _cachedTextFieldFontSize = -1;
    private string _activeDecisionRequestKey = string.Empty;
    private bool _activeDecisionApprove;

    private void Awake()
    {
        _windowRect = new Rect(_windowPosition.x, _windowPosition.y, _windowSize.x, _windowSize.y);
    }

    private void OnEnable()
    {
        TryEnsureAndBindManager();

        if (_autoStartPolling)
            StartPolling();
    }

    private void OnDisable()
    {
        StopPolling();
        UnbindManagerEvents();
    }

    public void StartPolling()
    {
        if (_pollingCoroutine != null)
            return;

        _pollingCoroutine = StartCoroutine(PollJoinRequestsLoop());
        SetStatus("Polling started.");
    }

    public void StopPolling()
    {
        if (_pollingCoroutine == null)
            return;

        StopCoroutine(_pollingCoroutine);
        _pollingCoroutine = null;
        SetStatus("Polling stopped.");
    }

    public void FetchNow()
    {
        TryFetchJoinRequests();
    }

    private IEnumerator PollJoinRequestsLoop()
    {
        while (true)
        {
            TryFetchJoinRequests();
            yield return new WaitForSeconds(Mathf.Max(MinPollIntervalSeconds, _pollIntervalSeconds));
        }
    }

    private void TryFetchJoinRequests()
    {
        if (_hostOnly && !CanCurrentUserManageHostRequests(out string reason))
        {
            SetStatus($"Host check failed. Poll skipped. reason={reason}");
            return;
        }

        if (!TryEnsureAndBindManager())
        {
            SetStatus("ChatRoomManager is missing.");
            return;
        }

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("Room ID is empty. Set RoomSessionContext or override.");
            return;
        }

        if (_chatRoomManager.IsBusy)
        {
            SetStatus("ChatRoomManager is busy. Waiting for next poll.");
            return;
        }

        ResetTrackingIfRoomChanged(roomId);
        _chatRoomManager.FetchJoinRequests(roomId, ResolveTokenOverride());
        SetStatus($"Fetch requested. roomId={roomId}");
    }

    private bool TryEnsureAndBindManager()
    {
        if (_chatRoomManager == null)
            _chatRoomManager = ChatRoomManager.Instance;

        if (_chatRoomManager == null && _createChatRoomManagerIfMissing)
        {
            var chatManagerObject = new GameObject("ChatRoomManager (Runtime)");
            _chatRoomManager = chatManagerObject.AddComponent<ChatRoomManager>();
            Log("ChatRoomManager instance was missing. Created runtime ChatRoomManager.");
        }

        if (_chatRoomManager == null)
            return false;

        if (_isBound)
            return true;

        _chatRoomManager.OnJoinRequestsFetchSucceeded += HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed += HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestsFetchCanceled += HandleJoinRequestsFetchCanceled;
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

        _chatRoomManager.OnJoinRequestsFetchSucceeded -= HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed -= HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestsFetchCanceled -= HandleJoinRequestsFetchCanceled;
        _chatRoomManager.OnJoinRequestDecisionSucceeded -= HandleJoinRequestDecisionSucceeded;
        _chatRoomManager.OnJoinRequestDecisionFailed -= HandleJoinRequestDecisionFailed;
        _chatRoomManager.OnJoinRequestDecisionCanceled -= HandleJoinRequestDecisionCanceled;
        _isBound = false;
    }

    private void HandleJoinRequestsFetchSucceeded(ChatRoomJoinRequestInfo[] requests)
    {
        _lastError = string.Empty;
        _lastFetchUtc = DateTime.UtcNow;
        _latestRequests.Clear();
        _newRequestKeys.Clear();

        if (requests != null)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i] != null)
                    _latestRequests.Add(requests[i]);
            }
        }

        if (!_snapshotInitialized)
        {
            CacheSeenRequestKeys(_latestRequests);
            _snapshotInitialized = true;
            SetStatus($"Baseline initialized. count={_latestRequests.Count}");
            return;
        }

        int newCount = TrackAndCountNewRequests(_latestRequests);
        SetStatus($"Fetched join requests. count={_latestRequests.Count}, new={newCount}");

        if (newCount > 0)
            Log($"New join request detected. new={newCount}, roomId={_trackedRoomId}");
    }

    private void HandleJoinRequestsFetchFailed(string message)
    {
        _lastError = string.IsNullOrWhiteSpace(message) ? "Join request fetch failed." : message;
        SetStatus($"Fetch failed: {_lastError}");
    }

    private void HandleJoinRequestsFetchCanceled()
    {
        SetStatus("Fetch canceled.");
    }

    private void HandleJoinRequestDecisionSucceeded(ChatRoomJoinRequestDecisionInfo info)
    {
        string key = BuildJoinRequestKey(new ChatRoomJoinRequestInfo
        {
            RequestId = info != null ? info.RequestId : string.Empty,
            RoomId = info != null ? info.RoomId : string.Empty
        });

        if (!string.IsNullOrWhiteSpace(key) &&
            string.Equals(_activeDecisionRequestKey, key, StringComparison.Ordinal))
        {
            _activeDecisionRequestKey = string.Empty;
        }

        string action = info != null && info.Approved ? "ACCEPT" : "REJECT";
        long code = info != null ? info.ResponseCode : 0;
        SetStatus($"Decision success. action={action}, requestId={info?.RequestId}, code={code}");

        // Refresh list right after a decision to keep UI/state in sync with server.
        FetchNow();
    }

    private void HandleJoinRequestDecisionFailed(string roomId, string requestId, bool approve, string message)
    {
        string key = BuildJoinRequestKey(new ChatRoomJoinRequestInfo
        {
            RequestId = requestId,
            RoomId = roomId
        });

        if (!string.IsNullOrWhiteSpace(key) &&
            string.Equals(_activeDecisionRequestKey, key, StringComparison.Ordinal))
        {
            _activeDecisionRequestKey = string.Empty;
        }

        string action = approve ? "ACCEPT" : "REJECT";
        _lastError = string.IsNullOrWhiteSpace(message) ? "Decision failed." : message;
        SetStatus($"Decision failed. action={action}, requestId={requestId}, message={_lastError}");
    }

    private void HandleJoinRequestDecisionCanceled(string roomId, string requestId, bool approve)
    {
        string key = BuildJoinRequestKey(new ChatRoomJoinRequestInfo
        {
            RequestId = requestId,
            RoomId = roomId
        });

        if (!string.IsNullOrWhiteSpace(key) &&
            string.Equals(_activeDecisionRequestKey, key, StringComparison.Ordinal))
        {
            _activeDecisionRequestKey = string.Empty;
        }

        SetStatus($"Decision canceled. requestId={requestId}, action={(approve ? "ACCEPT" : "REJECT")}");
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

        string requestKey = BuildJoinRequestKey(request);
        if (!string.IsNullOrWhiteSpace(_activeDecisionRequestKey))
        {
            SetStatus("A decision is already in progress.");
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

        _activeDecisionRequestKey = requestKey;
        _activeDecisionApprove = approve;
        _lastError = string.Empty;
        SetStatus($"Decision requested. action={(approve ? "ACCEPT" : "REJECT")}, requestId={requestId}");

        _chatRoomManager.DecideJoinRequest(
            roomId,
            requestId,
            approve,
            ResolveTokenOverride());
    }

    private void ResetTrackingIfRoomChanged(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return;

        if (string.Equals(_trackedRoomId, roomId, StringComparison.Ordinal))
            return;

        _trackedRoomId = roomId;
        _snapshotInitialized = false;
        _seenRequestKeys.Clear();
        _newRequestKeys.Clear();
        _latestRequests.Clear();
        _activeDecisionRequestKey = string.Empty;
        SetStatus($"Tracking room changed. roomId={roomId}");
    }

    private void CacheSeenRequestKeys(List<ChatRoomJoinRequestInfo> requests)
    {
        _seenRequestKeys.Clear();

        if (requests == null)
            return;

        for (int i = 0; i < requests.Count; i++)
        {
            string key = BuildJoinRequestKey(requests[i]);
            if (!string.IsNullOrWhiteSpace(key))
                _seenRequestKeys.Add(key);
        }
    }

    private int TrackAndCountNewRequests(List<ChatRoomJoinRequestInfo> requests)
    {
        if (requests == null || requests.Count == 0)
            return 0;

        int newCount = 0;

        for (int i = 0; i < requests.Count; i++)
        {
            string key = BuildJoinRequestKey(requests[i]);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            bool addedToSeen = _seenRequestKeys.Add(key);
            if (!addedToSeen)
                continue;

            _newRequestKeys.Add(key);
            newCount++;
        }

        return newCount;
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

    private string BuildHostRoleLabel()
    {
        if (CanCurrentUserManageHostRequests(out _))
            return "HOST";

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        bool hasHostUserId = currentRoom != null && !string.IsNullOrWhiteSpace(currentRoom.HostUserId);
        bool hasCurrentUser = AuthManager.Instance != null &&
                              AuthManager.Instance.CurrentUser != null &&
                              !string.IsNullOrWhiteSpace(AuthManager.Instance.CurrentUser.userId);

        if (!hasHostUserId || !hasCurrentUser)
            return "UNKNOWN";

        return "CLIENT";
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

    private int GetRoomMemberCount()
    {
        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom == null || string.IsNullOrWhiteSpace(currentRoom.RoomId))
            return 0;
        // Member count estimate: host(1) + approved join-request users.
        int hostCount = 1;
        var approvedUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < _latestRequests.Count; i++)
        {
            ChatRoomJoinRequestInfo request = _latestRequests[i];
            if (request == null || !IsApprovedStatus(request.Status))
                continue;

            string userKey = !string.IsNullOrWhiteSpace(request.RequestUserId)
                ? request.RequestUserId.Trim()
                : BuildJoinRequestKey(request);

            if (!string.IsNullOrWhiteSpace(userKey))
                approvedUsers.Add(userKey);
        }

        return hostCount + approvedUsers.Count;
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

    private void SetStatus(string message)
    {
        _lastStatus = string.IsNullOrWhiteSpace(message) ? "Idle" : message;
    }

    private void Log(string message)
    {
        if (!_debugLog)
            return;

        Debug.Log($"[HostJoinRequestMonitorGUI] {message}");
    }

    private void EnsureGuiStyles()
    {
        int windowFontSize = Mathf.Max(1, _windowTitleFontSize);
        int labelFontSize = Mathf.Max(1, _labelFontSize);
        int buttonFontSize = Mathf.Max(1, _buttonFontSize);
        int textFieldFontSize = Mathf.Max(1, _textFieldFontSize);

        bool requiresRebuild = _windowStyle == null ||
                               _labelStyle == null ||
                               _rowLabelStyle == null ||
                               _buttonStyle == null ||
                               _textFieldStyle == null ||
                               _cachedWindowFontSize != windowFontSize ||
                               _cachedLabelFontSize != labelFontSize ||
                               _cachedButtonFontSize != buttonFontSize ||
                               _cachedTextFieldFontSize != textFieldFontSize;

        if (!requiresRebuild)
            return;

        _cachedWindowFontSize = windowFontSize;
        _cachedLabelFontSize = labelFontSize;
        _cachedButtonFontSize = buttonFontSize;
        _cachedTextFieldFontSize = textFieldFontSize;

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = windowFontSize
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = labelFontSize,
            wordWrap = true
        };

        _rowLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = labelFontSize,
            wordWrap = false,
            clipping = TextClipping.Clip
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = buttonFontSize,
            alignment = TextAnchor.MiddleCenter
        };

        _textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = textFieldFontSize
        };
    }

    private void OnGUI()
    {
        if (!_showGui)
            return;

        if (_hostOnly && !CanCurrentUserManageHostRequests(out _))
            return;

        EnsureGuiStyles();

        _windowRect = GUI.Window(
            GetInstanceID(),
            _windowRect,
            DrawWindowContents,
            "Host Join Request Monitor",
            _windowStyle);
    }

    private void DrawWindowContents(int windowId)
    {
        float dragAreaHeight = Mathf.Max(64f, _windowTitleFontSize + 22f);
        float buttonHeight = Mathf.Max(46f, _buttonFontSize + 14f);
        float rowHeight = Mathf.Max(40f, _labelFontSize + 16f);
        float actionButtonWidth = Mathf.Max(110f, (_windowRect.width - 86f) * 0.5f);

        GUILayout.BeginVertical();
        
        if (GUILayout.Button("Fetch Now", _buttonStyle, GUILayout.Height(buttonHeight)))
            FetchNow();

        /*
         * Detailed debug UI is intentionally commented out per request:
         * - Collapse/Expand, Start/Stop Polling, Clear New buttons
         * - Scene/Host/Status/Fetch Time/Decision labels
         * - Room Override/Token Override input fields
         * - New-request baseline debug details
         */

        int roomMemberCount = GetRoomMemberCount();
        int pendingRequestCount = GetPendingRequestCount();

        GUILayout.Space(10f);
        GUILayout.Label($"Room Member Count: {roomMemberCount}", _labelStyle, GUILayout.Height(rowHeight));
        GUILayout.Label($"Pending Join Requests: {pendingRequestCount}", _labelStyle, GUILayout.Height(rowHeight));

        if (!string.IsNullOrWhiteSpace(_lastError))
        {
            Color previousColor = GUI.color;
            GUI.color = Color.red;
            GUILayout.Label($"Error: {_lastError}", _labelStyle, GUILayout.Height(rowHeight));
            GUI.color = previousColor;
        }

        GUILayout.Space(12f);
        GUILayout.Label("Client Join Requests", _labelStyle, GUILayout.Height(rowHeight));

        float reservedHeight = buttonHeight + (rowHeight * 3f) + 72f;
        float minViewportHeight = rowHeight + buttonHeight + 28f;
        float viewportHeight = Mathf.Clamp(
            _windowRect.height - reservedHeight,
            minViewportHeight,
            Mathf.Max(minViewportHeight, _windowRect.height - dragAreaHeight - 16f));
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(viewportHeight));

        bool hasPendingRequest = false;
        bool decisionBusy = !string.IsNullOrWhiteSpace(_activeDecisionRequestKey);

        for (int i = 0; i < _latestRequests.Count; i++)
        {
            ChatRoomJoinRequestInfo request = _latestRequests[i];
            if (request == null || !IsPendingStatus(request.Status))
                continue;

            hasPendingRequest = true;

            string requestId = request.RequestId ?? string.Empty;
            string userId = request.RequestUserId ?? string.Empty;
            bool canDecide = !string.IsNullOrWhiteSpace(requestId) && !decisionBusy;

            GUILayout.BeginVertical();
            GUILayout.Label(
                $"user={userId}, req={requestId}",
                _labelStyle,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(rowHeight));
            
            GUILayout.BeginHorizontal();

            bool previousEnabled = GUI.enabled;
            GUI.enabled = canDecide;

            if (GUILayout.Button("Accept", _buttonStyle, GUILayout.Width(actionButtonWidth), GUILayout.Height(buttonHeight)))
                TryDecideJoinRequest(request, true);

            if (GUILayout.Button("Reject", _buttonStyle, GUILayout.Width(actionButtonWidth), GUILayout.Height(buttonHeight)))
                TryDecideJoinRequest(request, false);

            GUI.enabled = previousEnabled;
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            GUILayout.EndVertical();
        }

        if (!hasPendingRequest)
            GUILayout.Label("(No pending join requests)", _labelStyle, GUILayout.Height(rowHeight));

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, dragAreaHeight));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapForNetworkCarTestScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, TargetSceneName, StringComparison.Ordinal))
            return;

        HostJoinRequestMonitorGUI existing = FindObjectOfType<HostJoinRequestMonitorGUI>();
        if (existing != null)
            return;

        var bootstrapObject = new GameObject("HostJoinRequestMonitorGUI");
        bootstrapObject.AddComponent<HostJoinRequestMonitorGUI>();
    }
}
