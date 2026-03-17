using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Miro Test Scene의 버튼 워크플로우(생성/렌더링/저장/불러오기)를 제어한다.
/// Remote DB를 비활성화하면 기존 Local JSON 저장/로드만 동작한다.
/// </summary>
public class MiroTestSceneController : MonoBehaviour
{
    [Header("References")]
    public MiroAlgorithm algorithm;
    public MiroLineRenderer lineRenderer;
    public MiroMazePersistence persistence;
    public MiroMazeRemoteRepository remoteRepository;

    [Header("Plane Merge")]
    [Tooltip("Miro 데이터를 Plane 텍스처로 베이크해 ChangeMap 런타임 맵으로 등록한다.")]
    public bool enablePlaneMapMerge = true;
    public MiroMapBaker mapBaker;
    public ChangeMap changeMap;
    public MiroRuntimeMapCatalogPersistence runtimeMapCatalogPersistence;

    [Header("Behavior")]
    [Tooltip("Generate 실행 직후 자동 저장 여부.")]
    public bool autoSaveOnGenerate = false;
    [Tooltip("씬 시작 시 마지막 저장 미로를 자동 불러오기/렌더링할지 여부.")]
    public bool autoLoadOnStart = false;
    [Tooltip("MiroAlgorithm이 고정 시드여도 Generate 클릭마다 랜덤 시드를 강제할지 여부.")]
    public bool forceRandomSeedOnGenerate = true;

    [Header("Runtime Map Catalog")]
    [Tooltip("씬 시작 시 런타임 맵 카탈로그를 읽어 ChangeMap에 복원한다.")]
    public bool autoRestoreRuntimeMapsOnStart = true;
    [Tooltip("카탈로그 복원 전에 기존 런타임 맵을 비우고 다시 채운다.")]
    public bool clearRuntimeMapsBeforeRestore = true;
    [Tooltip("Save 성공 시 ChangeMap 런타임 맵으로 자동 등록한다.")]
    public bool registerMapToChangeMapOnSave = true;
    [Tooltip("Generate 직후 ChangeMap 런타임 맵으로 자동 등록한다.")]
    public bool registerMapToChangeMapOnGenerate = true;
    [Tooltip("Generate 미리보기를 디스크(PNG/카탈로그)로 저장할지 여부. false면 런타임 메모리에만 등록한다.")]
    public bool persistGeneratePreviewToDisk = false;
    [Tooltip("Load 성공 시 ChangeMap 런타임 맵으로 자동 등록한다.")]
    public bool registerMapToChangeMapOnLoad = true;
    [Tooltip("Generate로 등록된 맵을 즉시 현재 맵으로 적용한다.")]
    public bool applyRegisteredMapImmediatelyOnGenerate = false;
    [Tooltip("Save/Load로 등록된 맵을 즉시 현재 맵으로 적용한다.")]
    public bool applyRegisteredMapImmediatelyOnSaveLoad = false;
    [Tooltip("런타임 맵 등록 시 텍스처 PNG를 파일로 저장한다.")]
    public bool saveRuntimeTexturePng = true;
    [Tooltip("런타임 맵 등록 직후 카탈로그 JSON을 저장한다.")]
    public bool saveRuntimeMapCatalogOnRegister = true;

    [Header("Remote DB")]
    [Tooltip("true면 Remote DB(API) 저장/불러오기를 활성화하고, false면 기존 Local JSON만 사용한다.")]
    public bool enableRemoteDb = false;
    [Tooltip("Remote DB 저장/로드 실패 시 Local JSON으로 자동 폴백할지 여부.")]
    public bool useLocalFallback = true;
    [Tooltip("Remote DB 저장 성공 후 Local JSON 캐시도 함께 저장할지 여부.")]
    public bool updateLocalCacheAfterRemoteSave = true;
    [Tooltip("Remote DB 로드 성공 후 Local JSON 캐시도 함께 저장할지 여부.")]
    public bool updateLocalCacheAfterRemoteLoad = true;

    [Header("Debug")]
    [SerializeField] bool logControllerActions = true;
    [SerializeField] MiroMazeData currentMaze;
    [SerializeField] List<MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData> runtimeMapSlots = new List<MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData>();

    /// <summary>
    /// 스크립트 추가/Reset 시 같은 오브젝트의 참조 컴포넌트를 자동 연결한다.
    /// </summary>
    void Reset()
    {
        if (algorithm == null)
        {
            algorithm = GetComponent<MiroAlgorithm>();
        }

        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<MiroLineRenderer>();
        }

        if (persistence == null)
        {
            persistence = GetComponent<MiroMazePersistence>();
        }

        if (remoteRepository == null)
        {
            remoteRepository = GetComponent<MiroMazeRemoteRepository>();
        }

        if (mapBaker == null)
        {
            mapBaker = GetComponent<MiroMapBaker>();
        }

        if (runtimeMapCatalogPersistence == null)
        {
            runtimeMapCatalogPersistence = GetComponent<MiroRuntimeMapCatalogPersistence>();
        }

        if (changeMap == null)
        {
            changeMap = FindObjectOfType<ChangeMap>();
        }

        SyncGroundReferencePlane();
    }

    /// <summary>
    /// 옵션이 켜져 있으면 카탈로그 복원 및 최신 저장 미로를 로드한다.
    /// </summary>
    void Start()
    {
        SyncGroundReferencePlane();

        if (autoRestoreRuntimeMapsOnStart)
        {
            RestoreRuntimeMapsFromCatalog();
        }

        if (autoLoadOnStart)
        {
            LoadLatest();
        }
    }

    /// <summary>
    /// 새 미로를 생성하고 렌더링한 뒤, 옵션에 따라 Plane 맵 등록/저장을 수행한다.
    /// </summary>
    public void Generate()
    {
        if (algorithm == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Generate failed: algorithm reference is missing.");
            return;
        }

        currentMaze = algorithm.GenerateMazeData(forceRandomSeedOnGenerate);
        RenderCurrentMaze();

        if (registerMapToChangeMapOnGenerate)
        {
            RegisterCurrentMazeToPlaneMapInternal("Generate", applyRegisteredMapImmediatelyOnGenerate, persistGeneratePreviewToDisk);
        }

        if (autoSaveOnGenerate)
        {
            SaveCurrent();
        }

        if (logControllerActions)
        {
            Debug.Log("[MiroTestSceneController] Generate completed.");
        }
    }

    /// <summary>
    /// 현재 생성된 미로를 저장한다.
    /// enableRemoteDb=true이면 Remote DB를 우선 시도하고, false이면 Local만 사용한다.
    /// </summary>
    public void SaveCurrent()
    {
        if (currentMaze == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Save skipped: no current maze data.");
            return;
        }

        if (enableRemoteDb)
        {
            if (remoteRepository == null)
            {
                Debug.LogWarning("[MiroTestSceneController] Remote save is enabled but remoteRepository reference is missing.");
                if (useLocalFallback)
                {
                    bool localSaved = SaveCurrentLocal();
                    if (localSaved)
                    {
                        RegisterAfterSaveIfNeeded();
                    }
                }

                return;
            }

            StartCoroutine(SaveCurrentRemoteRoutine(currentMaze));
            return;
        }

        bool saved = SaveCurrentLocal();
        if (saved)
        {
            RegisterAfterSaveIfNeeded();
        }
    }

    /// <summary>
    /// 최신 저장 미로를 불러온다.
    /// enableRemoteDb=true이면 Remote DB를 우선 시도하고, false이면 Local만 사용한다.
    /// </summary>
    public void LoadLatest()
    {
        if (enableRemoteDb)
        {
            if (remoteRepository == null)
            {
                Debug.LogWarning("[MiroTestSceneController] Remote load is enabled but remoteRepository reference is missing.");
                if (useLocalFallback)
                {
                    LoadLatestLocal();
                }
            }
            else
            {
                StartCoroutine(LoadLatestRemoteRoutine());
            }

            return;
        }

        LoadLatestLocal();
    }

    /// <summary>
    /// Remote DB 연결 상태를 수동으로 점검한다.
    /// </summary>
    public void TestRemoteDbConnection()
    {
        if (!enableRemoteDb)
        {
            if (logControllerActions)
            {
                Debug.Log("[MiroTestSceneController] TestRemoteDbConnection skipped because enableRemoteDb is false.");
            }

            return;
        }

        if (remoteRepository == null)
        {
            Debug.LogWarning("[MiroTestSceneController] TestRemoteDbConnection failed: remoteRepository reference is missing.");
            return;
        }

        StartCoroutine(TestRemoteDbConnectionRoutine());
    }

    /// <summary>
    /// 현재 currentMaze를 수동으로 Plane 런타임 맵에 등록한다.
    /// </summary>
    public void RegisterCurrentMazeToPlaneMap()
    {
        RegisterCurrentMazeToPlaneMapInternal("Manual", applyRegisteredMapImmediatelyOnSaveLoad);
    }

    /// <summary>
    /// 저장된 카탈로그를 수동으로 다시 읽어 ChangeMap 런타임 맵을 복원한다.
    /// </summary>
    public void RestoreRuntimeMapsFromCatalog()
    {
        if (!enablePlaneMapMerge)
        {
            return;
        }

        if (mapBaker == null || changeMap == null || runtimeMapCatalogPersistence == null)
        {
            if (logControllerActions)
            {
                Debug.Log("[MiroTestSceneController] RestoreRuntimeMapsFromCatalog skipped: required references are missing.");
            }

            return;
        }

        if (clearRuntimeMapsBeforeRestore)
        {
            changeMap.ClearRuntimeMaps();
        }

        runtimeMapSlots.Clear();
        bool loaded = runtimeMapCatalogPersistence.TryLoadSlots(out List<MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData> loadedSlots);
        if (!loaded || loadedSlots == null || loadedSlots.Count == 0)
        {
            if (logControllerActions)
            {
                Debug.Log("[MiroTestSceneController] Runtime map catalog is empty or missing.");
            }

            return;
        }

        int restoredCount = 0;
        for (int i = 0; i < loadedSlots.Count; i++)
        {
            MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData slot = loadedSlots[i];
            if (slot == null)
            {
                continue;
            }

            if (!mapBaker.TryLoadTexturePng(slot.texturePath, out Texture2D texture, out string loadMessage))
            {
                Debug.LogWarning($"[MiroTestSceneController] Runtime map restore skipped: {loadMessage}");
                continue;
            }

            Material runtimeMaterial = mapBaker.CreateRuntimeMaterial(texture, $"MiroRuntime_{slot.mapId}");
            if (runtimeMaterial == null)
            {
                continue;
            }

            ChangeMap.SpawnPose spawnPose = new ChangeMap.SpawnPose
            {
                position = slot.spawnPosition,
                rotation = slot.spawnRotationEuler
            };

            int mapIndex = changeMap.AddRuntimeMap(runtimeMaterial, spawnPose, slot.mapId, slot.displayName, applyImmediately: false);
            if (mapIndex < 0)
            {
                continue;
            }

            runtimeMapSlots.Add(slot);
            restoredCount++;
        }

        if (logControllerActions)
        {
            Debug.Log($"[MiroTestSceneController] Runtime map catalog restored: {restoredCount}/{loadedSlots.Count}");
        }
    }

    /// <summary>
    /// 저장 파일은 유지한 채 씬의 생성 라인 오브젝트만 제거한다.
    /// </summary>
    public void ClearLines()
    {
        if (lineRenderer != null)
        {
            lineRenderer.ClearRendered();
        }

        if (logControllerActions)
        {
            Debug.Log("[MiroTestSceneController] Cleared rendered line objects.");
        }
    }

    /// <summary>
    /// 렌더링 라인을 제거하고 최신 저장 JSON 파일도 함께 삭제한다.
    /// </summary>
    public void ClearLinesAndDeleteSave()
    {
        ClearLines();

        if (persistence != null)
        {
            persistence.DeleteSave();
        }
    }

    /// <summary>
    /// Remote DB 저장 루틴을 실행하고, 실패 시 옵션에 따라 Local 저장으로 폴백한다.
    /// </summary>
    IEnumerator SaveCurrentRemoteRoutine(MiroMazeData mazeToSave)
    {
        bool callbackInvoked = false;
        bool remoteSaved = false;
        string remoteMessage = "";

        yield return remoteRepository.SaveMaze(mazeToSave, (success, message) =>
        {
            callbackInvoked = true;
            remoteSaved = success;
            remoteMessage = message;
        });

        if (!callbackInvoked)
        {
            remoteSaved = false;
            remoteMessage = "Remote save callback was not invoked.";
        }

        bool finalSaved = remoteSaved;
        if (remoteSaved)
        {
            if (logControllerActions)
            {
                Debug.Log($"[MiroTestSceneController] Remote save completed. {remoteMessage}");
            }

            if (updateLocalCacheAfterRemoteSave)
            {
                SaveMazeLocal(mazeToSave, "Remote save cache");
            }
        }
        else
        {
            Debug.LogWarning($"[MiroTestSceneController] Remote save failed. {remoteMessage}");
            if (useLocalFallback)
            {
                bool fallbackSaved = SaveMazeLocal(mazeToSave, "Remote save fallback");
                finalSaved = fallbackSaved;
            }
        }

        if (finalSaved)
        {
            RegisterAfterSaveIfNeeded();
        }
    }

    /// <summary>
    /// Remote DB에서 최신 미로를 불러오고, 실패 시 옵션에 따라 Local 로드로 폴백한다.
    /// </summary>
    IEnumerator LoadLatestRemoteRoutine()
    {
        bool callbackInvoked = false;
        bool remoteLoaded = false;
        string remoteMessage = "";
        MiroMazeData remoteMaze = null;

        yield return remoteRepository.TryLoadLatest((success, loadedMaze, message) =>
        {
            callbackInvoked = true;
            remoteLoaded = success;
            remoteMaze = loadedMaze;
            remoteMessage = message;
        });

        if (!callbackInvoked)
        {
            remoteLoaded = false;
            remoteMessage = "Remote load callback was not invoked.";
        }

        if (remoteLoaded && remoteMaze != null)
        {
            ApplyLoadedMaze(remoteMaze);

            if (updateLocalCacheAfterRemoteLoad)
            {
                SaveMazeLocal(remoteMaze, "Remote load cache");
            }

            RegisterAfterLoadIfNeeded();

            if (logControllerActions)
            {
                Debug.Log($"[MiroTestSceneController] Remote load completed. {remoteMessage}");
            }

            yield break;
        }

        Debug.LogWarning($"[MiroTestSceneController] Remote load failed. {remoteMessage}");
        if (useLocalFallback)
        {
            LoadLatestLocal();
        }
    }

    /// <summary>
    /// Remote DB 연결 상태 확인 루틴을 실행해 로그로 결과를 남긴다.
    /// </summary>
    IEnumerator TestRemoteDbConnectionRoutine()
    {
        bool callbackInvoked = false;
        bool connected = false;
        string message = "";

        yield return remoteRepository.TestConnection((success, resultMessage) =>
        {
            callbackInvoked = true;
            connected = success;
            message = resultMessage;
        });

        if (!callbackInvoked)
        {
            connected = false;
            message = "Remote connection callback was not invoked.";
        }

        if (connected)
        {
            Debug.Log($"[MiroTestSceneController] Remote DB connection test succeeded. {message}");
        }
        else
        {
            Debug.LogWarning($"[MiroTestSceneController] Remote DB connection test failed. {message}");
        }
    }

    /// <summary>
    /// 현재 currentMaze를 Local JSON 파일로 저장한다.
    /// </summary>
    bool SaveCurrentLocal()
    {
        return SaveMazeLocal(currentMaze, "Local save");
    }

    /// <summary>
    /// 지정한 미로 데이터를 Local JSON 파일로 저장한다.
    /// </summary>
    bool SaveMazeLocal(MiroMazeData data, string contextLabel)
    {
        if (persistence == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Save failed: persistence reference is missing.");
            return false;
        }

        if (data == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Save skipped: maze data is null.");
            return false;
        }

        bool saved = persistence.SaveMaze(data);
        if (logControllerActions)
        {
            Debug.Log(saved
                ? $"[MiroTestSceneController] {contextLabel} completed."
                : $"[MiroTestSceneController] {contextLabel} failed.");
        }

        return saved;
    }

    /// <summary>
    /// Local JSON 파일에서 최신 미로를 불러오고 성공 시 즉시 렌더링한다.
    /// </summary>
    void LoadLatestLocal()
    {
        if (persistence == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Load failed: persistence reference is missing.");
            return;
        }

        bool loaded = persistence.TryLoadLatest(out MiroMazeData loadedMaze);
        if (!loaded)
        {
            if (logControllerActions)
            {
                Debug.Log("[MiroTestSceneController] Load skipped: no valid save file found.");
            }

            return;
        }

        ApplyLoadedMaze(loadedMaze);
        RegisterAfterLoadIfNeeded();

        if (logControllerActions)
        {
            Debug.Log("[MiroTestSceneController] Local load completed.");
        }
    }

    /// <summary>
    /// 전달받은 미로 데이터를 현재 상태에 반영하고 화면에 렌더링한다.
    /// </summary>
    void ApplyLoadedMaze(MiroMazeData loadedMaze)
    {
        if (loadedMaze == null)
        {
            return;
        }

        currentMaze = loadedMaze;
        RenderCurrentMaze();
    }

    void RenderCurrentMaze()
    {
        if (lineRenderer != null && currentMaze != null)
        {
            lineRenderer.Render(currentMaze);
        }
    }

    void RegisterAfterSaveIfNeeded()
    {
        if (!registerMapToChangeMapOnSave)
        {
            return;
        }

        RegisterCurrentMazeToPlaneMapInternal("Save", applyRegisteredMapImmediatelyOnSaveLoad);
    }

    void RegisterAfterLoadIfNeeded()
    {
        if (!registerMapToChangeMapOnLoad)
        {
            return;
        }

        RegisterCurrentMazeToPlaneMapInternal("Load", applyRegisteredMapImmediatelyOnSaveLoad);
    }

    bool RegisterCurrentMazeToPlaneMapInternal(string sourceLabel, bool applyImmediately, bool persistToDisk = true)
    {
        if (!enablePlaneMapMerge)
        {
            return false;
        }

        if (currentMaze == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Register map skipped: currentMaze is null.");
            return false;
        }

        if (mapBaker == null || changeMap == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Register map skipped: mapBaker or changeMap is missing.");
            return false;
        }

        if (!mapBaker.TryBake(currentMaze, out Texture2D texture, out string bakeMessage))
        {
            Debug.LogWarning($"[MiroTestSceneController] Register map bake failed: {bakeMessage}");
            return false;
        }

        string mapId = BuildMapId(currentMaze, sourceLabel);
        Material runtimeMaterial = mapBaker.CreateRuntimeMaterial(texture, $"MiroRuntime_{mapId}");
        if (runtimeMaterial == null)
        {
            return false;
        }

        string texturePath = "";
        if (persistToDisk && saveRuntimeTexturePng)
        {
            bool savedTexture = mapBaker.TrySaveTexturePng(texture, out texturePath, out string textureSaveMessage);
            if (!savedTexture)
            {
                Debug.LogWarning($"[MiroTestSceneController] Texture save failed: {textureSaveMessage}");
            }
        }

        ChangeMap.SpawnPose spawnPose = ResolveRuntimeMapSpawnPose();
        string displayName = BuildMapDisplayName(currentMaze, sourceLabel);
        int mapIndex = changeMap.AddRuntimeMap(runtimeMaterial, spawnPose, mapId, displayName, applyImmediately);
        if (mapIndex < 0)
        {
            return false;
        }

        if (persistToDisk)
        {
            UpsertRuntimeSlot(new MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData
            {
                mapId = mapId,
                displayName = displayName,
                texturePath = texturePath,
                generatedAtUtc = string.IsNullOrWhiteSpace(currentMaze.generatedAtUtc) ? DateTime.UtcNow.ToString("o") : currentMaze.generatedAtUtc,
                mazeSize = currentMaze.mazeSize,
                seed = currentMaze.seed,
                spawnPosition = spawnPose.position,
                spawnRotationEuler = spawnPose.rotation
            });

            if (saveRuntimeMapCatalogOnRegister)
            {
                SaveRuntimeMapCatalog();
            }
        }

        if (logControllerActions)
        {
            Debug.Log($"[MiroTestSceneController] Registered runtime map from {sourceLabel}. mapId={mapId}, index={mapIndex}");
        }

        return true;
    }

    ChangeMap.SpawnPose ResolveRuntimeMapSpawnPose()
    {
        ChangeMap.SpawnPose pose = new ChangeMap.SpawnPose
        {
            position = Vector3.zero,
            rotation = Vector3.zero
        };

        if (changeMap != null && changeMap.TryGetRuntimeDefaultSpawnPose(out Vector3 runtimeDefaultPosition, out Quaternion runtimeDefaultRotation))
        {
            pose.position = runtimeDefaultPosition;
            pose.rotation = runtimeDefaultRotation.eulerAngles;
            return pose;
        }

        if (changeMap != null && changeMap.TryGetCurrentSpawnPose(out Vector3 spawnPosition, out Quaternion spawnRotation))
        {
            pose.position = spawnPosition;
            pose.rotation = spawnRotation.eulerAngles;
            return pose;
        }

        if (changeMap != null && changeMap.carTransform != null)
        {
            pose.position = changeMap.carTransform.position;
            pose.rotation = changeMap.carTransform.rotation.eulerAngles;
        }

        return pose;
    }

    void UpsertRuntimeSlot(MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData newSlot)
    {
        if (newSlot == null)
        {
            return;
        }

        if (runtimeMapSlots == null)
        {
            runtimeMapSlots = new List<MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData>();
        }

        int existingIndex = FindRuntimeSlotIndexById(newSlot.mapId);
        if (existingIndex >= 0)
        {
            runtimeMapSlots[existingIndex] = newSlot;
        }
        else
        {
            runtimeMapSlots.Add(newSlot);
        }
    }

    int FindRuntimeSlotIndexById(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId) || runtimeMapSlots == null)
        {
            return -1;
        }

        for (int i = 0; i < runtimeMapSlots.Count; i++)
        {
            MiroRuntimeMapCatalogPersistence.RuntimeMapSlotData slot = runtimeMapSlots[i];
            if (slot != null && string.Equals(slot.mapId, mapId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    void SaveRuntimeMapCatalog()
    {
        if (runtimeMapCatalogPersistence == null)
        {
            return;
        }

        runtimeMapCatalogPersistence.SaveSlots(runtimeMapSlots);
    }

    string BuildMapId(MiroMazeData maze, string sourceLabel)
    {
        string utc = string.IsNullOrWhiteSpace(maze.generatedAtUtc)
            ? DateTime.UtcNow.ToString("o")
            : maze.generatedAtUtc;

        string normalizedTime = utc.Replace(":", "-").Replace("/", "-").Replace(" ", "_");
        return $"miro_{maze.mazeSize}_{maze.seed}_{normalizedTime}";
    }

    string BuildMapDisplayName(MiroMazeData maze, string sourceLabel)
    {
        string generated = string.IsNullOrWhiteSpace(maze.generatedAtUtc)
            ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            : maze.generatedAtUtc;

        return $"{sourceLabel}_size{maze.mazeSize}_seed{maze.seed}_{generated}";
    }

    void SyncGroundReferencePlane()
    {
        if (lineRenderer == null || changeMap == null || lineRenderer.groundReferencePlane != null)
        {
            return;
        }

        if (changeMap.planeRenderer != null)
        {
            lineRenderer.groundReferencePlane = changeMap.planeRenderer.transform;
        }
    }
}
