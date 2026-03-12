using System.Collections;
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

    [Header("Behavior")]
    [Tooltip("Generate 실행 직후 자동 저장 여부.")]
    public bool autoSaveOnGenerate = true;
    [Tooltip("씬 시작 시 마지막 저장 미로를 자동 불러오기/렌더링할지 여부.")]
    public bool autoLoadOnStart = false;
    [Tooltip("MiroAlgorithm이 고정 시드여도 Generate 클릭마다 랜덤 시드를 강제할지 여부.")]
    public bool forceRandomSeedOnGenerate = true;

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
    }

    /// <summary>
    /// 옵션이 켜져 있으면 씬 시작 시 최신 저장 미로를 불러온다.
    /// </summary>
    void Start()
    {
        if (autoLoadOnStart)
        {
            LoadLatest();
        }
    }

    /// <summary>
    /// 새 미로를 생성하고 렌더링한 뒤, 옵션에 따라 즉시 저장한다.
    /// </summary>
    public void Generate()
    {
        if (algorithm == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Generate failed: algorithm reference is missing.");
            return;
        }

        currentMaze = algorithm.GenerateMazeData(forceRandomSeedOnGenerate);

        if (lineRenderer != null)
        {
            lineRenderer.Render(currentMaze);
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
                    SaveCurrentLocal();
                }

                return;
            }

            StartCoroutine(SaveCurrentRemoteRoutine(currentMaze));
            return;
        }

        SaveCurrentLocal();
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

            yield break;
        }

        Debug.LogWarning($"[MiroTestSceneController] Remote save failed. {remoteMessage}");
        if (useLocalFallback)
        {
            SaveMazeLocal(mazeToSave, "Remote save fallback");
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
    void SaveCurrentLocal()
    {
        SaveMazeLocal(currentMaze, "Local save");
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
        if (lineRenderer != null)
        {
            lineRenderer.Render(currentMaze);
        }
    }
}
