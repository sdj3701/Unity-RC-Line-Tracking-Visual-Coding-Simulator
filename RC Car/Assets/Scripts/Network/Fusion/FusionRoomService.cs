using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Auth;
using Auth.Models;
using Fusion;
using Fusion.Photon.Realtime;
using RC.App.Defines;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RC.Network.Fusion
{
    [DisallowMultipleComponent]
    public sealed class FusionRoomService : MonoBehaviour
    {
        public static FusionRoomService Instance { get; private set; }

        [Header("Room Defaults")]
        [SerializeField] private string _sessionNamePrefix = "rc";
        [SerializeField] private string _defaultMode = "NetworkCar";
        [SerializeField] private bool _hostSessionOpen = true;
        [SerializeField] private bool _hostSessionVisible = true;
        [SerializeField] private bool _practiceAllowed = true;
        [SerializeField] private bool _clientCanCreateSession = false;

        [Header("Scene")]
        [SerializeField] private bool _loadNetworkSceneOnSuccess = true;
        [SerializeField] private string _networkSceneName = AppScenes.NetworkCarTest;

        [Header("Debug")]
        [SerializeField] private bool _debugLog = true;

        private bool _isBusy;

        public bool IsBusy => _isBusy;
        public StartGameResult LastStartResult { get; private set; }
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

        public static FusionRoomService GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var obj = new GameObject("FusionRoomService");
            return obj.AddComponent<FusionRoomService>();
        }

        public Task<bool> CreateRoomAsync(string roomName, int maxPlayers)
        {
            return CreateRoomAsync(roomName, maxPlayers, null, _networkSceneName, _loadNetworkSceneOnSuccess);
        }

        public async Task<bool> CreateRoomAsync(string roomName, int maxPlayers, string targetSceneName, bool loadSceneOnSuccess)
        {
            return await CreateRoomAsync(roomName, maxPlayers, null, targetSceneName, loadSceneOnSuccess);
        }

        public Task<bool> CreateRoomAsync(string roomName, int maxPlayers, string apiRoomId)
        {
            return CreateRoomAsync(roomName, maxPlayers, apiRoomId, _networkSceneName, _loadNetworkSceneOnSuccess);
        }

        public async Task<bool> CreateRoomAsync(
            string roomName,
            int maxPlayers,
            string apiRoomId,
            string targetSceneName,
            bool loadSceneOnSuccess)
        {
            if (_isBusy)
            {
                SetError("Photon room operation is already in progress.", warning: true);
                return false;
            }

            string normalizedRoomName = Normalize(roomName);
            if (string.IsNullOrWhiteSpace(normalizedRoomName))
            {
                SetError("Photon room name is empty.", warning: true);
                return false;
            }

            int resolvedMaxPlayers = Mathf.Max(1, maxPlayers);
            string sessionName = GenerateSessionName();

            _isBusy = true;
            LastErrorMessage = null;
            Log($"Photon room create started. room={normalizedRoomName}, session={sessionName}, maxPlayers={resolvedMaxPlayers}");

            try
            {
                FusionConnectionManager connection = FusionConnectionManager.GetOrCreate();
                if (!await EnsurePhotonReadyAsync(connection))
                    return false;

                NetworkRunner runner = connection.EnsureRunner();
                NetworkSceneManagerDefault sceneManager = connection.EnsureSceneManager();
                connection.RegisterCallbacksOnRunner(runner);
                if (!TryCreateAuthValues(connection, sessionName, out AuthenticationValues authValues))
                    return false;

                StartGameArgs args = new StartGameArgs
                {
                    GameMode = GameMode.Host,
                    SessionName = sessionName,
                    PlayerCount = resolvedMaxPlayers,
                    IsOpen = _hostSessionOpen,
                    IsVisible = _hostSessionVisible,
                    SessionProperties = BuildSessionProperties(normalizedRoomName, apiRoomId),
                    AuthValues = authValues,
                    SceneManager = sceneManager
                };

                LastStartResult = await runner.StartGame(args);
                if (!LastStartResult.Ok)
                {
                    SetError($"Photon room create failed. reason={LastStartResult.ShutdownReason}, message={LastStartResult.ErrorMessage}");
                    return false;
                }

                connection.MarkGameSessionStarted();
                FusionRoomSessionContext.Set(BuildContext(
                    GameMode.Host,
                    sessionName,
                    apiRoomId,
                    normalizedRoomName,
                    ResolveCurrentPlayerCount(runner, fallback: 1),
                    ResolveMaxPlayers(runner, resolvedMaxPlayers)));
                Log($"Photon room created. {FusionRoomSessionContext.Current}");
                LoadTargetSceneIfNeeded(targetSceneName, loadSceneOnSuccess);
                return true;
            }
            catch (Exception e)
            {
                SetError($"Photon room create exception: {e.Message}");
                return false;
            }
            finally
            {
                _isBusy = false;
            }
        }

        public Task<bool> JoinRoomAsync(string sessionName)
        {
            return JoinRoomAsync(sessionName, null, _networkSceneName, _loadNetworkSceneOnSuccess);
        }

        public async Task<bool> JoinRoomAsync(string sessionName, FusionRoomInfo roomInfo, string targetSceneName, bool loadSceneOnSuccess)
        {
            if (_isBusy)
            {
                SetError("Photon room operation is already in progress.", warning: true);
                return false;
            }

            string normalizedSessionName = Normalize(sessionName);
            if (string.IsNullOrWhiteSpace(normalizedSessionName))
            {
                SetError("Photon session name is empty.", warning: true);
                return false;
            }

            _isBusy = true;
            LastErrorMessage = null;
            Log($"Photon room join started. session={normalizedSessionName}");

            try
            {
                FusionConnectionManager connection = FusionConnectionManager.GetOrCreate();
                if (!await EnsurePhotonReadyAsync(connection))
                    return false;

                NetworkRunner runner = connection.EnsureRunner();
                NetworkSceneManagerDefault sceneManager = connection.EnsureSceneManager();
                connection.RegisterCallbacksOnRunner(runner);
                if (!TryCreateAuthValues(connection, normalizedSessionName, out AuthenticationValues authValues))
                    return false;

                StartGameArgs args = new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = normalizedSessionName,
                    EnableClientSessionCreation = _clientCanCreateSession,
                    ConnectionToken = FusionConnectionTokenUtility.CreateForCurrentUser(normalizedSessionName),
                    AuthValues = authValues,
                    SceneManager = sceneManager
                };

                LastStartResult = await runner.StartGame(args);
                if (!LastStartResult.Ok)
                {
                    SetError($"Photon room join failed. reason={LastStartResult.ShutdownReason}, message={LastStartResult.ErrorMessage}");
                    return false;
                }

                connection.MarkGameSessionStarted();
                FusionRoomInfo resolvedRoom = roomInfo ?? ResolveRoomInfo(normalizedSessionName);
                FusionRoomSessionContext.Set(BuildContext(
                    GameMode.Client,
                    normalizedSessionName,
                    resolvedRoom != null ? resolvedRoom.ApiRoomId : string.Empty,
                    resolvedRoom != null ? resolvedRoom.RoomName : normalizedSessionName,
                    ResolveCurrentPlayerCount(runner, resolvedRoom != null ? resolvedRoom.PlayerCount : 1),
                    ResolveMaxPlayers(runner, resolvedRoom != null ? resolvedRoom.MaxPlayers : 0),
                    resolvedRoom));

                Log($"Photon room joined. {FusionRoomSessionContext.Current}");
                LoadTargetSceneIfNeeded(targetSceneName, loadSceneOnSuccess);
                return true;
            }
            catch (Exception e)
            {
                SetError($"Photon room join exception: {e.Message}");
                return false;
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async Task<bool> EnsurePhotonReadyAsync(FusionConnectionManager connection)
        {
            if (connection == null)
            {
                SetError("FusionConnectionManager is null.");
                return false;
            }

            if (connection.IsInGameSession)
            {
                SetError("Already in a Photon game session. Leave the current room before starting another.", warning: true);
                return false;
            }

            if (connection.IsInSessionLobby)
                return true;

            Log("Photon lobby is not connected. Connecting before room operation.");
            return await connection.ConnectToPhotonLobbyAsync();
        }

        private bool TryCreateAuthValues(FusionConnectionManager connection, string sessionName, out AuthenticationValues authValues)
        {
            if (!FusionAuthFactory.TryCreateFromAuthManager(
                    out authValues,
                    out string errorMessage,
                    sessionName,
                    usePostData: connection != null && connection.UseAuthPostData,
                    authMode: connection != null ? connection.AuthMode : FusionAuthMode.UserIdOnly))
            {
                SetError(errorMessage);
                return false;
            }

            return true;
        }

        private Dictionary<string, SessionProperty> BuildSessionProperties(string roomName, string apiRoomId)
        {
            string userId = string.Empty;
            string hostName = string.Empty;

            AuthManager authManager = AuthManager.Instance;
            UserInfo currentUser = authManager != null ? authManager.CurrentUser : null;
            if (currentUser != null)
            {
                userId = Normalize(currentUser.userId);
                hostName = Normalize(currentUser.name);
            }

            return new Dictionary<string, SessionProperty>
            {
                { FusionLobbyService.RoomNameProperty, roomName },
                { FusionLobbyService.ApiRoomIdProperty, Normalize(apiRoomId) },
                { FusionLobbyService.HostUserIdProperty, userId },
                { FusionLobbyService.HostNameProperty, hostName },
                { FusionLobbyService.ModeProperty, Normalize(_defaultMode) },
                { FusionLobbyService.CreatedAtProperty, DateTime.UtcNow.ToString("o") },
                { FusionLobbyService.PracticeAllowedProperty, _practiceAllowed }
            };
        }

        private FusionRoomSessionInfo BuildContext(
            GameMode gameMode,
            string sessionName,
            string apiRoomId,
            string roomName,
            int playerCount,
            int maxPlayers,
            FusionRoomInfo roomInfo = null)
        {
            return new FusionRoomSessionInfo
            {
                SessionName = sessionName,
                ApiRoomId = roomInfo != null && !string.IsNullOrWhiteSpace(roomInfo.ApiRoomId)
                    ? roomInfo.ApiRoomId
                    : Normalize(apiRoomId),
                RoomName = roomName,
                HostUserId = roomInfo != null ? roomInfo.HostUserId : GetCurrentUserId(),
                HostName = roomInfo != null ? roomInfo.HostName : GetCurrentUserName(),
                GameMode = gameMode,
                IsHost = gameMode == GameMode.Host,
                PlayerCount = playerCount,
                MaxPlayers = maxPlayers
            };
        }

        private FusionRoomInfo ResolveRoomInfo(string sessionName)
        {
            FusionLobbyService lobbyService = FusionLobbyService.GetOrCreate();
            lobbyService.RefreshFromConnectionManager();

            return lobbyService.TryGetRoom(sessionName, out FusionRoomInfo roomInfo)
                ? roomInfo
                : null;
        }

        private static int ResolveCurrentPlayerCount(NetworkRunner runner, int fallback)
        {
            return FusionPlayerCountUtility.ResolveCurrentPlayerCount(runner, fallback);
        }

        private static int ResolveMaxPlayers(NetworkRunner runner, int fallback)
        {
            return FusionPlayerCountUtility.ResolveMaxPlayers(runner, fallback);
        }

        private void LoadTargetSceneIfNeeded(string targetSceneName, bool loadSceneOnSuccess)
        {
            if (!loadSceneOnSuccess)
                return;

            string sceneName = string.IsNullOrWhiteSpace(targetSceneName)
                ? _networkSceneName
                : targetSceneName.Trim();

            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            FusionDebugLog.Info(FusionDebugFlow.Scene, $"Loading network scene. scene={sceneName}");
            SceneManager.LoadScene(sceneName);
        }

        private string GenerateSessionName()
        {
            string prefix = string.IsNullOrWhiteSpace(_sessionNamePrefix)
                ? "rc"
                : _sessionNamePrefix.Trim();

            string token = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"{prefix}-{token}";
        }

        private void Log(string message)
        {
            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Room, message);
        }

        private void SetError(string message, bool warning = false)
        {
            LastErrorMessage = message ?? string.Empty;

            if (!_debugLog)
                return;

            if (warning)
                FusionDebugLog.Warning(FusionDebugFlow.Room, LastErrorMessage);
            else
                FusionDebugLog.Error(FusionDebugFlow.Room, LastErrorMessage);
        }

        private static string GetCurrentUserId()
        {
            AuthManager authManager = AuthManager.Instance;
            return authManager != null && authManager.CurrentUser != null
                ? Normalize(authManager.CurrentUser.userId)
                : string.Empty;
        }

        private static string GetCurrentUserName()
        {
            AuthManager authManager = AuthManager.Instance;
            return authManager != null && authManager.CurrentUser != null
                ? Normalize(authManager.CurrentUser.name)
                : string.Empty;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
