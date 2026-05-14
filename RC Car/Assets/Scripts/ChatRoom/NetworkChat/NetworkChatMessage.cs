using System;
using Fusion;

[Serializable]
public readonly struct NetworkChatMessage
{
    public NetworkChatMessage(PlayerRef sender, string senderName, string message, long unixTimeMilliseconds)
        : this(sender, senderName, senderName, message, unixTimeMilliseconds)
    {
    }

    public NetworkChatMessage(PlayerRef sender, string senderUserId, string senderName, string message, long unixTimeMilliseconds)
    {
        Sender = sender;
        SenderUserId = Normalize(senderUserId);
        SenderName = Normalize(senderName);
        Message = Normalize(message);
        UnixTimeMilliseconds = unixTimeMilliseconds;
    }

    public PlayerRef Sender { get; }
    public string SenderUserId { get; }
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

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
