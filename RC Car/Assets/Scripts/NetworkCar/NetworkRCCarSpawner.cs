using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed class NetworkRCCarSpawner
{
    private readonly bool _debugLog;

    public NetworkRCCarSpawner(bool debugLog)
    {
        _debugLog = debugLog;
    }

    public HostCarRuntimeRefs SpawnForPlayer(
        NetworkRunner runner,
        GameObject carPrefab,
        Transform carRoot,
        IList<Transform> slotSpawnPoints,
        int slotIndex,
        string userId,
        PlayerRef inputAuthority,
        Color color)
    {
        if (runner == null || !runner.IsRunning || runner.IsShutdown)
        {
            LogWarning($"Runner is not ready for network spawn. slot={slotIndex}, user={userId}");
            return null;
        }

        if (!runner.IsServer)
        {
            LogWarning($"Network spawn must run on host/server only. slot={slotIndex}, user={userId}, isClient={runner.IsClient}");
            return null;
        }

        if (carPrefab == null)
        {
            LogWarning("carPrefab is null.");
            return null;
        }

        NetworkObject prefabNetworkObject = carPrefab.GetComponent<NetworkObject>();
        if (prefabNetworkObject == null)
        {
            LogWarning($"carPrefab does not contain NetworkObject. prefab={carPrefab.name}");
            return null;
        }

        if (!prefabNetworkObject.gameObject.scene.IsValid() && _debugLog)
            Debug.Log($"[NetworkRCCarSpawner] Prefab candidate accepted. prefab={carPrefab.name}, slot={slotIndex}, user={userId}");

        ResolveSpawnPose(carRoot, slotSpawnPoints, slotIndex, out Vector3 position, out Quaternion rotation);

        NetworkObject spawnedObject = runner.Spawn(
            prefabNetworkObject,
            position,
            rotation,
            inputAuthority,
            (spawnRunner, networkObject) =>
            {
                NetworkRCCar networkCar = networkObject.GetComponent<NetworkRCCar>();
                if (networkCar != null)
                    networkCar.ConfigureForHost(userId, slotIndex, color);
            });

        if (spawnedObject == null)
        {
            LogWarning($"Runner.Spawn returned null. slot={slotIndex}, user={userId}, prefab={carPrefab.name}");
            return null;
        }

        HostCarRuntimeRefs refs = BuildRuntimeRefs(spawnedObject.gameObject, spawnedObject, inputAuthority);
        ApplyColor(refs.CarObject, color);

        if (refs.Physics != null)
            refs.Physics.StopRunning();

        if (_debugLog)
        {
            Debug.Log(
                $"[NetworkRCCarSpawner] Spawned network car. slot={slotIndex}, user={userId}, player={inputAuthority}, pos={position}, netId={spawnedObject.Id}");
        }

        return refs;
    }

    private static HostCarRuntimeRefs BuildRuntimeRefs(GameObject carObject, NetworkObject networkObject, PlayerRef inputAuthority)
    {
        var refs = new HostCarRuntimeRefs
        {
            CarObject = carObject,
            NetworkObject = networkObject,
            NetworkCar = carObject != null ? carObject.GetComponent<NetworkRCCar>() : null,
            Physics = carObject != null ? carObject.GetComponentInChildren<VirtualCarPhysics>(true) : null,
            Executor = carObject != null ? carObject.GetComponentInChildren<BlockCodeExecutor>(true) : null,
            Arduino = carObject != null ? carObject.GetComponentInChildren<VirtualArduinoMicro>(true) : null,
            OwnerPlayer = inputAuthority
        };

        if (refs.Executor != null)
            refs.Executor.arduino = refs.Arduino;

        if (refs.Arduino != null)
            refs.Arduino.blockCodeExecutor = refs.Executor;

        if (refs.Physics != null)
        {
            refs.Physics.blockCodeExecutor = refs.Executor;
            refs.Physics.motorDriver = carObject != null ? carObject.GetComponentInChildren<VirtualMotorDriver>(true) : null;
        }

        return refs;
    }

    private static void ResolveSpawnPose(
        Transform carRoot,
        IList<Transform> slotSpawnPoints,
        int slotIndex,
        out Vector3 position,
        out Quaternion rotation)
    {
        int pointIndex = Mathf.Max(0, slotIndex - 1);
        if (slotSpawnPoints != null &&
            pointIndex >= 0 &&
            pointIndex < slotSpawnPoints.Count &&
            slotSpawnPoints[pointIndex] != null)
        {
            Transform point = slotSpawnPoints[pointIndex];
            position = point.position;
            rotation = point.rotation;
            return;
        }

        Vector3 basePosition = carRoot != null ? carRoot.position : Vector3.zero;
        position = basePosition + new Vector3(0f, 0f, pointIndex * 2f);
        rotation = carRoot != null ? carRoot.rotation : Quaternion.identity;
    }

    private static void ApplyColor(GameObject carObject, Color color)
    {
        if (carObject == null)
            return;

        Renderer[] renderers = carObject.GetComponentsInChildren<Renderer>(true);
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

    private void LogWarning(string message)
    {
        if (_debugLog && !string.IsNullOrWhiteSpace(message))
            Debug.LogWarning($"[NetworkRCCarSpawner] {message}");
    }
}
