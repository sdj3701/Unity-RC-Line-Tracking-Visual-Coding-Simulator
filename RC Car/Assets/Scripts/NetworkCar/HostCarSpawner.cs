using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class HostCarSpawner
{
    private readonly GameObject _carPrefab;
    private readonly Transform _carRoot;
    private readonly IList<Transform> _slotSpawnPoints;
    private readonly bool _debugLog;

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
    }

    public HostCarRuntimeRefs EnsureCarForSlot(
        int slotIndex,
        string userId,
        HostCarRuntimeRefs existingRefs,
        Color color)
    {
        if (existingRefs != null && existingRefs.CarObject != null)
        {
            RebindRuntimeRefs(existingRefs);
            return existingRefs;
        }

        if (_carPrefab == null)
        {
            Debug.LogWarning("[HostCarSpawner] carPrefab is null.");
            return null;
        }

        ResolveSpawnPose(slotIndex, out Vector3 position, out Quaternion rotation);
        bool useParent = _carRoot != null && !IsPersistentTransform(_carRoot);
        GameObject carObject = useParent
            ? UnityEngine.Object.Instantiate(_carPrefab, position, rotation, _carRoot)
            : UnityEngine.Object.Instantiate(_carPrefab, position, rotation);

        if (!useParent && _carRoot != null && _debugLog)
        {
            Debug.LogWarning(
                $"[HostCarSpawner] carRoot is persistent. spawn without parent. slot={slotIndex}, user={userId}, root={_carRoot.name}");
        }

        carObject.name = $"{_carPrefab.name}_slot{slotIndex}_{SafeName(userId)}";

        var refs = new HostCarRuntimeRefs
        {
            CarObject = carObject,
            Physics = carObject.GetComponentInChildren<VirtualCarPhysics>(true),
            Executor = carObject.GetComponentInChildren<BlockCodeExecutor>(true),
            Arduino = carObject.GetComponentInChildren<VirtualArduinoMicro>(true)
        };

        RebindRuntimeRefs(refs);

        ApplyColor(carObject, color);

        if (refs.Physics != null)
            refs.Physics.StopRunning();

        if (_debugLog)
        {
            Debug.Log(
                $"[HostCarSpawner] Spawned car. slot={slotIndex}, user={userId}, pos={position}, hasPhysics={refs.Physics != null}, hasExecutor={refs.Executor != null}");
        }

        return refs;
    }

    private static bool IsPersistentTransform(Transform root)
    {
        if (root == null)
            return false;

        Scene scene = root.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
            return true;

        return string.Equals(scene.name, "DontDestroyOnLoad", System.StringComparison.Ordinal);
    }

    private static void RebindRuntimeRefs(HostCarRuntimeRefs refs)
    {
        if (refs == null || refs.CarObject == null)
            return;

        if (refs.Physics == null)
            refs.Physics = refs.CarObject.GetComponentInChildren<VirtualCarPhysics>(true);
        if (refs.Executor == null)
            refs.Executor = refs.CarObject.GetComponentInChildren<BlockCodeExecutor>(true);
        if (refs.Arduino == null)
            refs.Arduino = refs.CarObject.GetComponentInChildren<VirtualArduinoMicro>(true);

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

    private void ResolveSpawnPose(int slotIndex, out Vector3 position, out Quaternion rotation)
    {
        int pointIndex = Mathf.Max(0, slotIndex - 1);
        if (_slotSpawnPoints != null &&
            pointIndex >= 0 &&
            pointIndex < _slotSpawnPoints.Count &&
            _slotSpawnPoints[pointIndex] != null)
        {
            Transform point = _slotSpawnPoints[pointIndex];
            position = point.position;
            rotation = point.rotation;
            return;
        }

        Vector3 basePosition = _carRoot != null ? _carRoot.position : Vector3.zero;
        position = basePosition + new Vector3(pointIndex * 0f, 0f, 0f);
        rotation = _carRoot != null ? _carRoot.rotation : Quaternion.identity;
    }

    private static string SafeName(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "unknown";

        return userId.Replace(" ", "_");
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
}

