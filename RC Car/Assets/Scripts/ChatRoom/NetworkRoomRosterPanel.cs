using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class NetworkRoomRosterPanel : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_Text _participantsText;
    [SerializeField] private TMP_Text _waitingText;
    [SerializeField] private TMP_Text _statusText;

    [Header("Options")]
    [SerializeField] private bool _showParticipantHeader = true;
    [SerializeField] private bool _showWaitingHeader = true;
    [SerializeField] private bool _fetchWaitingListFromApi = true;
    [SerializeField] private bool _allowClientWaitingListFetch = false;
    [SerializeField] private float _refreshIntervalSeconds = 1f;
    [SerializeField] private float _apiFetchIntervalSeconds = 5f;

    [Header("Fallback GUI")]
    // Unused: IMGUI fallback is no longer used.
    // [SerializeField] private bool _showFallbackGuiWhenTextMissing = true;
    [SerializeField] private Rect _fallbackGuiRect = new Rect(24f, 24f, 420f, 320f);
    [SerializeField] private int _fallbackFontSize = 16;

    [Header("Request Target")]
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private string _accessTokenOverride = string.Empty;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private readonly List<ChatRoomJoinRequestInfo> _apiJoinRequests = new List<ChatRoomJoinRequestInfo>();
    private FusionConnectionManager _connectionManager;
    private ChatRoomManager _chatRoomManager;
    private bool _chatEventsBound;
    private float _nextRefreshAt;
    private float _nextApiFetchAt;
    private string _participantsSnapshot = "Participants\n(none)";
    private string _waitingSnapshot = "Waiting\n(none)";
    private GUIStyle _fallbackWindowStyle;
    private GUIStyle _fallbackLabelStyle;
    private Vector2 _fallbackScroll;

    private const string NetworkCarSceneName = "03_NetworkCarTest";

    private void OnEnable()
    {
        BindFusion();
        BindChatRoomManager();
        RefreshAll(forceApiFetch: true);
    }

    private void OnDisable()
    {
        UnbindFusion();
        UnbindChatRoomManager();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.25f, _refreshIntervalSeconds);
        RefreshAll(forceApiFetch: false);
    }

    public void RefreshNow()
    {
        RefreshAll(forceApiFetch: true);
    }

    private void BindFusion()
    {
        FusionConnectionManager manager = FusionConnectionManager.GetOrCreate();
        if (_connectionManager == manager)
            return;

        UnbindFusion();
        _connectionManager = manager;

        if (_connectionManager == null)
            return;

        _connectionManager.OnPendingJoinRequestsChanged += HandlePendingJoinRequestsChanged;
        _connectionManager.OnPlayerCountChanged += HandlePlayerCountChanged;
        _connectionManager.OnStatusChanged += HandleFusionStatusChanged;
    }

    private void UnbindFusion()
    {
        if (_connectionManager == null)
            return;

        _connectionManager.OnPendingJoinRequestsChanged -= HandlePendingJoinRequestsChanged;
        _connectionManager.OnPlayerCountChanged -= HandlePlayerCountChanged;
        _connectionManager.OnStatusChanged -= HandleFusionStatusChanged;
        _connectionManager = null;
    }

    private void BindChatRoomManager()
    {
        ChatRoomManager manager = ChatRoomManager.Instance;
        if (_chatRoomManager == manager && _chatEventsBound)
            return;

        UnbindChatRoomManager();
        _chatRoomManager = manager;

        if (_chatRoomManager == null)
            return;

        _chatRoomManager.OnJoinRequestsFetchSucceeded += HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed += HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestDecisionSucceeded += HandleJoinRequestDecisionSucceeded;
        _chatEventsBound = true;
    }

    private void UnbindChatRoomManager()
    {
        if (!_chatEventsBound || _chatRoomManager == null)
            return;

        _chatRoomManager.OnJoinRequestsFetchSucceeded -= HandleJoinRequestsFetchSucceeded;
        _chatRoomManager.OnJoinRequestsFetchFailed -= HandleJoinRequestsFetchFailed;
        _chatRoomManager.OnJoinRequestDecisionSucceeded -= HandleJoinRequestDecisionSucceeded;
        _chatEventsBound = false;
        _chatRoomManager = null;
    }

    private void RefreshAll(bool forceApiFetch)
    {
        BindFusion();
        BindChatRoomManager();
        TryFetchWaitingList(forceApiFetch);
        RefreshParticipantsText();
        RefreshWaitingText();
    }

    private void TryFetchWaitingList(bool force)
    {
        if (!_fetchWaitingListFromApi)
            return;

        if (!force && Time.unscaledTime < _nextApiFetchAt)
            return;

        _nextApiFetchAt = Time.unscaledTime + Mathf.Max(1f, _apiFetchIntervalSeconds);

        if (!CanFetchWaitingListFromApi(out string reason))
        {
            SetStatus($"Waiting list API skipped. reason={reason}");
            return;
        }

        if (_chatRoomManager == null || _chatRoomManager.IsBusy)
            return;

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
            return;

        _chatRoomManager.FetchJoinRequests(roomId, ResolveTokenOverride());
    }

    private bool CanFetchWaitingListFromApi(out string reason)
    {
        if (_allowClientWaitingListFetch)
        {
            reason = "client fetch allowed";
            return true;
        }

        if (IsHost())
        {
            reason = "current user is host";
            return true;
        }

        reason = "client fetch disabled";
        return false;
    }

    private void RefreshParticipantsText()
    {
        StringBuilder builder = new StringBuilder();
        if (_showParticipantHeader)
            builder.AppendLine("Participants");

        NetworkRunner runner = _connectionManager != null ? _connectionManager.Runner : null;
        int count = 0;
        if (runner != null && runner.IsRunning && !runner.IsShutdown)
        {
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                count++;
                string userId = ResolvePlayerUserId(runner, player);
                builder.Append(count)
                    .Append(". ")
                    .Append(string.IsNullOrWhiteSpace(userId) ? player.ToString() : userId)
                    .Append("  [")
                    .Append(player)
                    .AppendLine("]");
            }
        }

        if (count == 0)
            builder.AppendLine("(none)");

        _participantsSnapshot = builder.ToString().TrimEnd();

        if (_participantsText != null)
            _participantsText.text = _participantsSnapshot;
    }

    private void RefreshWaitingText()
    {
        StringBuilder builder = new StringBuilder();
        if (_showWaitingHeader)
            builder.AppendLine("Waiting");

        int count = AppendFusionWaitingRequests(builder);
        count += AppendApiWaitingRequests(builder, count);

        if (count == 0)
            builder.AppendLine("(none)");

        _waitingSnapshot = builder.ToString().TrimEnd();

        if (_waitingText != null)
            _waitingText.text = _waitingSnapshot;
    }

    private int AppendFusionWaitingRequests(StringBuilder builder)
    {
        IReadOnlyList<FusionPendingJoinRequestInfo> requests =
            _connectionManager != null ? _connectionManager.PendingJoinRequests : null;

        if (requests == null || requests.Count == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < requests.Count; i++)
        {
            FusionPendingJoinRequestInfo request = requests[i];
            if (request == null)
                continue;

            count++;
            builder.Append(count)
                .Append(". ")
                .Append(FirstNonEmpty(request.DisplayName, request.UserId, request.RemoteAddress, "unknown"))
                .Append("  [photon:")
                .Append(request.RequestId)
                .AppendLine("]");
        }

        return count;
    }

    private int AppendApiWaitingRequests(StringBuilder builder, int existingCount)
    {
        if (_apiJoinRequests.Count == 0)
            return 0;

        int count = 0;
        for (int i = 0; i < _apiJoinRequests.Count; i++)
        {
            ChatRoomJoinRequestInfo request = _apiJoinRequests[i];
            if (request == null || !IsPendingStatus(request.Status))
                continue;

            count++;
            builder.Append(existingCount + count)
                .Append(". ")
                .Append(FirstNonEmpty(request.RequestUserId, request.RequestId, "unknown"))
                .Append("  [api:")
                .Append(request.RequestId)
                .AppendLine("]");
        }

        return count;
    }

    private void HandlePendingJoinRequestsChanged(IReadOnlyList<FusionPendingJoinRequestInfo> requests)
    {
        RefreshWaitingText();
    }

    private void HandlePlayerCountChanged(int playerCount, int maxPlayers)
    {
        RefreshParticipantsText();
    }

    private void HandleFusionStatusChanged(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
            SetStatus(status);
    }

    private void HandleJoinRequestsFetchSucceeded(ChatRoomJoinRequestInfo[] requests)
    {
        _apiJoinRequests.Clear();
        if (requests != null)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i] != null)
                    _apiJoinRequests.Add(requests[i]);
            }
        }

        SetStatus($"Waiting list updated. apiCount={_apiJoinRequests.Count}");
        RefreshWaitingText();
    }

    private void HandleJoinRequestsFetchFailed(string message)
    {
        SetStatus($"Waiting list fetch failed. {message}");
    }

    private void HandleJoinRequestDecisionSucceeded(ChatRoomJoinRequestDecisionInfo info)
    {
        RefreshAll(forceApiFetch: true);
    }

    private bool IsHost()
    {
        NetworkRunner runner = _connectionManager != null ? _connectionManager.Runner : null;
        if (runner != null && runner.IsRunning && !runner.IsShutdown)
            return runner.IsServer;

        FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
        return context != null && (context.IsHost || context.GameMode == GameMode.Host);
    }

    private static string ResolvePlayerUserId(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null)
            return string.Empty;

        string userId = runner.GetPlayerUserId(player);
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }

    private string ResolveTargetRoomId()
    {
        return NetworkRoomIdentity.ResolveApiRoomId(_roomIdOverride);
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_accessTokenOverride) ? null : _accessTokenOverride.Trim();
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[NetworkRoomRosterPanel] {text}");
    }

    private static bool IsPendingStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return true;

        string normalized = status.Trim().ToUpperInvariant();
        return normalized == "REQUESTED" || normalized == "PENDING";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return string.Empty;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i].Trim();
        }

        return string.Empty;
    }

    private void OnGUI()
    {
        // IMGUI fallback is no longer used.
        /*
        if (!_showFallbackGuiWhenTextMissing)
            return;

        if (_participantsText != null && _waitingText != null)
            return;

        EnsureFallbackStyles();
        _fallbackGuiRect = GUI.Window(
            GetInstanceID(),
            _fallbackGuiRect,
            DrawFallbackWindow,
            "Room Roster",
            _fallbackWindowStyle);
        */
    }

    private void DrawFallbackWindow(int id)
    {
        _fallbackScroll = GUILayout.BeginScrollView(_fallbackScroll);
        GUILayout.Label(_participantsSnapshot, _fallbackLabelStyle);
        GUILayout.Space(12f);
        GUILayout.Label(_waitingSnapshot, _fallbackLabelStyle);
        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0f, 0f, _fallbackGuiRect.width, 28f));
    }

    private void EnsureFallbackStyles()
    {
        int fontSize = Mathf.Max(10, _fallbackFontSize);
        if (_fallbackWindowStyle != null &&
            _fallbackLabelStyle != null &&
            _fallbackLabelStyle.fontSize == fontSize)
        {
            return;
        }

        _fallbackWindowStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = fontSize
        };

        _fallbackLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            wordWrap = true
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapForNetworkCarScene()
    {
        // IMGUI fallback panel bootstrap is no longer used.
        /*
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, NetworkCarSceneName, StringComparison.Ordinal))
            return;

        if (FindObjectOfType<NetworkRoomRosterPanel>() != null)
            return;

        GameObject obj = new GameObject("NetworkRoomRosterPanel");
        obj.AddComponent<NetworkRoomRosterPanel>();
        */
    }
}
