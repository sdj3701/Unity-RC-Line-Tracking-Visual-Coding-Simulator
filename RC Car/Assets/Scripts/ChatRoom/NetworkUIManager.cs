using System;
using Auth;
using UnityEngine;

public class NetworkUIManager : MonoBehaviour
{
    [SerializeField] private GameObject _hostUI;
    [SerializeField] private GameObject _clientUI;
    [SerializeField] private bool _treatUnknownAsClient = true;
    [SerializeField] private bool _debugLog = true;

    private enum UserRole
    {
        Unknown,
        Host,
        Client
    }

    private void OnEnable()
    {
        RefreshRoleUI();
    }

    public void RefreshRoleUI()
    {
        UserRole role = ResolveRole(out string reason);

        bool showHostUI = role == UserRole.Host;
        bool showClientUI = role == UserRole.Client ||
                            (role == UserRole.Unknown && _treatUnknownAsClient);

        if (_hostUI != null)
            _hostUI.SetActive(showHostUI);

        if (_clientUI != null)
            _clientUI.SetActive(showClientUI);

        if (_debugLog)
            Debug.Log($"[NetworkUIManager] role={role}, reason={reason}, hostUI={showHostUI}, clientUI={showClientUI}");
    }

    private static UserRole ResolveRole(out string reason)
    {
        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom == null)
        {
            reason = "RoomSessionContext.CurrentRoom is null";
            return UserRole.Unknown;
        }

        string hostUserId = string.IsNullOrWhiteSpace(currentRoom.HostUserId)
            ? string.Empty
            : currentRoom.HostUserId.Trim();
        if (string.IsNullOrWhiteSpace(hostUserId))
        {
            reason = "HostUserId is empty";
            return UserRole.Unknown;
        }

        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
        {
            reason = "Current user is not resolved";
            return UserRole.Unknown;
        }

        string currentUserId = string.IsNullOrWhiteSpace(AuthManager.Instance.CurrentUser.userId)
            ? string.Empty
            : AuthManager.Instance.CurrentUser.userId.Trim();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            reason = "Current user id is empty";
            return UserRole.Unknown;
        }

        bool isHost = string.Equals(hostUserId, currentUserId, StringComparison.Ordinal);
        reason = isHost
            ? $"host={hostUserId}, me={currentUserId}"
            : $"current user does not match host (host={hostUserId}, me={currentUserId})";
        return isHost ? UserRole.Host : UserRole.Client;
    }
}
