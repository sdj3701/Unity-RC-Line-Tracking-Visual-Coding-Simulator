using Fusion;
using RC.Network.Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkUIManager : MonoBehaviour
{
    [Header("Common UI")]
    [SerializeField] private GameObject _commonUI;
    [SerializeField] private bool _showCommonUIForAllRoles = true;

    [Header("Role UI")]
    [SerializeField] private GameObject _hostUI;
    [SerializeField] private GameObject _clientUI;
    [SerializeField] private bool _treatUnknownAsClient = true;

    [Header("Auto Resolve")]
    [SerializeField] private bool _autoResolveMissingUiRoots = true;
    [SerializeField] private string _commonUiObjectName = "Common HUD";
    [SerializeField] private string _hostUiObjectName = "Host UI";
    [SerializeField] private string _clientUiObjectName = "Client UI";

    [Header("Refresh")]
    [SerializeField, Min(0.05f)] private float _refreshIntervalSeconds = 0.25f;
    [SerializeField] private bool _debugLog = true;

    private float _nextRefreshAt;
    private UserRole _lastResolvedRole = UserRole.Unknown;
    private bool _hasResolvedAtLeastOnce;
    private string _lastReason = string.Empty;

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

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, _refreshIntervalSeconds);
        RefreshRoleUI();
    }

    public void RefreshRoleUI()
    {
        TryResolveUiRoots();

        UserRole role = ResolveRole(out string reason);

        bool showHostUI = role == UserRole.Host;
        bool showClientUI = role == UserRole.Client ||
                            (role == UserRole.Unknown && _treatUnknownAsClient);
        bool showCommonUI = _showCommonUIForAllRoles &&
                            (role == UserRole.Host ||
                             role == UserRole.Client ||
                             (role == UserRole.Unknown && _treatUnknownAsClient));

        if (_commonUI != null)
            _commonUI.SetActive(showCommonUI);

        if (_hostUI != null)
            _hostUI.SetActive(showHostUI);

        if (_clientUI != null)
            _clientUI.SetActive(showClientUI);

        if (_debugLog && ShouldLog(role, reason))
            Debug.Log($"[NetworkUIManager] role={role}, reason={reason}, commonUI={showCommonUI}, hostUI={showHostUI}, clientUI={showClientUI}");

        _lastResolvedRole = role;
        _lastReason = reason ?? string.Empty;
        _hasResolvedAtLeastOnce = true;
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

    private void TryResolveUiRoots()
    {
        if (!_autoResolveMissingUiRoots)
            return;

        if (_commonUI == null)
            _commonUI = FindSceneObject(_commonUiObjectName);

        if (_hostUI == null)
            _hostUI = FindSceneObject(_hostUiObjectName);

        if (_clientUI == null)
            _clientUI = FindSceneObject(_clientUiObjectName);
    }

    private bool ShouldLog(UserRole role, string reason)
    {
        if (!_hasResolvedAtLeastOnce)
            return true;

        if (_lastResolvedRole != role)
            return true;

        return !string.Equals(_lastReason, reason ?? string.Empty, System.StringComparison.Ordinal);
    }

    private static GameObject FindSceneObject(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        string normalizedName = objectName.Trim();
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject found = FindInHierarchy(roots[i].transform, normalizedName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject FindInHierarchy(Transform root, string objectName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, objectName, System.StringComparison.Ordinal))
            return root.gameObject;

        for (int i = 0; i < root.childCount; i++)
        {
            GameObject found = FindInHierarchy(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }
}
