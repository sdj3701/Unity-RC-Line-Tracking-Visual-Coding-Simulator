using System;
using System.Threading.Tasks;
using Auth;
using RC.App.Defines;
using RC.Network.Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class NetworkRoomExitController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button _leaveButton;
    [SerializeField] private bool _showFallbackGui = true;
    [SerializeField] private Vector2 _fallbackButtonSize = new Vector2(180f, 48f);
    [SerializeField] private Vector2 _fallbackButtonOffset = new Vector2(24f, 24f);
    [SerializeField] private int _fallbackFontSize = 18;

    [Header("Scene")]
    [SerializeField] private string _networkSceneName = AppScenes.NetworkCarTest;
    [SerializeField] private string _lobbySceneName = AppScenes.Lobby;
    [SerializeField] private bool _loadLobbySceneAfterExit = true;
    [SerializeField] private bool _connectLobbyAfterSceneLoad = true;
    [SerializeField] private bool _destroyControllerAfterExit = true;

    [Header("Server State")]
    [SerializeField] private bool _callChatLeaveApiBeforeShutdown = true;
    [SerializeField] private bool _cancelBusyChatRequestBeforeLeave = true;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private FusionConnectionManager _connectionManager;
    private bool _connectionEventsBound;
    private bool _exitInProgress;
    private bool _returnToLobbyInProgress;
    private string _status = "In room";
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private int _cachedFontSize = -1;

    private readonly struct ExitContext
    {
        public ExitContext(string apiRoomId, string sessionName, string userId, bool isHost)
        {
            ApiRoomId = apiRoomId;
            SessionName = sessionName;
            UserId = userId;
            IsHost = isHost;
        }

        public string ApiRoomId { get; }
        public string SessionName { get; }
        public string UserId { get; }
        public bool IsHost { get; }
    }

    private void OnEnable()
    {
        BindLeaveButton();
        BindConnectionManager();
    }

    private void OnDisable()
    {
        UnbindLeaveButton();
        UnbindConnectionManager();
    }

    private void OnDestroy()
    {
        UnbindLeaveButton();
        UnbindConnectionManager();
    }

    public void LeaveRoom()
    {
        _ = LeaveRoomAsync();
    }

    public async Task LeaveRoomAsync()
    {
        if (_exitInProgress)
            return;

        _exitInProgress = true;
        SetStatus("Leaving room...");
        SetLeaveButtonInteractable(false);
        DontDestroyOnLoad(gameObject);

        ExitContext exitContext = CaptureExitContext();
        PrepareLocalExit();
        await TryLeaveChatRoomInDbAsync(exitContext);

        bool success = true;
        try
        {
            BindConnectionManager();
            if (_connectionManager != null)
                success = await _connectionManager.LeaveRoomAsync(FusionRoomExitReason.UserLeave);
            else
                success = false;
        }
        catch (Exception e)
        {
            success = false;
            SetStatus($"Room leave failed: {e.Message}");
            LogWarning($"LeaveRoomAsync failed. message={e.Message}");
        }

        await ReturnToLobbyAsync(
            success ? FusionRoomExitReason.UserLeave : FusionRoomExitReason.ShutdownFailed,
            success ? "Room leave completed." : "Room leave cleanup failed.");
    }

    private void HandleRoomExitStarted(FusionRoomExitReason reason)
    {
        SetLeaveButtonInteractable(false);
        SetStatus($"Room exit started. reason={reason}");
    }

    private void HandleRoomExitCompleted(FusionRoomExitReason reason, bool success, string message)
    {
        if (_exitInProgress || _returnToLobbyInProgress)
            return;

        if (!IsNetworkSceneActive())
            return;

        _ = HandleRemoteRoomExitAsync(reason, success, message);
    }

    private async Task HandleRemoteRoomExitAsync(FusionRoomExitReason reason, bool success, string message)
    {
        _exitInProgress = true;
        SetLeaveButtonInteractable(false);
        DontDestroyOnLoad(gameObject);
        PrepareLocalExit();

        string resolvedMessage = string.IsNullOrWhiteSpace(message)
            ? $"Room exit detected. reason={reason}"
            : message.Trim();
        SetStatus(resolvedMessage);

        await ReturnToLobbyAsync(reason, resolvedMessage);
    }

    private async Task ReturnToLobbyAsync(FusionRoomExitReason reason, string message)
    {
        if (_returnToLobbyInProgress)
            return;

        _returnToLobbyInProgress = true;
        SetStatus(BuildReturnStatus(reason, message));
        ClearLocalRoomState();

        if (_loadLobbySceneAfterExit && !string.IsNullOrWhiteSpace(_lobbySceneName))
        {
            string activeSceneName = SceneManager.GetActiveScene().name;
            if (!string.Equals(activeSceneName, _lobbySceneName, StringComparison.Ordinal))
                SceneManager.LoadScene(_lobbySceneName);
        }

        await Task.Yield();

        if (_connectLobbyAfterSceneLoad)
        {
            FusionConnectionManager manager = FusionConnectionManager.GetOrCreate();
            if (manager != null && !manager.IsInSessionLobby && !manager.IsLeavingRoom)
                await manager.ConnectToPhotonLobbyAsync();
        }

        if (_destroyControllerAfterExit)
            Destroy(gameObject);
    }

    private ExitContext CaptureExitContext()
    {
        NetworkRunnerSafeState(out bool isHost);

        return new ExitContext(
            NetworkRoomIdentity.ResolveApiRoomId(),
            NetworkRoomIdentity.ResolvePhotonSessionName(),
            ResolveCurrentUserId(),
            isHost);
    }

    private void NetworkRunnerSafeState(out bool isHost)
    {
        isHost = false;

        FusionConnectionManager manager = FusionConnectionManager.Instance;
        if (manager != null && manager.Runner != null && manager.Runner.IsRunning && !manager.Runner.IsShutdown)
        {
            isHost = manager.Runner.IsServer;
            return;
        }

        FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
        isHost = context != null && (context.IsHost || context.GameMode == Fusion.GameMode.Host);
    }

    private async Task<bool> TryLeaveChatRoomInDbAsync(ExitContext exitContext)
    {
        if (!_callChatLeaveApiBeforeShutdown)
            return true;

        if (string.IsNullOrWhiteSpace(exitContext.ApiRoomId))
        {
            LogWarning("Chat room leave API skipped because apiRoomId is empty.");
            return false;
        }

        ChatRoomManager manager = ChatRoomManager.Instance;
        if (manager == null)
        {
            LogWarning("Chat room leave API skipped because ChatRoomManager.Instance is null.");
            return false;
        }

        if (manager.IsBusy && _cancelBusyChatRequestBeforeLeave)
        {
            manager.CancelCurrentRequest();
            await WaitForChatRoomManagerIdleAsync(manager);
        }

        if (manager.IsBusy)
        {
            LogWarning("Chat room leave API skipped because ChatRoomManager is still busy.");
            return false;
        }

        ChatRoomLeaveInfo leaveInfo = await manager.LeaveRoomAsync(exitContext.ApiRoomId);
        if (leaveInfo == null || !leaveInfo.Success)
        {
            string message = leaveInfo != null ? leaveInfo.Message : "leave result is null";
            LogWarning($"Chat room leave API failed. roomId={exitContext.ApiRoomId}, session={exitContext.SessionName}, user={exitContext.UserId}, isHost={exitContext.IsHost}, message={message}");
            return false;
        }

        SetStatus($"DB participant removed. roomId={exitContext.ApiRoomId}");
        return true;
    }

    private static async Task WaitForChatRoomManagerIdleAsync(ChatRoomManager manager)
    {
        const int MaxWaitFrames = 30;
        for (int i = 0; i < MaxWaitFrames && manager != null && manager.IsBusy; i++)
            await Task.Yield();
    }

    private void ClearLocalRoomState()
    {
        ChatRoomManager chatRoomManager = ChatRoomManager.Instance;
        if (chatRoomManager != null)
            chatRoomManager.CancelCurrentRequest();

        NetworkRoomRosterPanel.ClearAllSnapshots();
        But_RoomList.ResetAllJoinFlowState();
        FusionRoomSessionContext.Clear();
        RoomSessionContext.Clear();
    }

    private void PrepareLocalExit()
    {
        HostNetworkCarCoordinator[] coordinators = FindObjectsOfType<HostNetworkCarCoordinator>();
        for (int i = 0; i < coordinators.Length; i++)
        {
            if (coordinators[i] != null)
                coordinators[i].PrepareForLocalRoomExit();
        }

        HostExecutionScheduler[] schedulers = FindObjectsOfType<HostExecutionScheduler>();
        for (int i = 0; i < schedulers.Length; i++)
        {
            if (schedulers[i] != null)
                schedulers[i].StopExecution();
        }
    }

    private void BindConnectionManager()
    {
        FusionConnectionManager manager = FusionConnectionManager.GetOrCreate();
        if (_connectionManager == manager && _connectionEventsBound)
            return;

        UnbindConnectionManager();
        _connectionManager = manager;

        if (_connectionManager == null)
            return;

        _connectionManager.OnRoomExitStarted += HandleRoomExitStarted;
        _connectionManager.OnRoomExitCompleted += HandleRoomExitCompleted;
        _connectionEventsBound = true;
    }

    private void UnbindConnectionManager()
    {
        if (!_connectionEventsBound || _connectionManager == null)
            return;

        _connectionManager.OnRoomExitStarted -= HandleRoomExitStarted;
        _connectionManager.OnRoomExitCompleted -= HandleRoomExitCompleted;
        _connectionEventsBound = false;
        _connectionManager = null;
    }

    private void BindLeaveButton()
    {
        if (_leaveButton == null)
            return;

        _leaveButton.onClick.RemoveListener(LeaveRoom);
        _leaveButton.onClick.AddListener(LeaveRoom);
        SetLeaveButtonInteractable(!_exitInProgress);
    }

    private void UnbindLeaveButton()
    {
        if (_leaveButton == null)
            return;

        _leaveButton.onClick.RemoveListener(LeaveRoom);
    }

    private void SetLeaveButtonInteractable(bool interactable)
    {
        if (_leaveButton != null)
            _leaveButton.interactable = interactable;
    }

    private bool IsNetworkSceneActive()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        return string.Equals(activeSceneName, _networkSceneName, StringComparison.Ordinal);
    }

    private static string BuildReturnStatus(FusionRoomExitReason reason, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            return message.Trim();

        if (reason == FusionRoomExitReason.HostClosed)
            return "Host closed the room.";

        if (reason == FusionRoomExitReason.Disconnected)
            return "Disconnected from room.";

        return "Returning to lobby.";
    }

    private static string ResolveCurrentUserId()
    {
        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
            return string.Empty;

        string userId = AuthManager.Instance.CurrentUser.userId;
        return string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
    }

    private void SetStatus(string status)
    {
        _status = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
        if (_debugLog && !string.IsNullOrWhiteSpace(_status))
            Debug.Log($"[NetworkRoomExitController] {_status}");
    }

    private void LogWarning(string message)
    {
        if (_debugLog && !string.IsNullOrWhiteSpace(message))
            Debug.LogWarning($"[NetworkRoomExitController] {message}");
    }

    private void OnGUI()
    {
        if (!_showFallbackGui || _leaveButton != null)
            return;

        if (!IsNetworkSceneActive() && !_exitInProgress)
            return;

        EnsureGuiStyles();

        float width = Mathf.Max(120f, _fallbackButtonSize.x);
        float height = Mathf.Max(32f, _fallbackButtonSize.y);
        float x = Mathf.Max(0f, Screen.width - width - Mathf.Max(0f, _fallbackButtonOffset.x));
        float y = Mathf.Max(0f, _fallbackButtonOffset.y);
        Rect buttonRect = new Rect(x, y, width, height);

        bool previousEnabled = GUI.enabled;
        GUI.enabled = !_exitInProgress && (_connectionManager == null || !_connectionManager.IsLeavingRoom);

        if (GUI.Button(buttonRect, _exitInProgress ? "Leaving..." : "Leave Room", _buttonStyle))
            LeaveRoom();

        GUI.enabled = previousEnabled;

        if (!string.IsNullOrWhiteSpace(_status))
        {
            Rect labelRect = new Rect(x - 160f, y + height + 8f, width + 160f, 28f);
            GUI.Label(labelRect, _status, _labelStyle);
        }
    }

    private void EnsureGuiStyles()
    {
        int fontSize = Mathf.Max(10, _fallbackFontSize);
        if (_buttonStyle != null && _labelStyle != null && _cachedFontSize == fontSize)
            return;

        _cachedFontSize = fontSize;
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = fontSize,
            alignment = TextAnchor.MiddleCenter
        };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(10, fontSize - 2),
            alignment = TextAnchor.MiddleRight,
            wordWrap = false
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoadedCallback()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapActiveScene()
    {
        TryBootstrapForScene(SceneManager.GetActiveScene());
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryBootstrapForScene(scene);
    }

    private static void TryBootstrapForScene(Scene scene)
    {
        if (!string.Equals(scene.name, AppScenes.NetworkCarTest, StringComparison.Ordinal))
            return;

        if (FindObjectOfType<NetworkRoomExitController>() != null)
            return;

        GameObject obj = new GameObject(nameof(NetworkRoomExitController));
        obj.AddComponent<NetworkRoomExitController>();
    }
}
