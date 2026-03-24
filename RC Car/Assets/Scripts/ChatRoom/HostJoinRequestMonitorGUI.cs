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
    [SerializeField] private bool _startCollapsed = false;
    [SerializeField] private int _maxVisibleRows = 8;
    [SerializeField] private Vector2 _windowPosition = new Vector2(24f, 24f);
    [SerializeField] private Vector2 _windowSize = new Vector2(580f, 420f);

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private const float MinPollIntervalSeconds = 0.5f;
    private const string TargetSceneName = "03_NetworkCarTest";

    private ChatRoomManager _chatRoomManager;
    private Coroutine _pollingCoroutine;
    private bool _isBound;
    private bool _isCollapsed;
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
    private string _activeDecisionRequestKey = string.Empty;
    private bool _activeDecisionApprove;

    private void Awake()
    {
        _isCollapsed = _startCollapsed;
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
        if (_hostOnly && !IsCurrentUserHost())
        {
            SetStatus("Current user is not host. Poll skipped.");
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

    private bool IsCurrentUserHost()
    {
        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        string hostUserId = currentRoom != null ? currentRoom.HostUserId : string.Empty;
        string currentUserId = AuthManager.Instance != null && AuthManager.Instance.CurrentUser != null
            ? AuthManager.Instance.CurrentUser.userId
            : string.Empty;

        if (string.IsNullOrWhiteSpace(hostUserId) || string.IsNullOrWhiteSpace(currentUserId))
            return true;

        return string.Equals(hostUserId.Trim(), currentUserId.Trim(), StringComparison.Ordinal);
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

    private void OnGUI()
    {
        if (!_showGui)
            return;

        _windowRect = GUI.Window(
            GetInstanceID(),
            _windowRect,
            DrawWindowContents,
            "Host Join Request Monitor");
    }

    private void DrawWindowContents(int windowId)
    {
        GUILayout.BeginVertical();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_isCollapsed ? "Expand" : "Collapse", GUILayout.Width(90f)))
            _isCollapsed = !_isCollapsed;

        if (GUILayout.Button(_pollingCoroutine == null ? "Start Polling" : "Stop Polling", GUILayout.Width(120f)))
        {
            if (_pollingCoroutine == null)
                StartPolling();
            else
                StopPolling();
        }

        if (GUILayout.Button("Fetch Now", GUILayout.Width(90f)))
            FetchNow();

        if (GUILayout.Button("Clear New", GUILayout.Width(90f)))
            _newRequestKeys.Clear();

        GUILayout.EndHorizontal();

        if (_isCollapsed)
        {
            GUILayout.Label($"roomId={ResolveTargetRoomId()}");
            GUILayout.Label($"pending={_latestRequests.Count}, new={_newRequestKeys.Count}");
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
            return;
        }

        GUILayout.Space(6f);
        GUILayout.Label($"Scene: {SceneManager.GetActiveScene().name}");
        GUILayout.Label($"Manager Ready: {_chatRoomManager != null}");
        GUILayout.Label($"Polling: {(_pollingCoroutine != null ? "ON" : "OFF")} (interval={Mathf.Max(MinPollIntervalSeconds, _pollIntervalSeconds):0.0}s)");
        GUILayout.Label($"Host Only: {_hostOnly} (current user host={IsCurrentUserHost()})");
        GUILayout.Label($"Target RoomId: {ResolveTargetRoomId()}");
        GUILayout.Label($"Last Status: {_lastStatus}");
        GUILayout.Label($"Last Fetch UTC: {(_lastFetchUtc == DateTime.MinValue ? "-" : _lastFetchUtc.ToString("yyyy-MM-dd HH:mm:ss"))}");
        GUILayout.Label($"Pending Requests: {_latestRequests.Count} / New Since Baseline: {_newRequestKeys.Count}");
        GUILayout.Label(
            $"Decision In Progress: {!string.IsNullOrWhiteSpace(_activeDecisionRequestKey)}" +
            $"{(string.IsNullOrWhiteSpace(_activeDecisionRequestKey) ? string.Empty : $" ({(_activeDecisionApprove ? "ACCEPT" : "REJECT")}, key={_activeDecisionRequestKey})")}");

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Room Override", GUILayout.Width(110f));
        _roomIdOverride = GUILayout.TextField(_roomIdOverride ?? string.Empty);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Token Override", GUILayout.Width(110f));
        _accessTokenOverride = GUILayout.TextField(_accessTokenOverride ?? string.Empty);
        GUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(_lastError))
        {
            Color previousColor = GUI.color;
            GUI.color = Color.red;
            GUILayout.Label($"Error: {_lastError}");
            GUI.color = previousColor;
        }

        GUILayout.Space(8f);
        GUILayout.Label("Latest Join Requests");

        float rowHeight = 22f;
        float viewportHeight = Mathf.Max(rowHeight * Mathf.Max(1, _maxVisibleRows), rowHeight);
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(viewportHeight));

        if (_latestRequests.Count == 0)
        {
            GUILayout.Label("(empty)");
        }
        else
        {
            for (int i = 0; i < _latestRequests.Count; i++)
            {
                ChatRoomJoinRequestInfo request = _latestRequests[i];
                string key = BuildJoinRequestKey(request);
                bool isNew = _newRequestKeys.Contains(key);
                string prefix = isNew ? "[NEW] " : string.Empty;
                string requestId = request != null ? request.RequestId : string.Empty;
                string userId = request != null ? request.RequestUserId : string.Empty;
                string status = request != null ? request.Status : string.Empty;
                string createdAt = request != null ? request.CreatedAtUtc : string.Empty;
                bool isPending = string.IsNullOrWhiteSpace(status) ||
                                 string.Equals(status, "REQUESTED", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);
                bool decisionBusy = !string.IsNullOrWhiteSpace(_activeDecisionRequestKey);
                bool canDecide = request != null &&
                                 isPending &&
                                 !string.IsNullOrWhiteSpace(requestId) &&
                                 !decisionBusy;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{prefix}req={requestId}, user={userId}, status={status}, created={createdAt}", GUILayout.ExpandWidth(true));

                bool previousEnabled = GUI.enabled;
                GUI.enabled = canDecide;

                if (GUILayout.Button("Accept", GUILayout.Width(72f)))
                    TryDecideJoinRequest(request, true);

                if (GUILayout.Button("Reject", GUILayout.Width(72f)))
                    TryDecideJoinRequest(request, false);

                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
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
