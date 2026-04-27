public sealed class FusionRoomIdProvider : IRoomIdProvider
{
    private readonly string _roomIdOverride;
    private readonly bool _autoRoomFromSession;

    public FusionRoomIdProvider(string roomIdOverride, bool autoRoomFromSession)
    {
        _roomIdOverride = roomIdOverride;
        _autoRoomFromSession = autoRoomFromSession;
    }

    public string GetRoomId()
    {
        if (!_autoRoomFromSession && string.IsNullOrWhiteSpace(_roomIdOverride))
            return string.Empty;

        return NetworkRoomIdentity.ResolveApiRoomId(_roomIdOverride);
    }
}
