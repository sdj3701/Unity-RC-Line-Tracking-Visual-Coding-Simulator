using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkRCCar : NetworkBehaviour
{
    [Header("Replication")]
    [SerializeField] private bool _syncPosition = true;
    [SerializeField] private bool _syncRotation = true;
    [SerializeField, Range(0.01f, 1f)] private float _clientLerpAlpha = 0.35f;
    [SerializeField, Min(0.1f)] private float _clientSnapDistance = 2f;
    [SerializeField] private bool _debugLog;

    [Networked] private Vector3 SyncedPosition { get; set; }
    [Networked] private Quaternion SyncedRotation { get; set; }
    [Networked] private Vector3 SyncedVelocity { get; set; }
    [Networked] private Vector3 SyncedAngularVelocity { get; set; }
    [Networked] private int SyncedPackedColor { get; set; }
    [Networked] private int SyncedSlotIndex { get; set; }
    [Networked] private NetworkString<_64> SyncedUserId { get; set; }
    [Networked] private bool SyncedIsRunning { get; set; }
    [Networked] private int SyncedMapIndex { get; set; }

    private Rigidbody _rigidbody;
    private VirtualCarPhysics _physics;
    private BlockCodeExecutor _executor;
    private VirtualArduinoMicro _arduino;
    private static ChangeMap _sharedChangeMap;
    private string _assignedUserId = string.Empty;
    private int _configuredSlotIndex;
    private Color _configuredColor = Color.white;
    private bool _hasPendingConfiguration;
    private int _lastAppliedPackedColor = int.MinValue;
    private string _lastAppliedUserId = string.Empty;
    private int _lastAppliedMapIndex = int.MinValue;

    public string AssignedUserId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_assignedUserId))
                return _assignedUserId;

            string synced = SyncedUserId.ToString();
            return string.IsNullOrWhiteSpace(synced) ? string.Empty : synced.Trim();
        }
    }

    public int AssignedSlotIndex => SyncedSlotIndex > 0 ? SyncedSlotIndex : _configuredSlotIndex;
    public bool IsNetworkRunning => SyncedIsRunning;
    public bool CanSubmitCodeSelectionToHost => Object != null && Object.HasInputAuthority;
    public bool HasLocalInputAuthority => Object != null && Object.HasInputAuthority;

    public void ConfigureForHost(string userId, int slotIndex, Color color)
    {
        _assignedUserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        _configuredSlotIndex = Mathf.Max(1, slotIndex);
        _configuredColor = color;
        _hasPendingConfiguration = true;

        if (Object != null && Object.HasStateAuthority)
            ApplyPendingConfiguration();
    }

    public bool TrySubmitCodeSelectionToHost(int userLevelSeq, string fileNameRaw, out string error)
    {
        error = string.Empty;

        if (Object == null)
        {
            error = "NetworkObject is null.";
            return false;
        }

        if (!Object.HasInputAuthority)
        {
            error = $"This car has no input authority. netId={Object.Id}";
            return false;
        }

        if (userLevelSeq <= 0)
        {
            error = "userLevelSeq must be >= 1.";
            return false;
        }

        string fileName = TrimForNetworkString(fileNameRaw, 120);
        RPC_SubmitCodeSelectionToHost(userLevelSeq, fileName);

        if (_debugLog)
            Debug.Log($"<color=#33A6FF>[NetworkRCCar] Client code selection sent. user={AssignedUserId}, seq={userLevelSeq}, fileName={fileName}, netId={Object.Id}</color>");

        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SubmitCodeSelectionToHost(int userLevelSeq, NetworkString<_128> fileName, RpcInfo info = default)
    {
        HostNetworkCarCoordinator coordinator = FindObjectOfType<HostNetworkCarCoordinator>();
        if (coordinator == null)
        {
            if (_debugLog)
                Debug.LogWarning("[NetworkRCCar] HostNetworkCarCoordinator is missing. Photon code selection ignored.");
            return;
        }

        coordinator.HandlePhotonCodeSelection(AssignedUserId, userLevelSeq, fileName.ToString(), info.Source);
    }

    public override void Spawned()
    {
        CacheRuntimeRefs();
        ApplyAuthorityState();

        if (Object.HasStateAuthority)
        {
            ApplyPendingConfiguration();
            CaptureTransformState();
            CaptureSharedSceneState();
        }
        else
        {
            ApplySharedSceneStateFromNetwork(force: true);
            ApplyTransformState(immediate: true);
            ApplyColorFromNetwork(force: true);
            ApplyUserIdFromNetwork(force: true);
        }

        RenameForDebug();

        if (_debugLog)
        {
            string netId = Object != null ? Object.Id.ToString() : "-";
            string inputAuthority = Object != null ? Object.InputAuthority.ToString() : "-";
            Debug.Log(
                $"[NetworkRCCar] spawned. netId={netId}, stateAuthority={Object != null && Object.HasStateAuthority}, inputAuthority={inputAuthority}");
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        ApplyAuthorityState();
        ApplyPendingConfiguration();
        CaptureTransformState();
        CaptureSharedSceneState();
    }

    public override void Render()
    {
        if (Object == null || Object.HasStateAuthority)
            return;

        ApplyAuthorityState();
        ApplySharedSceneStateFromNetwork(force: false);
        ApplyTransformState(immediate: false);
        ApplyColorFromNetwork(force: false);
        ApplyUserIdFromNetwork(force: false);
    }

    private void CacheRuntimeRefs()
    {
        if (_rigidbody == null)
            _rigidbody = GetComponent<Rigidbody>();
        if (_physics == null)
            _physics = GetComponentInChildren<VirtualCarPhysics>(true);
        if (_executor == null)
            _executor = GetComponentInChildren<BlockCodeExecutor>(true);
        if (_arduino == null)
            _arduino = GetComponentInChildren<VirtualArduinoMicro>(true);
    }

    private void ApplyAuthorityState()
    {
        bool hasStateAuthority = Object != null && Object.HasStateAuthority;

        if (_rigidbody != null)
            _rigidbody.isKinematic = !hasStateAuthority;

        if (_physics != null)
        {
            if (!hasStateAuthority && _physics.IsRunning)
                _physics.StopRunning();

            _physics.enabled = hasStateAuthority;
        }

        if (_executor != null)
            _executor.enabled = hasStateAuthority;

        if (_arduino != null)
            _arduino.enabled = hasStateAuthority;
    }

    private void ApplyPendingConfiguration()
    {
        if (!_hasPendingConfiguration)
            return;

        SyncedSlotIndex = Mathf.Max(1, _configuredSlotIndex);
        SyncedPackedColor = PackColor(_configuredColor);
        SyncedUserId = _assignedUserId ?? string.Empty;
        _lastAppliedPackedColor = SyncedPackedColor;
        _lastAppliedUserId = _assignedUserId ?? string.Empty;
        ApplyColor(_configuredColor);
        _hasPendingConfiguration = false;
        RenameForDebug();
    }

    private void CaptureTransformState()
    {
        if (_syncPosition)
            SyncedPosition = transform.position;

        if (_syncRotation)
            SyncedRotation = transform.rotation;

        if (_rigidbody != null)
        {
            SyncedVelocity = _rigidbody.velocity;
            SyncedAngularVelocity = _rigidbody.angularVelocity;
        }
        else
        {
            SyncedVelocity = Vector3.zero;
            SyncedAngularVelocity = Vector3.zero;
        }

        SyncedIsRunning = _physics != null && _physics.IsRunning;
    }

    private void CaptureSharedSceneState()
    {
        ChangeMap changeMap = ResolveChangeMap();
        if (changeMap == null)
            return;

        SyncedMapIndex = changeMap.CurrentMapIndex;
    }

    private void ApplySharedSceneStateFromNetwork(bool force)
    {
        ChangeMap changeMap = ResolveChangeMap();
        if (changeMap == null)
            return;

        int mapIndex = SyncedMapIndex;
        if (!force && _lastAppliedMapIndex == mapIndex)
            return;

        if (!force && changeMap.CurrentMapIndex == mapIndex)
        {
            _lastAppliedMapIndex = mapIndex;
            return;
        }

        changeMap.ApplyMap(mapIndex, moveCarToSpawn: false);
        _lastAppliedMapIndex = mapIndex;
    }

    private void ApplyTransformState(bool immediate)
    {
        Vector3 targetPosition = _syncPosition ? SyncedPosition : transform.position;
        Quaternion targetRotation = _syncRotation ? SyncedRotation : transform.rotation;
        float snapDistance = Mathf.Max(0.1f, _clientSnapDistance);
        bool shouldSnap = immediate ||
                          (_syncPosition && (transform.position - targetPosition).sqrMagnitude > snapDistance * snapDistance);

        if (shouldSnap)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        float alpha = Mathf.Clamp01(_clientLerpAlpha);
        Vector3 nextPosition = _syncPosition
            ? Vector3.Lerp(transform.position, targetPosition, alpha)
            : transform.position;
        Quaternion nextRotation = _syncRotation
            ? Quaternion.Slerp(transform.rotation, targetRotation, alpha)
            : transform.rotation;

        transform.SetPositionAndRotation(nextPosition, nextRotation);
    }

    private void ApplyColorFromNetwork(bool force)
    {
        int packed = SyncedPackedColor;
        if (!force && packed == _lastAppliedPackedColor)
            return;

        _lastAppliedPackedColor = packed;
        ApplyColor(UnpackColor(packed));
    }

    private void ApplyUserIdFromNetwork(bool force)
    {
        string synced = SyncedUserId.ToString();
        synced = string.IsNullOrWhiteSpace(synced) ? string.Empty : synced.Trim();
        if (!force && string.Equals(_lastAppliedUserId, synced, System.StringComparison.Ordinal))
            return;

        _lastAppliedUserId = synced;
        if (!string.IsNullOrWhiteSpace(synced))
            _assignedUserId = synced;

        RenameForDebug();
    }

    private void ApplyColor(Color color)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material material = renderer.material;
            if (material == null)
                continue;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }

    private void RenameForDebug()
    {
        string userId = AssignedUserId;
        if (string.IsNullOrWhiteSpace(userId) || AssignedSlotIndex <= 0)
            return;

        name = $"Car_slot{AssignedSlotIndex}_{SafeName(userId)}";

        if (_debugLog)
            Debug.Log($"[NetworkRCCar] configured. slot={AssignedSlotIndex}, user={userId}, stateAuthority={Object != null && Object.HasStateAuthority}");
    }

    private static int PackColor(Color color)
    {
        Color32 value = color;
        return value.r | (value.g << 8) | (value.b << 16) | (value.a << 24);
    }

    private static Color UnpackColor(int packed)
    {
        byte r = (byte)(packed & 0xFF);
        byte g = (byte)((packed >> 8) & 0xFF);
        byte b = (byte)((packed >> 16) & 0xFF);
        byte a = (byte)((packed >> 24) & 0xFF);
        return new Color32(r, g, b, a);
    }

    private static string SafeName(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? "unknown" : userId.Replace(" ", "_");
    }

    private static string TrimForNetworkString(string value, int maxLength)
    {
        string text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        int limit = Mathf.Max(1, maxLength);
        return text.Length <= limit ? text : text.Substring(0, limit);
    }

    private static ChangeMap ResolveChangeMap()
    {
        if (_sharedChangeMap == null)
            _sharedChangeMap = FindObjectOfType<ChangeMap>();

        return _sharedChangeMap;
    }
}
