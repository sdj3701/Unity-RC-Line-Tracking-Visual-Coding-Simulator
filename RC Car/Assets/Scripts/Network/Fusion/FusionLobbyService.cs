using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace RC.Network.Fusion
{
    [DisallowMultipleComponent]
    public sealed class FusionLobbyService : MonoBehaviour
    {
        public const string RoomNameProperty = "roomName";
        public const string HostUserIdProperty = "hostUserId";
        public const string HostNameProperty = "hostName";
        public const string ModeProperty = "mode";
        public const string CreatedAtProperty = "createdAt";
        public const string PracticeAllowedProperty = "isPracticeAllowed";

        public static FusionLobbyService Instance { get; private set; }

        [SerializeField] private bool _debugLog = true;

        private readonly List<FusionRoomInfo> _rooms = new List<FusionRoomInfo>();
        private FusionConnectionManager _connectionManager;

        public event Action<IReadOnlyList<FusionRoomInfo>> OnRoomsUpdated;

        public IReadOnlyList<FusionRoomInfo> Rooms => _rooms;

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

        private void OnEnable()
        {
            BindConnectionManager();
        }

        private void OnDisable()
        {
            UnbindConnectionManager();
        }

        public static FusionLobbyService GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var obj = new GameObject("FusionLobbyService");
            return obj.AddComponent<FusionLobbyService>();
        }

        public void RefreshFromConnectionManager()
        {
            BindConnectionManager();

            IReadOnlyList<SessionInfo> sessions = _connectionManager != null
                ? _connectionManager.SessionInfos
                : null;

            RebuildRooms(sessions);
        }

        public bool TryGetRoom(string sessionName, out FusionRoomInfo roomInfo)
        {
            roomInfo = null;
            if (string.IsNullOrWhiteSpace(sessionName))
                return false;

            string normalized = sessionName.Trim();
            for (int i = 0; i < _rooms.Count; i++)
            {
                if (string.Equals(_rooms[i].SessionName, normalized, StringComparison.Ordinal))
                {
                    roomInfo = _rooms[i];
                    return true;
                }
            }

            return false;
        }

        private void BindConnectionManager()
        {
            FusionConnectionManager manager = FusionConnectionManager.GetOrCreate();
            if (_connectionManager == manager)
                return;

            UnbindConnectionManager();
            _connectionManager = manager;
            _connectionManager.OnSessionListChanged += HandleSessionListChanged;
        }

        private void UnbindConnectionManager()
        {
            if (_connectionManager == null)
                return;

            _connectionManager.OnSessionListChanged -= HandleSessionListChanged;
            _connectionManager = null;
        }

        private void HandleSessionListChanged(IReadOnlyList<SessionInfo> sessionList)
        {
            RebuildRooms(sessionList);
        }

        private void RebuildRooms(IReadOnlyList<SessionInfo> sessionList)
        {
            _rooms.Clear();

            if (sessionList != null)
            {
                for (int i = 0; i < sessionList.Count; i++)
                {
                    FusionRoomInfo room = ConvertSessionInfo(sessionList[i]);
                    if (room != null && room.IsValid && !string.IsNullOrWhiteSpace(room.SessionName))
                        _rooms.Add(room);
                }
            }

            if (_debugLog)
                FusionDebugLog.Info(FusionDebugFlow.Lobby, $"Photon room cache rebuilt. count={_rooms.Count}");

            OnRoomsUpdated?.Invoke(_rooms);
        }

        public static FusionRoomInfo ConvertSessionInfo(SessionInfo sessionInfo)
        {
            if (sessionInfo == null)
                return null;

            IReadOnlyDictionary<string, SessionProperty> properties = sessionInfo.Properties;
            string sessionName = Normalize(sessionInfo.Name);

            return new FusionRoomInfo
            {
                SessionName = sessionName,
                RoomName = GetStringProperty(properties, RoomNameProperty, sessionName),
                HostUserId = GetStringProperty(properties, HostUserIdProperty, string.Empty),
                HostName = GetStringProperty(properties, HostNameProperty, string.Empty),
                Mode = GetStringProperty(properties, ModeProperty, string.Empty),
                CreatedAtUtc = GetStringProperty(properties, CreatedAtProperty, string.Empty),
                PlayerCount = sessionInfo.PlayerCount,
                MaxPlayers = sessionInfo.MaxPlayers,
                IsOpen = sessionInfo.IsOpen,
                IsVisible = sessionInfo.IsVisible,
                IsValid = sessionInfo.IsValid
            };
        }

        public static string GetStringProperty(IReadOnlyDictionary<string, SessionProperty> properties, string key, string fallback)
        {
            if (properties == null || string.IsNullOrWhiteSpace(key))
                return fallback;

            if (!properties.TryGetValue(key, out SessionProperty property))
                return fallback;

            if (property.IsString)
            {
                string value = property;
                return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            }

            return property.ToString();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
