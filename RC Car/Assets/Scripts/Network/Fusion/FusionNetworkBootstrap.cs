using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;

namespace RC.Network.Fusion
{
    public enum FusionBootstrapStartMode
    {
        AutoFromRoomSession,
        Host,
        Client
    }

    /// <summary>
    /// Binds the NetworkCar scene to the already-started Photon Fusion runner and
    /// provides a fallback start path when a session context exists but the runner
    /// was not preserved across the scene change.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FusionNetworkBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Startup")]
        [SerializeField] private bool _startOnEnable = true;
        [SerializeField] private FusionBootstrapStartMode _startMode = FusionBootstrapStartMode.AutoFromRoomSession;
        [SerializeField] private string _sessionNameOverride = string.Empty;
        [SerializeField] private int _playerCount = 0;
        [SerializeField] private bool _provideInput = true;
        [SerializeField] private bool _useAuthPostData = false;

        [Header("Session")]
        [SerializeField] private bool _hostSessionOpen = true;
        [SerializeField] private bool _hostSessionVisible = false;
        [SerializeField] private bool _clientCanCreateSession = false;

        [Header("Runner")]
        [SerializeField] private NetworkRunner _runnerOverride;
        [SerializeField] private NetworkRunner _runnerPrefab;
        [SerializeField] private bool _dontDestroyRunnerOnLoad = true;
        [SerializeField] private bool _destroyOwnedRunnerOnShutdown = true;
        [SerializeField] private bool _shutdownOnDisable = false;

        [Header("Connect Requests")]
        [SerializeField] private bool _autoAcceptConnectRequests = true;

        [Header("Debug")]
        [SerializeField] private bool _debugLog = true;
        [SerializeField] private bool _logPlayerEvents = true;

        private NetworkRunner _runner;
        private bool _ownsRunnerObject;
        private bool _isStarting;
        private bool _isShuttingDown;

        public NetworkRunner Runner => _runner;
        public bool IsStarting => _isStarting;
        public StartGameResult LastStartResult { get; private set; }
        public string LastStatusMessage { get; private set; }

        public bool IsConnected =>
            _runner != null &&
            _runner.IsRunning &&
            !_runner.IsShutdown &&
            _runner.SessionInfo.IsValid;

        public event Action<string> OnStatusChanged;

        private void OnEnable()
        {
            if (_startOnEnable)
                StartConnection();
        }

        private void OnDisable()
        {
            if (_shutdownOnDisable)
                Shutdown();
        }

        private void OnDestroy()
        {
            if (_runner != null)
                _runner.RemoveCallbacks(this);
        }

        public void StartConnection()
        {
            _ = StartConnectionAsync();
        }

        public async Task<bool> StartConnectionAsync()
        {
            if (_isStarting)
            {
                SetStatus("Fusion start is already in progress.", warning: true);
                return false;
            }

            if (IsConnected)
            {
                SyncContextFromRunner(_runner);
                LogRunnerStatus("Already connected");
                return true;
            }

            if (TryUseExistingPhotonSession())
                return true;

            if (!TryBuildStartContext(out FusionStartContext context, out string errorMessage))
            {
                SetStatus(errorMessage, error: true);
                return false;
            }

            FusionConnectionManager connectionManager = FusionConnectionManager.GetOrCreate();
            if (!FusionAuthFactory.TryCreateFromAuthManager(
                    out AuthenticationValues authValues,
                    out errorMessage,
                    context.SessionName,
                    _useAuthPostData,
                    connectionManager.AuthMode))
            {
                SetStatus(errorMessage, error: true);
                return false;
            }

            _runner = EnsureRunner();
            if (_runner == null)
            {
                SetStatus("NetworkRunner could not be created.", error: true);
                return false;
            }

            _runner.ProvideInput = _provideInput;
            connectionManager.RegisterCallbacksOnRunner(_runner);
            _runner.RemoveCallbacks(this);
            _runner.AddCallbacks(this);

            var sceneManager = EnsureSceneManager(_runner);
            var args = new StartGameArgs
            {
                GameMode = context.GameMode,
                SessionName = context.SessionName,
                AuthValues = authValues,
                SceneManager = sceneManager
            };

            if (_playerCount > 0)
                args.PlayerCount = _playerCount;

            if (context.GameMode == GameMode.Host)
            {
                args.IsOpen = _hostSessionOpen;
                args.IsVisible = _hostSessionVisible;
            }
            else if (context.GameMode == GameMode.Client)
            {
                args.EnableClientSessionCreation = _clientCanCreateSession;
                args.ConnectionToken = FusionConnectionTokenUtility.CreateForCurrentUser(context.SessionName);
            }

            _isStarting = true;
            SetStatus($"Starting Fusion. mode={context.GameMode}, session={context.SessionName}");

            try
            {
                LastStartResult = await _runner.StartGame(args);
                if (!LastStartResult.Ok)
                {
                    SetStatus(
                        $"Fusion start failed. reason={LastStartResult.ShutdownReason}, message={LastStartResult.ErrorMessage}",
                        error: true);
                    return false;
                }

                connectionManager.MarkGameSessionStarted();
                SyncContextFromRunner(_runner, context.GameMode);
                LogRunnerStatus("Fusion started");
                return true;
            }
            catch (Exception e)
            {
                SetStatus($"Fusion start exception: {e.Message}", error: true);
                return false;
            }
            finally
            {
                _isStarting = false;
            }
        }

        private bool TryUseExistingPhotonSession()
        {
            FusionConnectionManager connectionManager = FusionConnectionManager.Instance;
            if (connectionManager == null)
                return false;

            NetworkRunner existingRunner = connectionManager.Runner;
            if (existingRunner == null || existingRunner.IsShutdown || !existingRunner.IsRunning || !existingRunner.SessionInfo.IsValid)
                return false;

            _runner = existingRunner;
            connectionManager.RegisterCallbacksOnRunner(existingRunner);
            _runner.RemoveCallbacks(this);
            _runner.AddCallbacks(this);

            SyncContextFromRunner(existingRunner);
            LogRunnerStatus("Using existing Photon game session");
            return true;
        }

        public void Shutdown()
        {
            _ = ShutdownAsync();
        }

        public async Task ShutdownAsync()
        {
            if (_runner == null || _isShuttingDown)
                return;

            _isShuttingDown = true;
            NetworkRunner runner = _runner;
            SetStatus("Fusion shutdown requested.");

            try
            {
                FusionConnectionManager connectionManager = FusionConnectionManager.Instance;
                if (connectionManager != null && ReferenceEquals(connectionManager.Runner, runner))
                {
                    runner.RemoveCallbacks(this);
                    await connectionManager.ShutdownAsync();
                    _runner = null;
                    return;
                }

                runner.RemoveCallbacks(this);
                if (runner.IsRunning && !runner.IsShutdown)
                    await runner.Shutdown(false, ShutdownReason.Ok, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FusionNetworkBootstrap] Shutdown failed: {e.Message}");
            }
            finally
            {
                CleanupRunnerReference(runner);
                _isShuttingDown = false;
            }
        }

        private bool TryBuildStartContext(out FusionStartContext context, out string errorMessage)
        {
            context = default;
            errorMessage = null;

            string sessionName = ResolveSessionName();
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                errorMessage = "Fusion session name is empty. Set FusionRoomSessionContext.Current or Session Name Override.";
                return false;
            }

            if (!TryResolveGameMode(out GameMode gameMode, out errorMessage))
                return false;

            context = new FusionStartContext(sessionName, gameMode);
            return true;
        }

        private string ResolveSessionName()
        {
            if (!string.IsNullOrWhiteSpace(_sessionNameOverride))
                return _sessionNameOverride.Trim();

            FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
            return context != null && !string.IsNullOrWhiteSpace(context.SessionName)
                ? context.SessionName.Trim()
                : string.Empty;
        }

        private bool TryResolveGameMode(out GameMode gameMode, out string errorMessage)
        {
            errorMessage = null;
            gameMode = GameMode.Client;

            if (_startMode == FusionBootstrapStartMode.Host)
            {
                gameMode = GameMode.Host;
                return true;
            }

            if (_startMode == FusionBootstrapStartMode.Client)
            {
                gameMode = GameMode.Client;
                return true;
            }

            FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
            if (context == null)
            {
                errorMessage = "FusionRoomSessionContext.Current is null. Use explicit Host/Client mode or start the room through FusionRoomService first.";
                return false;
            }

            if (context.GameMode == GameMode.Host || context.IsHost)
            {
                gameMode = GameMode.Host;
                return true;
            }

            gameMode = GameMode.Client;
            return true;
        }

        private NetworkRunner EnsureRunner()
        {
            if (_runner != null && !_runner.IsShutdown)
                return _runner;

            _ownsRunnerObject = false;

            if (_runnerOverride != null && !_runnerOverride.IsShutdown)
                return _runnerOverride;

            NetworkRunner existing = GetComponent<NetworkRunner>();
            if (existing != null && !existing.IsShutdown)
                return existing;

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

            return created;
        }

        private static NetworkSceneManagerDefault EnsureSceneManager(NetworkRunner runner)
        {
            if (runner == null)
                return null;

            NetworkSceneManagerDefault sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null)
                sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            return sceneManager;
        }

        private void CleanupRunnerReference(NetworkRunner runner)
        {
            if (runner == null)
                return;

            bool shouldDestroy = _ownsRunnerObject && _destroyOwnedRunnerOnShutdown && runner.gameObject != gameObject;
            if (ReferenceEquals(_runner, runner))
                _runner = null;

            if (shouldDestroy)
                Destroy(runner.gameObject);

            _ownsRunnerObject = false;
        }

        private void SyncContextFromRunner(NetworkRunner runner, GameMode? fallbackGameMode = null)
        {
            if (runner == null || runner.SessionInfo == null || !runner.SessionInfo.IsValid)
                return;

            FusionRoomSessionInfo seed = FusionRoomSessionContext.CloneCurrent() ?? new FusionRoomSessionInfo();
            seed.GameMode = fallbackGameMode ?? ResolveRunnerGameMode(runner, seed.GameMode);
            seed.IsHost = runner.IsServer;
            if (string.IsNullOrWhiteSpace(seed.SessionName))
                seed.SessionName = runner.SessionInfo.Name;
            if (string.IsNullOrWhiteSpace(seed.RoomName))
                seed.RoomName = seed.SessionName;

            FusionRoomSessionContext.UpdateFromRunner(runner, seed);
        }

        private static GameMode ResolveRunnerGameMode(NetworkRunner runner, GameMode fallback)
        {
            if (runner != null)
            {
                if (runner.IsServer)
                    return GameMode.Host;

                if (runner.IsClient)
                    return GameMode.Client;

                if (runner.GameMode != default)
                    return runner.GameMode;
            }

            return fallback;
        }

        private void SetStatus(string message, bool warning = false, bool error = false)
        {
            LastStatusMessage = message ?? string.Empty;
            OnStatusChanged?.Invoke(LastStatusMessage);

            if (!_debugLog)
                return;

            if (error)
                Debug.LogError($"[FusionNetworkBootstrap] {LastStatusMessage}");
            else if (warning)
                Debug.LogWarning($"[FusionNetworkBootstrap] {LastStatusMessage}");
            else
                Debug.Log($"[FusionNetworkBootstrap] {LastStatusMessage}");
        }

        private void LogRunnerStatus(string prefix)
        {
            if (_runner == null)
            {
                SetStatus($"{prefix}: runner is null", warning: true);
                return;
            }

            SetStatus(
                $"{prefix}: running={_runner.IsRunning}, shutdown={_runner.IsShutdown}, " +
                $"isServer={_runner.IsServer}, isClient={_runner.IsClient}, " +
                $"sessionValid={_runner.SessionInfo.IsValid}, session={_runner.SessionInfo.Name}, " +
                $"players={_runner.SessionInfo.PlayerCount}/{_runner.SessionInfo.MaxPlayers}, " +
                $"userId={_runner.UserId}, localPlayer={_runner.LocalPlayer}");
        }

        private void LogPlayerEvent(string message)
        {
            if (_debugLog && _logPlayerEvents)
                Debug.Log($"[FusionNetworkBootstrap] {message}");
        }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            SyncContextFromRunner(runner);
            LogPlayerEvent($"Player joined: {player}");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            SyncContextFromRunner(runner);
            LogPlayerEvent($"Player left: {player}");
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            SetStatus($"Fusion shutdown. reason={shutdownReason}", warning: shutdownReason != ShutdownReason.Ok);

            FusionConnectionManager connectionManager = FusionConnectionManager.Instance;
            if (connectionManager != null && ReferenceEquals(connectionManager.Runner, runner))
            {
                _runner = null;
                return;
            }

            CleanupRunnerReference(runner);
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            SetStatus($"Disconnected from Fusion server. reason={reason}", warning: true);
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            LogPlayerEvent(
                $"Connect request observed by scene bootstrap. remote={request.RemoteAddress}, tokenBytes={(token != null ? token.Length : 0)}");
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            SetStatus($"Fusion connect failed. remote={remoteAddress}, reason={reason}", error: true);
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
            SyncContextFromRunner(runner);
            LogRunnerStatus("Connected to Fusion server");
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            LogPlayerEvent($"Session list updated. count={(sessionList != null ? sessionList.Count : 0)}");
        }

        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
            string keys = data == null ? "(null)" : string.Join(", ", data.Keys);
            LogPlayerEvent($"Custom authentication response received. keys={keys}");
        }

        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
            SetStatus("Host migration token received. Host migration is not implemented yet.", warning: true);
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            SyncContextFromRunner(runner);
            LogPlayerEvent("Fusion scene load done.");
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
            LogPlayerEvent("Fusion scene load start.");
        }

        private readonly struct FusionStartContext
        {
            public FusionStartContext(string sessionName, GameMode gameMode)
            {
                SessionName = sessionName;
                GameMode = gameMode;
            }

            public string SessionName { get; }
            public GameMode GameMode { get; }
        }
    }
}
