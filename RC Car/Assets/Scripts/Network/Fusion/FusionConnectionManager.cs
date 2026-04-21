using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Auth;
using Auth.Models;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;

namespace RC.Network.Fusion
{
    public enum FusionJoinApprovalMode
    {
        AutoAccept,
        Manual,
        AutoReject
    }

    public sealed class FusionPendingJoinRequestInfo
    {
        public string RequestId { get; set; }
        public string UserId { get; set; }
        public string DisplayName { get; set; }
        public string RemoteAddress { get; set; }
        public string RequestedAtUtc { get; set; }
        public int TokenBytes { get; set; }

        public override string ToString()
        {
            return $"request={RequestId}, user={UserId}, name={DisplayName}, remote={RemoteAddress}, tokenBytes={TokenBytes}";
        }
    }

    [Serializable]
    public sealed class FusionConnectionTokenPayload
    {
        public string userId;
        public string displayName;
        public string sessionName;
        public string requestedAtUtc;
    }

    public static class FusionConnectionTokenUtility
    {
        public static byte[] CreateForCurrentUser(string sessionName)
        {
            AuthManager authManager = AuthManager.Instance;
            UserInfo currentUser = authManager != null ? authManager.CurrentUser : null;

            var payload = new FusionConnectionTokenPayload
            {
                userId = currentUser != null ? Normalize(currentUser.userId) : string.Empty,
                displayName = ResolveDisplayName(currentUser),
                sessionName = Normalize(sessionName),
                requestedAtUtc = DateTime.UtcNow.ToString("o")
            };

            string json = JsonUtility.ToJson(payload);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : Encoding.UTF8.GetBytes(json);
        }

        public static FusionConnectionTokenPayload Decode(byte[] token)
        {
            if (token == null || token.Length == 0)
                return null;

            try
            {
                string json = Encoding.UTF8.GetString(token);
                return JsonUtility.FromJson<FusionConnectionTokenPayload>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ResolveDisplayName(UserInfo user)
        {
            if (user == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(user.name))
                return user.name.Trim();

            if (!string.IsNullOrWhiteSpace(user.username))
                return user.username.Trim();

            return Normalize(user.userId);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    [DisallowMultipleComponent]
    public sealed class FusionConnectionManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static FusionConnectionManager Instance { get; private set; }

        [Header("Authentication")]
        [SerializeField] private FusionAuthMode _authMode = FusionAuthMode.UserIdOnly;
        [SerializeField] private bool _useAuthPostData = false;

        [Header("Lobby")]
        [SerializeField] private SessionLobby _sessionLobby = SessionLobby.ClientServer;
        [SerializeField] private string _customLobbyName = string.Empty;
        [SerializeField] private bool _useCachedRegions = false;

        [Header("Runner")]
        [SerializeField] private NetworkRunner _runnerOverride;
        [SerializeField] private NetworkRunner _runnerPrefab;
        [SerializeField] private bool _provideInput = true;
        [SerializeField] private bool _dontDestroyRunnerOnLoad = true;

        [Header("Join Approval")]
        [SerializeField] private FusionJoinApprovalMode _joinApprovalMode = FusionJoinApprovalMode.Manual;
        [SerializeField] private float _manualJoinRequestTimeoutSeconds = 30f;

        [Header("Debug")]
        [SerializeField] private bool _debugLog = true;

        private readonly List<SessionInfo> _sessionInfos = new List<SessionInfo>();
        private readonly List<FusionPendingJoinRequestInfo> _pendingJoinRequestInfos = new List<FusionPendingJoinRequestInfo>();
        private readonly Dictionary<string, PendingJoinRequest> _pendingJoinRequests = new Dictionary<string, PendingJoinRequest>();
        private NetworkRunner _runner;
        private bool _ownsRunnerObject;
        private bool _isConnectingToLobby;
        private bool _isInSessionLobby;
        private bool _isInGameSession;

        public event Action<IReadOnlyList<SessionInfo>> OnSessionListChanged;
        public event Action<IReadOnlyList<FusionPendingJoinRequestInfo>> OnPendingJoinRequestsChanged;
        public event Action<int, int> OnPlayerCountChanged;
        public event Action<string> OnStatusChanged;

        public NetworkRunner Runner => _runner;
        public IReadOnlyList<SessionInfo> SessionInfos => _sessionInfos;
        public IReadOnlyList<FusionPendingJoinRequestInfo> PendingJoinRequests => _pendingJoinRequestInfos;
        public FusionAuthMode AuthMode => _authMode;
        public bool UseAuthPostData => _useAuthPostData;
        public FusionJoinApprovalMode JoinApprovalMode => _joinApprovalMode;
        public bool IsConnectingToLobby => _isConnectingToLobby;
        public bool IsInSessionLobby => _isInSessionLobby;
        public bool IsInGameSession => _isInGameSession;
        public bool IsPhotonConnected => _runner != null && !_runner.IsShutdown && (_isInSessionLobby || _isInGameSession || _runner.IsRunning);
        public string LastStatusMessage { get; private set; }
        public string LastErrorMessage { get; private set; }

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

        private void OnDestroy()
        {
            if (_runner != null)
                UnregisterCallbacksFromRunner(_runner);

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            RejectExpiredJoinRequests();
        }

        public static FusionConnectionManager GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var obj = new GameObject("FusionConnectionManager");
            return obj.AddComponent<FusionConnectionManager>();
        }

        public void ConfigureAuthentication(FusionAuthMode authMode, bool useAuthPostData)
        {
            _authMode = authMode;
            _useAuthPostData = useAuthPostData;

            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Auth, $"Photon auth configured. mode={_authMode}, postData={_useAuthPostData}");
        }

        public async Task<bool> ConnectToPhotonLobbyAsync()
        {
            if (_isConnectingToLobby)
            {
                SetStatus("Photon lobby connection is already in progress.", warning: true);
                return false;
            }

            if (_isInSessionLobby && _runner != null && !_runner.IsShutdown)
            {
                SetStatus($"Already connected to Photon lobby. cachedRooms={_sessionInfos.Count}");
                return true;
            }

            if (!FusionAuthFactory.TryCreateFromAuthManager(
                    out AuthenticationValues authValues,
                    out string errorMessage,
                    sessionName: null,
                    usePostData: _useAuthPostData,
                    authMode: _authMode))
            {
                SetStatus(errorMessage, error: true);
                return false;
            }

            _runner = EnsureRunner();
            if (_runner == null)
            {
                SetStatus("NetworkRunner could not be created for Photon lobby connection.", error: true);
                return false;
            }

            _runner.ProvideInput = _provideInput;
            RegisterCallbacksOnRunner(_runner);

            _isConnectingToLobby = true;
            _isInSessionLobby = false;
            _isInGameSession = false;
            LastErrorMessage = null;
            SetStatus($"Photon lobby connect started. lobby={_sessionLobby}, customLobby={ResolveCustomLobbyName()}, authMode={_authMode}");

            try
            {
                StartGameResult result = await _runner.JoinSessionLobby(
                    _sessionLobby,
                    ResolveCustomLobbyName(),
                    authValues,
                    null,
                    null,
                    default,
                    _useCachedRegions);

                if (!result.Ok)
                {
                    SetStatus($"Photon lobby connect failed. reason={result.ShutdownReason}, message={result.ErrorMessage}", error: true);
                    CleanupFailedRunner();
                    return false;
                }

                _isInSessionLobby = true;
                _isInGameSession = false;
                SetStatus($"Photon lobby connected. userId={_runner.UserId}, cachedRooms={_sessionInfos.Count}");
                return true;
            }
            catch (Exception e)
            {
                SetStatus($"Photon lobby connect exception: {e.Message}", error: true);
                CleanupFailedRunner();
                return false;
            }
            finally
            {
                _isConnectingToLobby = false;
            }
        }

        public NetworkRunner EnsureRunner()
        {
            if (_runner != null && !_runner.IsShutdown)
                return _runner;

            _ownsRunnerObject = false;

            if (_runnerOverride != null && !_runnerOverride.IsShutdown)
            {
                _runner = _runnerOverride;
                return _runner;
            }

            NetworkRunner created;
            if (_runnerPrefab != null)
            {
                created = Instantiate(_runnerPrefab);
                created.name = $"{_runnerPrefab.name}_Runtime";
                _ownsRunnerObject = true;
            }
            else
            {
                var runnerObject = new GameObject("Fusion NetworkRunner");
                created = runnerObject.AddComponent<NetworkRunner>();
                _ownsRunnerObject = true;
            }

            if (_dontDestroyRunnerOnLoad && created != null)
                DontDestroyOnLoad(created.gameObject);

            _runner = created;
            return _runner;
        }

        public NetworkSceneManagerDefault EnsureSceneManager()
        {
            NetworkRunner runner = EnsureRunner();
            if (runner == null)
                return null;

            NetworkSceneManagerDefault sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null)
                sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            return sceneManager;
        }

        public void RegisterCallbacksOnRunner(NetworkRunner runner = null)
        {
            NetworkRunner target = runner ?? EnsureRunner();
            if (target == null)
                return;

            target.RemoveCallbacks(this);
            target.AddCallbacks(this);
        }

        public void UnregisterCallbacksFromRunner(NetworkRunner runner = null)
        {
            NetworkRunner target = runner ?? _runner;
            if (target == null)
                return;

            target.RemoveCallbacks(this);
        }

        public void MarkGameSessionStarted()
        {
            _isInGameSession = true;
            _isInSessionLobby = false;
            FusionRoomSessionContext.UpdateFromRunner(_runner);
            SetStatus("Photon game session started.");
            UpdatePlayerCount(_runner);
        }

        public void MarkGameSessionEnded()
        {
            _isInGameSession = false;
            _isInSessionLobby = false;
            _sessionInfos.Clear();
            ClearPendingJoinRequests();
            FusionRoomSessionContext.Clear();
            OnSessionListChanged?.Invoke(_sessionInfos);
        }

        public void SetJoinApprovalMode(FusionJoinApprovalMode approvalMode)
        {
            _joinApprovalMode = approvalMode;
            SetStatus($"Photon join approval mode changed. mode={_joinApprovalMode}");
        }

        public bool ApproveJoinRequest(string requestId)
        {
            return CompletePendingJoinRequest(requestId, approve: true);
        }

        public bool RejectJoinRequest(string requestId)
        {
            return CompletePendingJoinRequest(requestId, approve: false);
        }

        public async Task ShutdownAsync()
        {
            if (_runner == null)
                return;

            NetworkRunner runner = _runner;
            SetStatus("Photon shutdown requested.");

            try
            {
                UnregisterCallbacksFromRunner(runner);
                if (!runner.IsShutdown)
                    await runner.Shutdown(false, ShutdownReason.Ok, false);
            }
            catch (Exception e)
            {
                SetStatus($"Photon shutdown failed: {e.Message}", warning: true);
            }
            finally
            {
                CleanupRunner(runner);
                MarkGameSessionEnded();
            }
        }

        private string ResolveCustomLobbyName()
        {
            return string.IsNullOrWhiteSpace(_customLobbyName) ? null : _customLobbyName.Trim();
        }

        private void CleanupFailedRunner()
        {
            if (_runner != null && _runner.IsShutdown)
                CleanupRunner(_runner);
        }

        private void CleanupRunner(NetworkRunner runner)
        {
            if (runner == null)
                return;

            bool shouldDestroy = _ownsRunnerObject && runner.gameObject != gameObject;
            if (ReferenceEquals(_runner, runner))
                _runner = null;

            if (shouldDestroy)
                Destroy(runner.gameObject);

            _ownsRunnerObject = false;
        }

        private void SetStatus(string message, bool warning = false, bool error = false)
        {
            LastStatusMessage = message ?? string.Empty;
            if (error)
                LastErrorMessage = LastStatusMessage;

            OnStatusChanged?.Invoke(LastStatusMessage);

            if (!_debugLog)
                return;

            if (error)
                FusionDebugLog.Error(FusionDebugFlow.Connect, LastStatusMessage);
            else if (warning)
                FusionDebugLog.Warning(FusionDebugFlow.Connect, LastStatusMessage);
            else
                FusionDebugLog.Info(FusionDebugFlow.Connect, LastStatusMessage);
        }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Room, $"Player joined. player={player}, local={runner.LocalPlayer}");

            FusionRoomSessionContext.UpdateFromRunner(runner);
            UpdatePlayerCount(runner);
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (_debugLog)
                FusionDebugLog.Warning(FusionDebugFlow.Room, $"Player left. player={player}");

            FusionRoomSessionContext.UpdateFromRunner(runner);
            UpdatePlayerCount(runner);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            SetStatus($"Photon runner shutdown. reason={shutdownReason}", warning: shutdownReason != ShutdownReason.Ok);
            MarkGameSessionEnded();
            CleanupRunner(runner);
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            SetStatus($"Disconnected from Photon server. reason={reason}", warning: true);
            _isInSessionLobby = false;
            _isInGameSession = false;
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            FusionConnectionTokenPayload payload = FusionConnectionTokenUtility.Decode(token);

            if (_joinApprovalMode == FusionJoinApprovalMode.AutoAccept)
            {
                request.Accept();
                LogJoinDecision("auto-accepted", request, payload, token);
                return;
            }

            if (_joinApprovalMode == FusionJoinApprovalMode.AutoReject)
            {
                request.Refuse();
                LogJoinDecision("auto-rejected", request, payload, token);
                return;
            }

            string requestId = Guid.NewGuid().ToString("N");
            var info = new FusionPendingJoinRequestInfo
            {
                RequestId = requestId,
                UserId = payload != null ? Normalize(payload.userId) : string.Empty,
                DisplayName = payload != null ? Normalize(payload.displayName) : string.Empty,
                RemoteAddress = request.RemoteAddress.ToString(),
                RequestedAtUtc = DateTime.UtcNow.ToString("o"),
                TokenBytes = token != null ? token.Length : 0
            };

            request.Waiting();
            _pendingJoinRequests[requestId] = new PendingJoinRequest(request, info, Time.unscaledTime + Mathf.Max(1f, _manualJoinRequestTimeoutSeconds));
            _pendingJoinRequestInfos.Add(info);
            FusionDebugLog.Warning(FusionDebugFlow.Room, $"Join request waiting for host decision. {info}");
            OnPendingJoinRequestsChanged?.Invoke(_pendingJoinRequestInfos);
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            SetStatus($"Photon connect failed. remote={remoteAddress}, reason={reason}", error: true);
        }

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
        }

        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
        {
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        public void OnConnectedToServer(NetworkRunner runner)
        {
            SetStatus($"Connected to Photon server. userId={runner.UserId}, localPlayer={runner.LocalPlayer}");
            FusionRoomSessionContext.UpdateFromRunner(runner);
            UpdatePlayerCount(runner);
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            _sessionInfos.Clear();
            if (sessionList != null)
                _sessionInfos.AddRange(sessionList);

            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Lobby, $"Photon session list updated. count={_sessionInfos.Count}");

            OnSessionListChanged?.Invoke(_sessionInfos);
        }

        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
            string keys = data == null ? "(null)" : string.Join(", ", data.Keys);
            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Auth, $"Photon custom auth response. keys={keys}");
        }

        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
            if (_debugLog)
                FusionDebugLog.Warning(FusionDebugFlow.Room, "Host migration token received. Host migration is not implemented yet.");
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Scene, "Fusion scene load done.");
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Scene, "Fusion scene load start.");
        }

        private void UpdatePlayerCount(NetworkRunner runner)
        {
            if (runner == null || runner.IsShutdown)
                return;

            FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
            string sessionName = runner.SessionInfo != null && runner.SessionInfo.IsValid
                ? runner.SessionInfo.Name
                : (context != null ? context.SessionName : string.Empty);

            int fallbackPlayerCount = context != null
                ? context.PlayerCount
                : (runner.IsRunning && runner.IsServer ? 1 : 0);
            int fallbackMaxPlayers = context != null ? context.MaxPlayers : 0;
            int playerCount = FusionPlayerCountUtility.ResolveCurrentPlayerCount(runner, fallbackPlayerCount);
            int maxPlayers = FusionPlayerCountUtility.ResolveMaxPlayers(runner, fallbackMaxPlayers);

            if (context != null &&
                (string.IsNullOrWhiteSpace(sessionName) ||
                 string.Equals(context.SessionName, sessionName, StringComparison.Ordinal)))
            {
                context.PlayerCount = playerCount;
                context.MaxPlayers = maxPlayers;
            }

            FusionDebugLog.Info(
                FusionDebugFlow.Room,
                $"Player count updated. session={sessionName}, players={playerCount}/{maxPlayers}, isServer={runner.IsServer}, isClient={runner.IsClient}");

            OnPlayerCountChanged?.Invoke(playerCount, maxPlayers);
        }

        private bool CompletePendingJoinRequest(string requestId, bool approve)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return false;

            string normalizedRequestId = requestId.Trim();
            if (!_pendingJoinRequests.TryGetValue(normalizedRequestId, out PendingJoinRequest pending))
                return false;

            if (approve)
                pending.Request.Accept();
            else
                pending.Request.Refuse();

            RemovePendingJoinRequest(normalizedRequestId);
            FusionDebugLog.Info(FusionDebugFlow.Room, $"Join request {(approve ? "accepted" : "rejected")}. {pending.Info}");
            return true;
        }

        private void RejectExpiredJoinRequests()
        {
            if (_pendingJoinRequests.Count == 0)
                return;

            List<string> expiredIds = null;
            foreach (KeyValuePair<string, PendingJoinRequest> pair in _pendingJoinRequests)
            {
                if (Time.unscaledTime <= pair.Value.TimeoutAt)
                    continue;

                if (expiredIds == null)
                    expiredIds = new List<string>();

                expiredIds.Add(pair.Key);
            }

            if (expiredIds == null)
                return;

            for (int i = 0; i < expiredIds.Count; i++)
            {
                string requestId = expiredIds[i];
                if (!_pendingJoinRequests.TryGetValue(requestId, out PendingJoinRequest pending))
                    continue;

                pending.Request.Refuse();
                RemovePendingJoinRequest(requestId);
                FusionDebugLog.Warning(FusionDebugFlow.Room, $"Join request timed out and was rejected. {pending.Info}");
            }
        }

        private void RemovePendingJoinRequest(string requestId)
        {
            _pendingJoinRequests.Remove(requestId);
            for (int i = _pendingJoinRequestInfos.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_pendingJoinRequestInfos[i].RequestId, requestId, StringComparison.Ordinal))
                    _pendingJoinRequestInfos.RemoveAt(i);
            }

            OnPendingJoinRequestsChanged?.Invoke(_pendingJoinRequestInfos);
        }

        private void ClearPendingJoinRequests()
        {
            _pendingJoinRequests.Clear();
            _pendingJoinRequestInfos.Clear();
            OnPendingJoinRequestsChanged?.Invoke(_pendingJoinRequestInfos);
        }

        private void LogJoinDecision(
            string action,
            NetworkRunnerCallbackArgs.ConnectRequest request,
            FusionConnectionTokenPayload payload,
            byte[] token)
        {
            string userId = payload != null ? Normalize(payload.userId) : string.Empty;
            string displayName = payload != null ? Normalize(payload.displayName) : string.Empty;
            int tokenBytes = token != null ? token.Length : 0;
            FusionDebugLog.Info(
                FusionDebugFlow.Room,
                $"Join request {action}. remote={request.RemoteAddress}, user={userId}, name={displayName}, tokenBytes={tokenBytes}");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private readonly struct PendingJoinRequest
        {
            public PendingJoinRequest(
                NetworkRunnerCallbackArgs.ConnectRequest request,
                FusionPendingJoinRequestInfo info,
                float timeoutAt)
            {
                Request = request;
                Info = info;
                TimeoutAt = timeoutAt;
            }

            public NetworkRunnerCallbackArgs.ConnectRequest Request { get; }
            public FusionPendingJoinRequestInfo Info { get; }
            public float TimeoutAt { get; }
        }
    }
}
