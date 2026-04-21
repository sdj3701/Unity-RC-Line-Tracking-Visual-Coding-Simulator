using Fusion;
using RC.Network.Fusion;
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
        FusionConnectionManager connectionManager = FusionConnectionManager.Instance;
        NetworkRunner runner = connectionManager != null ? connectionManager.Runner : null;

        if (runner != null && runner.IsRunning && !runner.IsShutdown)
        {
            if (runner.IsServer)
            {
                reason = $"Photon runner is server. session={runner.SessionInfo.Name}";
                return UserRole.Host;
            }

            if (runner.IsClient)
            {
                reason = $"Photon runner is client. session={runner.SessionInfo.Name}";
                return UserRole.Client;
            }
        }

        FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
        if (context != null)
        {
            if (context.IsHost || context.GameMode == GameMode.Host)
            {
                reason = $"Fusion session context indicates host. session={context.SessionName}";
                return UserRole.Host;
            }

            if (context.GameMode == GameMode.Client)
            {
                reason = $"Fusion session context indicates client. session={context.SessionName}";
                return UserRole.Client;
            }
        }

        reason = "Photon runner/session context unavailable";
        return UserRole.Unknown;
    }
}
