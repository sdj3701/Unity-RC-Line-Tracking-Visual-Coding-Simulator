using System;
using Fusion;

[Serializable]
public readonly struct NetworkChatMessage
{
    public NetworkChatMessage(PlayerRef sender, string senderName, string message, long unixTimeMilliseconds)
    {
        Sender = sender;
        SenderName = string.IsNullOrWhiteSpace(senderName) ? string.Empty : senderName.Trim();
        Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        UnixTimeMilliseconds = unixTimeMilliseconds;
    }

    public PlayerRef Sender { get; }
    public string SenderName { get; }
    public string Message { get; }
    public long UnixTimeMilliseconds { get; }

    public DateTime LocalTime
    {
        get
        {
            if (UnixTimeMilliseconds <= 0)
                return DateTime.Now;

            return DateTimeOffset.FromUnixTimeMilliseconds(UnixTimeMilliseconds).LocalDateTime;
        }
    }

    public bool IsMine(NetworkRunner runner)
    {
        return runner != null && Sender == runner.LocalPlayer;
    }
}
