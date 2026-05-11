using System;
using System.Collections.Generic;
using Auth;
using Auth.Models;
using Fusion;
using RC.Network.Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkChatManager : NetworkBehaviour
{
    [Header("Message Limits")]
    [SerializeField, Min(1)] private int _maxMessageLength = 200;
    [SerializeField, Min(0f)] private float _localSendCooldownSeconds = 0.25f;
    [SerializeField, Min(0f)] private float _authorityAcceptCooldownSeconds = 0.15f;
    [SerializeField, Min(0)] private int _localHistoryLimit = 100;

    [Header("Identity")]
    [SerializeField] private string _localDisplayNameOverride = string.Empty;
    [SerializeField] private bool _preferAuthUserNameForLocalPlayer = true;
    [SerializeField] private bool _fallbackToPhotonUserId = true;

    [Header("Debug")]
    [SerializeField] private bool _debugLog;

    private readonly List<NetworkChatMessage> _localHistory = new List<NetworkChatMessage>();
    private readonly Dictionary<PlayerRef, float> _nextAcceptTimeBySender = new Dictionary<PlayerRef, float>();
    private float _nextLocalSendTime;

    private const int RpcMessageCharacterLimit = 64;
    private const int RpcSenderNameCharacterLimit = 32;

    public static NetworkChatManager Instance { get; private set; }

    public IReadOnlyList<NetworkChatMessage> LocalHistory => _localHistory;
    public int MaxMessageLength => Mathf.Clamp(_maxMessageLength, 1, RpcMessageCharacterLimit);
    public NetworkRunner ActiveRunner
    {
        get
        {
            TryResolveReadyRunner(out NetworkRunner runner);
            return runner;
        }
    }

    public bool IsReady => TryResolveReadyRunner(out _);

    public event Action<NetworkChatMessage> OnMessageReceived;
    public event Action<string> OnStatusChanged;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void Spawned()
    {
        Instance = this;
        SetStatus("Network chat manager spawned.");
    }

    public bool TrySendMessage(string message, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!TryResolveReadyRunner(out NetworkRunner runner))
        {
            errorMessage = "Network chat is not ready.";
            SetStatus(errorMessage, warning: true);
            return false;
        }

        string normalized = NormalizeMessage(message, MaxMessageLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            errorMessage = "Message is empty.";
            return false;
        }

        if (_localSendCooldownSeconds > 0f && Time.unscaledTime < _nextLocalSendTime)
        {
            errorMessage = "Message send is cooling down.";
            return false;
        }

        _nextLocalSendTime = Time.unscaledTime + Mathf.Max(0f, _localSendCooldownSeconds);

        if (Object != null && Runner != null && Runner.IsRunning && !Runner.IsShutdown)
        {
            LogDebug("Sending chat through spawned NetworkObject RPC.");
            RPC_SendChatMessage(normalized);
        }
        else
        {
            LogDebug("Sending chat through runner static RPC fallback.");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string senderName = TrimForNetwork(ResolveLocalDisplayName(), RpcSenderNameCharacterLimit);
            RPC_BroadcastChatMessage(runner, runner.LocalPlayer, senderName, normalized, now);
        }

        return true;
    }

    public void SendMessageFromUI(string message)
    {
        TrySendMessage(message, out _);
    }

    public void ClearLocalHistory()
    {
        _localHistory.Clear();
    }

    public string ResolveLocalDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(_localDisplayNameOverride))
            return TrimForNetwork(_localDisplayNameOverride, 64);

        if (_preferAuthUserNameForLocalPlayer)
        {
            UserInfo currentUser = AuthManager.Instance != null ? AuthManager.Instance.CurrentUser : null;
            if (currentUser != null)
            {
                string authName = FirstNonEmpty(currentUser.name, currentUser.username, currentUser.userId);
                if (!string.IsNullOrWhiteSpace(authName))
                    return TrimForNetwork(authName, 64);
            }
        }

        NetworkRunner runner = ActiveRunner;
        if (_fallbackToPhotonUserId && runner != null)
        {
            string userId = runner.GetPlayerUserId(runner.LocalPlayer);
            if (!string.IsNullOrWhiteSpace(userId))
                return TrimForNetwork(userId, 64);
        }

        return runner != null ? runner.LocalPlayer.ToString() : "Player";
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SendChatMessage(NetworkString<_64> message, RpcInfo info = default)
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        PlayerRef sender = info.Source;
        if (!CanAcceptFromSender(sender))
            return;

        string normalized = NormalizeMessage(message.ToString(), MaxMessageLength);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        string senderName = TrimForNetwork(ResolveSenderName(sender), RpcSenderNameCharacterLimit);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        RPC_ReceiveChatMessage(sender, senderName, normalized, now);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ReceiveChatMessage(
        PlayerRef sender,
        NetworkString<_32> senderName,
        NetworkString<_64> message,
        long unixTimeMilliseconds,
        RpcInfo info = default)
    {
        ReceiveChatMessage(sender, senderName.ToString(), message.ToString(), unixTimeMilliseconds);
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    private static void RPC_BroadcastChatMessage(
        NetworkRunner runner,
        PlayerRef sender,
        NetworkString<_32> senderName,
        NetworkString<_64> message,
        long unixTimeMilliseconds,
        RpcInfo info = default)
    {
        NetworkChatManager manager = Instance;
        if (manager == null)
            manager = FindObjectOfType<NetworkChatManager>(true);

        if (manager == null)
        {
            Debug.LogWarning("[NetworkChatManager] Static chat RPC received, but no NetworkChatManager exists in the scene.");
            return;
        }

        manager.ReceiveChatMessage(sender, senderName.ToString(), message.ToString(), unixTimeMilliseconds);
    }

    private void ReceiveChatMessage(PlayerRef sender, string senderName, string message, long unixTimeMilliseconds)
    {
        string text = NormalizeMessage(message, MaxMessageLength);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var chatMessage = new NetworkChatMessage(
            sender,
            TrimForNetwork(senderName, 64),
            text,
            unixTimeMilliseconds);

        AddToLocalHistory(chatMessage);
        OnMessageReceived?.Invoke(chatMessage);

        if (_debugLog)
            Debug.Log($"[NetworkChatManager] received sender={sender}, name={chatMessage.SenderName}, text={chatMessage.Message}");
    }

    private bool CanAcceptFromSender(PlayerRef sender)
    {
        if (_authorityAcceptCooldownSeconds <= 0f)
            return true;

        float now = Time.unscaledTime;
        if (_nextAcceptTimeBySender.TryGetValue(sender, out float nextAllowedAt) && now < nextAllowedAt)
        {
            if (_debugLog)
                Debug.Log($"[NetworkChatManager] throttled sender={sender}");

            return false;
        }

        _nextAcceptTimeBySender[sender] = now + Mathf.Max(0f, _authorityAcceptCooldownSeconds);
        return true;
    }

    private string ResolveSenderName(PlayerRef sender)
    {
        NetworkRunner runner = ActiveRunner;
        if (runner != null)
        {
            if (sender == runner.LocalPlayer)
            {
                string localDisplayName = ResolveLocalDisplayName();
                if (!string.IsNullOrWhiteSpace(localDisplayName))
                    return localDisplayName;
            }

            if (_fallbackToPhotonUserId)
            {
                string userId = runner.GetPlayerUserId(sender);
                if (!string.IsNullOrWhiteSpace(userId))
                    return TrimForNetwork(userId, 64);
            }
        }

        return sender.ToString();
    }

    private void AddToLocalHistory(NetworkChatMessage message)
    {
        int limit = Mathf.Max(0, _localHistoryLimit);
        if (limit == 0)
            return;

        _localHistory.Add(message);
        while (_localHistory.Count > limit)
            _localHistory.RemoveAt(0);
    }

    private void SetStatus(string message, bool warning = false)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        OnStatusChanged?.Invoke(text);

        if (!_debugLog || string.IsNullOrWhiteSpace(text))
            return;

        if (warning)
            Debug.LogWarning($"[NetworkChatManager] {text}");
        else
            Debug.Log($"[NetworkChatManager] {text}");
    }

    private bool TryResolveReadyRunner(out NetworkRunner runner)
    {
        runner = null;

        if (Runner != null && Runner.IsRunning && !Runner.IsShutdown)
        {
            runner = Runner;
            return true;
        }

        FusionConnectionManager connectionManager = FusionConnectionManager.Instance;
        if (connectionManager != null &&
            connectionManager.Runner != null &&
            connectionManager.Runner.IsRunning &&
            !connectionManager.Runner.IsShutdown)
        {
            runner = connectionManager.Runner;
            return true;
        }

        FusionNetworkBootstrap bootstrap = FindObjectOfType<FusionNetworkBootstrap>(true);
        if (bootstrap != null &&
            bootstrap.Runner != null &&
            bootstrap.Runner.IsRunning &&
            !bootstrap.Runner.IsShutdown)
        {
            runner = bootstrap.Runner;
            return true;
        }

        return false;
    }

    private void LogDebug(string message)
    {
        if (_debugLog && !string.IsNullOrWhiteSpace(message))
            Debug.Log($"[NetworkChatManager] {message}");
    }

    private static string NormalizeMessage(string value, int maxLength)
    {
        string text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        int limit = Mathf.Max(1, maxLength);
        return text.Length <= limit ? text : text.Substring(0, limit);
    }

    private static string TrimForNetwork(string value, int maxLength)
    {
        string text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        int limit = Mathf.Max(1, maxLength);
        return text.Length <= limit ? text : text.Substring(0, limit);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return string.Empty;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i].Trim();
        }

        return string.Empty;
    }
}
