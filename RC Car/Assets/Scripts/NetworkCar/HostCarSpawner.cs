using System.Collections.Generic;
using Fusion;
using RC.Network.Fusion;
using UnityEngine;

public sealed class HostCarSpawner
{
    private readonly GameObject _carPrefab;
    private readonly Transform _carRoot;
    private readonly IList<Transform> _slotSpawnPoints;
    private readonly bool _debugLog;
    private readonly NetworkRCCarSpawner _networkSpawner;

    public HostCarSpawner(
        GameObject carPrefab,
        Transform carRoot,
        IList<Transform> slotSpawnPoints,
        bool debugLog)
    {
        _carPrefab = carPrefab;
        _carRoot = carRoot;
        _slotSpawnPoints = slotSpawnPoints;
        _debugLog = debugLog;
        _networkSpawner = new NetworkRCCarSpawner(debugLog);
    }

    public HostCarRuntimeRefs EnsureCarForSlot(
        int slotIndex,
        string userId,
        PlayerRef ownerPlayer,
        HostCarRuntimeRefs existingRefs,
        Color color)
    {
        if (existingRefs != null && existingRefs.CarObject != null)
        {
            RebindRuntimeRefs(existingRefs, ownerPlayer);
            return existingRefs;
        }

        if (_carPrefab == null)
        {
            Debug.LogWarning("[HostCarSpawner] carPrefab is null.");
            return null;
        }

        NetworkRunner runner = FusionConnectionManager.Instance != null
            ? FusionConnectionManager.Instance.Runner
            : null;

        HostCarRuntimeRefs refs = _networkSpawner.SpawnForPlayer(
            runner,
            _carPrefab,
            _carRoot,
            _slotSpawnPoints,
            slotIndex,
            userId,
            ownerPlayer,
            color);

        if (refs == null)
            return null;

        RebindRuntimeRefs(refs, ownerPlayer);

        if (_debugLog)
        {
            Debug.Log(
                $"[HostCarSpawner] Spawned network car. slot={slotIndex}, user={userId}, player={ownerPlayer}, hasPhysics={refs.Physics != null}, hasExecutor={refs.Executor != null}");
        }

        return refs;
    }

    private static void RebindRuntimeRefs(HostCarRuntimeRefs refs, PlayerRef ownerPlayer)
    {
        if (refs == null || refs.CarObject == null)
            return;

        if (refs.NetworkObject == null)
            refs.NetworkObject = refs.CarObject.GetComponent<NetworkObject>();
        if (refs.NetworkCar == null)
            refs.NetworkCar = refs.CarObject.GetComponent<NetworkRCCar>();
        if (refs.Physics == null)
            refs.Physics = refs.CarObject.GetComponentInChildren<VirtualCarPhysics>(true);
        if (refs.Executor == null)
            refs.Executor = refs.CarObject.GetComponentInChildren<BlockCodeExecutor>(true);
        if (refs.Arduino == null)
            refs.Arduino = refs.CarObject.GetComponentInChildren<VirtualArduinoMicro>(true);

        refs.OwnerPlayer = ownerPlayer;

        if (refs.Executor != null)
            refs.Executor.arduino = refs.Arduino;

        if (refs.Arduino != null)
            refs.Arduino.blockCodeExecutor = refs.Executor;

        if (refs.Physics != null)
        {
            refs.Physics.blockCodeExecutor = refs.Executor;
            refs.Physics.motorDriver = refs.CarObject.GetComponentInChildren<VirtualMotorDriver>(true);
        }
    }
}
