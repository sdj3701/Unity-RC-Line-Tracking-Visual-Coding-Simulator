using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChangeMap : MonoBehaviour
{
    [System.Serializable]
    public struct SpawnPose
    {
        public Vector3 position;
        public Vector3 rotation;
    }

    [System.Serializable]
    public class RuntimeMapEntry
    {
        public string mapId = "";
        public string displayName = "";
        public Material material;
        public SpawnPose spawnPose;
        public bool destroyMaterialOnRemove = true;
    }

    [Header("맵 설정")]
    [Tooltip("Plane에 순서대로 적용할 머테리얼 목록")]
    public Material[] mapMaterials;

    [Tooltip("맵 인덱스와 매칭되는 차량 시작 위치/회전 목록")]
    public SpawnPose[] carSpawnPoses;

    [Tooltip("시작 시 현재 맵을 적용하고 차량을 시작 위치로 이동")]
    public bool applyCurrentMapOnStart = true;
    [Tooltip("맵 변경 버튼 순환에 런타임 맵까지 포함할지 여부. false면 기본 mapMaterials만 순환한다.")]
    public bool includeRuntimeMapsInCycle = true;

    [Header("참조 설정")]
    [Tooltip("머테리얼을 적용할 플레인 렌더러")]
    public Renderer planeRenderer;

    [Tooltip("RC카 트랜스폼")]
    public Transform carTransform;

    [Tooltip("차량 물리 제어 스크립트(비우면 자동 탐색)")]
    public VirtualCarPhysics carPhysics;

    [Tooltip("리스타트 기준 위치 동기화용 ButtonRestart")]
    public ButtonRestart buttonRestart;

    [Tooltip("맵 변경 버튼(비우면 현재 오브젝트 Button 자동 사용)")]
    public Button changeMapButton;

    [SerializeField] int currentMapIndex = 0;
    [SerializeField] bool logRuntimeMapActions = true;
    [SerializeField] List<RuntimeMapEntry> runtimeMaps = new List<RuntimeMapEntry>();

    public int CurrentMapIndex => currentMapIndex;
    public int StaticMapCount => mapMaterials != null ? mapMaterials.Length : 0;
    public int RuntimeMapCount => runtimeMaps != null ? runtimeMaps.Count : 0;
    public int TotalMapCount => StaticMapCount + RuntimeMapCount;
    public IReadOnlyList<RuntimeMapEntry> RuntimeMaps => runtimeMaps;

    void Start()
    {
        TryAutoFindReferences();

        if (changeMapButton == null)
        {
            changeMapButton = GetComponent<Button>();
        }

        if (changeMapButton != null)
        {
            if (!HasPersistentChangeToNextMapBinding(changeMapButton))
            {
                changeMapButton.onClick.AddListener(ChangeToNextMap);
            }
        }

        if (applyCurrentMapOnStart)
        {
            currentMapIndex = 0;
            ApplyMap(0, true);
        }
    }

    void OnDestroy()
    {
        if (changeMapButton != null)
        {
            changeMapButton.onClick.RemoveListener(ChangeToNextMap);
        }

        ClearRuntimeMaps();
    }

    /// <summary>
    /// 다음 맵 머테리얼로 전환합니다.
    /// </summary>
    public void ChangeToNextMap()
    {
        if (TotalMapCount == 0)
        {
            Debug.LogWarning("[ChangeMap] 맵 목록이 비어 있습니다.");
            return;
        }

        int nextIndex = ResolveNextIndexForCycle();
        ApplyMap(nextIndex, true);
    }

    /// <summary>
    /// 인덱스로 맵을 적용합니다.
    /// </summary>
    public void ApplyMap(int mapIndex, bool moveCarToSpawn)
    {
        int totalCount = TotalMapCount;
        if (totalCount == 0)
        {
            Debug.LogWarning("[ChangeMap] 맵 목록이 비어 있어 맵을 적용할 수 없습니다.");
            return;
        }

        currentMapIndex = NormalizeIndex(mapIndex, totalCount);
        ApplyCurrentMaterial();

        if (moveCarToSpawn)
        {
            MoveCarToCurrentSpawn();
        }

        SyncRestartInitialPosition();

        Debug.Log($"[ChangeMap] 맵 변경 완료 -> 인덱스: {currentMapIndex} (static={StaticMapCount}, runtime={RuntimeMapCount})");
    }

    /// <summary>
    /// 현재 맵의 스폰 위치/회전을 가져옵니다.
    /// </summary>
    public bool TryGetCurrentSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (TryGetSpawnPose(currentMapIndex, out position, out rotation))
        {
            return true;
        }

        if (carTransform != null)
        {
            position = carTransform.position;
            rotation = carTransform.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    /// <summary>
    /// 런타임 생성 맵을 목록에 추가하고, 즉시 적용할지 선택한다.
    /// 반환값은 전체 맵 목록 기준 인덱스다.
    /// </summary>
    public int AddRuntimeMap(Material material, SpawnPose spawnPose, string mapId, string displayName, bool applyImmediately)
    {
        if (material == null)
        {
            Debug.LogWarning("[ChangeMap] AddRuntimeMap failed: material is null.");
            return -1;
        }

        if (runtimeMaps == null)
        {
            runtimeMaps = new List<RuntimeMapEntry>();
        }

        int replaceIndex = FindRuntimeMapIndexById(mapId);
        RuntimeMapEntry newEntry = new RuntimeMapEntry
        {
            mapId = mapId ?? "",
            displayName = displayName ?? "",
            material = material,
            spawnPose = spawnPose,
            destroyMaterialOnRemove = true
        };

        int runtimeIndex;
        if (replaceIndex >= 0)
        {
            RuntimeMapEntry oldEntry = runtimeMaps[replaceIndex];
            if (oldEntry != null && oldEntry.destroyMaterialOnRemove)
            {
                // 현재 Plane에 사용 중인 머테리얼을 즉시 파괴하면 화면이 사라질 수 있어 보호한다.
                bool isCurrentPlaneMaterial = planeRenderer != null && planeRenderer.sharedMaterial == oldEntry.material;
                if (!isCurrentPlaneMaterial)
                {
                    SafeDestroyMaterial(oldEntry.material);
                }
            }

            runtimeMaps[replaceIndex] = newEntry;
            runtimeIndex = replaceIndex;
        }
        else
        {
            runtimeMaps.Add(newEntry);
            runtimeIndex = runtimeMaps.Count - 1;
        }

        int globalIndex = StaticMapCount + runtimeIndex;
        if (applyImmediately)
        {
            ApplyMap(globalIndex, true);
        }

        if (logRuntimeMapActions)
        {
            Debug.Log($"[ChangeMap] Runtime map registered -> id={newEntry.mapId}, index={globalIndex}");
        }

        return globalIndex;
    }

    /// <summary>
    /// 런타임 맵 목록을 모두 제거한다.
    /// </summary>
    public void ClearRuntimeMaps()
    {
        if (runtimeMaps == null || runtimeMaps.Count == 0)
        {
            return;
        }

        for (int i = 0; i < runtimeMaps.Count; i++)
        {
            RuntimeMapEntry entry = runtimeMaps[i];
            if (entry != null && entry.destroyMaterialOnRemove)
            {
                SafeDestroyMaterial(entry.material);
            }
        }

        runtimeMaps.Clear();
        currentMapIndex = NormalizeIndex(currentMapIndex, Mathf.Max(1, TotalMapCount));

        if (logRuntimeMapActions)
        {
            Debug.Log("[ChangeMap] Runtime maps cleared.");
        }
    }

    void ApplyCurrentMaterial()
    {
        if (planeRenderer == null)
        {
            Debug.LogWarning("[ChangeMap] 플레인 렌더러가 비어 있어 머테리얼을 적용할 수 없습니다.");
            return;
        }

        if (!TryGetMaterialByIndex(currentMapIndex, out Material targetMaterial) || targetMaterial == null)
        {
            Debug.LogWarning($"[ChangeMap] 인덱스 {currentMapIndex}에 해당하는 머테리얼을 찾지 못했습니다.");
            return;
        }

        planeRenderer.sharedMaterial = targetMaterial;
    }

    void MoveCarToCurrentSpawn()
    {
        if (carTransform == null)
        {
            Debug.LogWarning("[ChangeMap] 차량 트랜스폼이 비어 있어 차량을 이동할 수 없습니다.");
            return;
        }

        if (!TryGetSpawnPose(currentMapIndex, out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            Debug.LogWarning($"[ChangeMap] 맵 인덱스 {currentMapIndex}에 스폰 위치가 설정되지 않았습니다.");
            return;
        }

        StopCarMotion();
        carTransform.SetPositionAndRotation(spawnPosition, spawnRotation);
    }

    void StopCarMotion()
    {
        if (carPhysics != null)
        {
            carPhysics.StopRunning();
        }

        Rigidbody rb = carTransform != null ? carTransform.GetComponent<Rigidbody>() : null;
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void SyncRestartInitialPosition()
    {
        if (buttonRestart == null)
        {
            return;
        }

        if (TryGetCurrentSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            buttonRestart.SetNewInitialPosition(spawnPosition, spawnRotation);
        }
    }

    bool TryGetSpawnPose(int mapIndex, out Vector3 position, out Quaternion rotation)
    {
        if (TryGetStaticSpawnPose(mapIndex, out position, out rotation))
        {
            return true;
        }

        if (TryGetRuntimeSpawnPose(mapIndex, out position, out rotation))
        {
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    bool TryGetMaterialByIndex(int mapIndex, out Material material)
    {
        int staticCount = StaticMapCount;
        if (mapIndex >= 0 && mapIndex < staticCount)
        {
            material = mapMaterials[mapIndex];
            return material != null;
        }

        int runtimeIndex = mapIndex - staticCount;
        if (runtimeMaps != null && runtimeIndex >= 0 && runtimeIndex < runtimeMaps.Count)
        {
            RuntimeMapEntry entry = runtimeMaps[runtimeIndex];
            material = entry != null ? entry.material : null;
            return material != null;
        }

        material = null;
        return false;
    }

    bool TryGetStaticSpawnPose(int mapIndex, out Vector3 position, out Quaternion rotation)
    {
        if (carSpawnPoses != null &&
            mapIndex >= 0 &&
            mapIndex < carSpawnPoses.Length)
        {
            SpawnPose spawnPose = carSpawnPoses[mapIndex];
            position = spawnPose.position;
            rotation = Quaternion.Euler(spawnPose.rotation);
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    bool TryGetRuntimeSpawnPose(int mapIndex, out Vector3 position, out Quaternion rotation)
    {
        int runtimeIndex = mapIndex - StaticMapCount;
        if (runtimeMaps != null && runtimeIndex >= 0 && runtimeIndex < runtimeMaps.Count)
        {
            RuntimeMapEntry entry = runtimeMaps[runtimeIndex];
            if (entry != null)
            {
                position = entry.spawnPose.position;
                rotation = Quaternion.Euler(entry.spawnPose.rotation);
                return true;
            }
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    int FindRuntimeMapIndexById(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId) || runtimeMaps == null)
        {
            return -1;
        }

        for (int i = 0; i < runtimeMaps.Count; i++)
        {
            RuntimeMapEntry entry = runtimeMaps[i];
            if (entry != null && string.Equals(entry.mapId, mapId, System.StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    void TryAutoFindReferences()
    {
        if (planeRenderer == null)
        {
            GameObject planeObj = GameObject.Find("Plane");
            if (planeObj != null)
            {
                planeRenderer = planeObj.GetComponent<Renderer>();
            }
        }

        if (carTransform == null)
        {
            GameObject carObj = GameObject.FindGameObjectWithTag("Car");
            if (carObj == null)
            {
                carObj = GameObject.Find("Car");
            }
            if (carObj != null)
            {
                carTransform = carObj.transform;
            }
        }

        if (carPhysics == null && carTransform != null)
        {
            carPhysics = carTransform.GetComponent<VirtualCarPhysics>();
        }

        if (carPhysics == null)
        {
            carPhysics = FindObjectOfType<VirtualCarPhysics>();
        }

        if (buttonRestart == null)
        {
            buttonRestart = FindObjectOfType<ButtonRestart>();
        }
    }

    static int NormalizeIndex(int index, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        int normalized = index % length;
        return normalized < 0 ? normalized + length : normalized;
    }

    static void SafeDestroyMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(material);
        }
        else
        {
            Object.DestroyImmediate(material);
        }
    }

    int GetCycleMapCount()
    {
        if (includeRuntimeMapsInCycle)
        {
            return TotalMapCount;
        }

        return StaticMapCount;
    }

    int ResolveNextIndexForCycle()
    {
        int cycleCount = GetCycleMapCount();
        if (cycleCount <= 0)
        {
            return 0;
        }

        // 원하는 순서:
        // 런타임 없음 -> 0..(정적끝)..0
        // 런타임 있음 -> 0..(정적끝)..(런타임끝)..0
        return NormalizeIndex(currentMapIndex + 1, cycleCount);
    }

    bool HasPersistentChangeToNextMapBinding(Button button)
    {
        if (button == null)
        {
            return false;
        }

        UnityEngine.Events.UnityEvent onClick = button.onClick;
        int persistentCount = onClick.GetPersistentEventCount();
        for (int i = 0; i < persistentCount; i++)
        {
            Object target = onClick.GetPersistentTarget(i);
            string methodName = onClick.GetPersistentMethodName(i);
            if (target == (Object)this && methodName == nameof(ChangeToNextMap))
            {
                return true;
            }
        }

        return false;
    }
}
