using RC.Network.Fusion;

public static class RoomSessionContext
{
    public static RoomInfo CurrentRoom { get; private set; }

    public static void Set(RoomInfo roomInfo)
    {
        CurrentRoom = roomInfo;
    }

    public static void Clear()
    {
        CurrentRoom = null;
    }
}

public static class NetworkRoomIdentity
{
    public static string ResolveApiRoomId(string roomIdOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(roomIdOverride))
            return roomIdOverride.Trim();

        RoomInfo room = RoomSessionContext.CurrentRoom;
        if (room != null && !string.IsNullOrWhiteSpace(room.ApiRoomId))
            return room.ApiRoomId.Trim();

        FusionRoomSessionInfo fusionContext = FusionRoomSessionContext.Current;
        if (fusionContext != null && !string.IsNullOrWhiteSpace(fusionContext.ApiRoomId))
            return fusionContext.ApiRoomId.Trim();

        if (room != null && !string.IsNullOrWhiteSpace(room.RoomId))
            return room.RoomId.Trim();

        return string.Empty;
    }

    public static string ResolvePhotonSessionName(string sessionNameOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(sessionNameOverride))
            return sessionNameOverride.Trim();

        FusionRoomSessionInfo fusionContext = FusionRoomSessionContext.Current;
        if (fusionContext != null && !string.IsNullOrWhiteSpace(fusionContext.SessionName))
            return fusionContext.SessionName.Trim();

        RoomInfo room = RoomSessionContext.CurrentRoom;
        if (room != null && !string.IsNullOrWhiteSpace(room.PhotonSessionName))
            return room.PhotonSessionName.Trim();

        return string.Empty;
    }

    public static RoomInfo CreateRoomInfo(
        string apiRoomId,
        string photonSessionName,
        string roomName,
        string hostUserId,
        string createdAtUtc)
    {
        string normalizedApiRoomId = Normalize(apiRoomId);
        return new RoomInfo
        {
            RoomId = normalizedApiRoomId,
            ApiRoomId = normalizedApiRoomId,
            PhotonSessionName = Normalize(photonSessionName),
            RoomName = Normalize(roomName),
            HostUserId = Normalize(hostUserId),
            CreatedAtUtc = Normalize(createdAtUtc)
        };
    }

    public static void ApplyRoomContext(
        string apiRoomId,
        string photonSessionName,
        string roomName,
        string hostUserId,
        string createdAtUtc)
    {
        RoomSessionContext.Set(CreateRoomInfo(
            apiRoomId,
            photonSessionName,
            roomName,
            hostUserId,
            createdAtUtc));
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
