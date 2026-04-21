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
    [SerializeField] private bool _debugLog;

    [Networked] private Vector3 SyncedPosition { get; set; }
    [Networked] private Quaternion SyncedRotation { get; set; }
    [Networked] private int SyncedPackedColor { get; set; }
    [Networked] private int SyncedSlotIndex { get; set; }

    private Rigidbody _rigidbody;
    private VirtualCarPhysics _physics;
    private BlockCodeExecutor _executor;
    private VirtualArduinoMicro _arduino;
    private string _assignedUserId = string.Empty;
    private int _configuredSlotIndex;
    private Color _configuredColor = Color.white;
    private bool _hasPendingConfiguration;
    private int _lastAppliedPackedColor = int.MinValue;

    public string AssignedUserId => _assignedUserId;
    public int AssignedSlotIndex => SyncedSlotIndex > 0 ? SyncedSlotIndex : _configuredSlotIndex;

    public void ConfigureForHost(string userId, int slotIndex, Color color)
    {
        _assignedUserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        _configuredSlotIndex = Mathf.Max(1, slotIndex);
        _configuredColor = color;
        _hasPendingConfiguration = true;

        if (Object != null && Object.HasStateAuthority)
            ApplyPendingConfiguration();
    }

    public override void Spawned()
    {
        CacheRuntimeRefs();
        ApplyAuthorityState();

        if (Object.HasStateAuthority)
        {
            ApplyPendingConfiguration();
            CaptureTransformState();
        }
        else
        {
            ApplyTransformState(immediate: true);
            ApplyColorFromNetwork(force: true);
        }

        RenameForDebug();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        ApplyPendingConfiguration();
        CaptureTransformState();
    }

    public override void Render()
    {
        if (Object == null || Object.HasStateAuthority)
            return;

        ApplyTransformState(immediate: false);
        ApplyColorFromNetwork(force: false);
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
        _lastAppliedPackedColor = SyncedPackedColor;
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
    }

    private void ApplyTransformState(bool immediate)
    {
        Vector3 targetPosition = _syncPosition ? SyncedPosition : transform.position;
        Quaternion targetRotation = _syncRotation ? SyncedRotation : transform.rotation;

        if (immediate)
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
        if (string.IsNullOrWhiteSpace(_assignedUserId) || AssignedSlotIndex <= 0)
            return;

        name = $"Car_slot{AssignedSlotIndex}_{SafeName(_assignedUserId)}";

        if (_debugLog)
            Debug.Log($"[NetworkRCCar] configured. slot={AssignedSlotIndex}, user={_assignedUserId}, stateAuthority={Object != null && Object.HasStateAuthority}");
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
}
