using System;
using Fusion;
using UnityEngine;

namespace RC.Network.Fusion
{
    public static class FusionRoomSessionContext
    {
        public static FusionRoomSessionInfo Current { get; private set; }

        public static void Set(FusionRoomSessionInfo sessionInfo)
        {
            Current = sessionInfo;
        }

        public static FusionRoomSessionInfo CloneCurrent()
        {
            return Current != null ? Current.Clone() : null;
        }

        public static void UpdateFromRunner(NetworkRunner runner, FusionRoomSessionInfo seed = null)
        {
            if (runner == null || runner.SessionInfo == null || !runner.SessionInfo.IsValid)
                return;

            FusionRoomSessionInfo next = seed != null
                ? seed.Clone()
                : (Current != null ? Current.Clone() : new FusionRoomSessionInfo());

            next.SessionName = Normalize(runner.SessionInfo.Name);
            next.ApiRoomId = FusionLobbyService.GetStringProperty(
                runner.SessionInfo.Properties,
                FusionLobbyService.ApiRoomIdProperty,
                next.ApiRoomId);
            next.RoomName = FusionLobbyService.GetStringProperty(
                runner.SessionInfo.Properties,
                FusionLobbyService.RoomNameProperty,
                string.IsNullOrWhiteSpace(next.RoomName) ? next.SessionName : next.RoomName);
            next.GameMode = runner.GameMode;
            next.IsHost = runner.IsServer;
            next.PlayerCount = FusionPlayerCountUtility.ResolveCurrentPlayerCount(runner, next.IsHost ? 1 : next.PlayerCount);
            next.MaxPlayers = FusionPlayerCountUtility.ResolveMaxPlayers(runner, next.MaxPlayers);

            if (next.IsHost && string.IsNullOrWhiteSpace(next.HostUserId))
                next.HostUserId = Normalize(runner.UserId);

            Set(next);
        }

        public static void Clear()
        {
            Current = null;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class FusionRoomSessionInfo
    {
        public string SessionName { get; set; }
        public string ApiRoomId { get; set; }
        public string RoomName { get; set; }
        public string HostUserId { get; set; }
        public string HostName { get; set; }
        public GameMode GameMode { get; set; }
        public bool IsHost { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }

        public override string ToString()
        {
            return $"session={SessionName}, apiRoomId={ApiRoomId}, room={RoomName}, mode={GameMode}, isHost={IsHost}, host={HostUserId}, players={PlayerCount}/{MaxPlayers}";
        }

        public FusionRoomSessionInfo Clone()
        {
            return new FusionRoomSessionInfo
            {
                SessionName = SessionName,
                ApiRoomId = ApiRoomId,
                RoomName = RoomName,
                HostUserId = HostUserId,
                HostName = HostName,
                GameMode = GameMode,
                IsHost = IsHost,
                PlayerCount = PlayerCount,
                MaxPlayers = MaxPlayers
            };
        }
    }

    internal static class FusionPlayerCountUtility
    {
        public static int ResolveCurrentPlayerCount(NetworkRunner runner, int fallback = 0)
        {
            int resolvedFallback = Mathf.Max(0, fallback);
            if (runner == null || runner.IsShutdown)
                return resolvedFallback;

            int activePlayerCount = CountActivePlayers(runner);
            if (activePlayerCount > 0)
                return activePlayerCount;

            if (runner.SessionInfo != null && runner.SessionInfo.IsValid && runner.SessionInfo.PlayerCount > 0)
                return runner.SessionInfo.PlayerCount;

            if (runner.IsRunning && (runner.IsServer || runner.IsClient))
                return Mathf.Max(1, resolvedFallback);

            return resolvedFallback;
        }

        public static int ResolveMaxPlayers(NetworkRunner runner, int fallback = 0)
        {
            if (runner != null && runner.SessionInfo != null && runner.SessionInfo.IsValid && runner.SessionInfo.MaxPlayers > 0)
                return runner.SessionInfo.MaxPlayers;

            return Mathf.Max(0, fallback);
        }

        private static int CountActivePlayers(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning || runner.IsShutdown)
                return 0;

            int count = 0;
            foreach (PlayerRef _ in runner.ActivePlayers)
                count++;

            return count;
        }
    }
}
