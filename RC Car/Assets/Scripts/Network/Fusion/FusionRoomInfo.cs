namespace RC.Network.Fusion
{
    public sealed class FusionRoomInfo
    {
        public string SessionName { get; set; }
        public string ApiRoomId { get; set; }
        public string RoomName { get; set; }
        public string HostUserId { get; set; }
        public string HostName { get; set; }
        public string Mode { get; set; }
        public string CreatedAtUtc { get; set; }
        public int PlayerCount { get; set; }
        public int MaxPlayers { get; set; }
        public bool IsOpen { get; set; }
        public bool IsVisible { get; set; }
        public bool IsValid { get; set; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(RoomName) ? SessionName : RoomName;

        public override string ToString()
        {
            return $"{DisplayName} ({PlayerCount}/{MaxPlayers}) session={SessionName}, apiRoomId={ApiRoomId}, host={HostUserId}, open={IsOpen}, visible={IsVisible}";
        }
    }
}
