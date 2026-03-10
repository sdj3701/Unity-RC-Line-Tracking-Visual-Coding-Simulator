using UnityEngine;

/// <summary>
/// Miro Test Scene의 버튼 워크플로우(생성/렌더링/저장/불러오기)를 제어한다.
/// </summary>
public class MiroTestSceneController : MonoBehaviour
{
    [Header("References")]
    public MiroAlgorithm algorithm;
    public MiroLineRenderer lineRenderer;
    public MiroMazePersistence persistence;

    [Header("Behavior")]
    [Tooltip("Generate 실행 직후 자동 저장 여부.")]
    public bool autoSaveOnGenerate = true;
    [Tooltip("씬 시작 시 마지막 저장 미로를 자동 불러오기/렌더링할지 여부.")]
    public bool autoLoadOnStart = false;
    [Tooltip("MiroAlgorithm이 고정 시드여도 Generate 클릭마다 랜덤 시드를 강제할지 여부.")]
    public bool forceRandomSeedOnGenerate = true;

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
    /// 현재 생성된 미로를 영구 JSON 저장소에 저장한다.
    /// </summary>
    public void SaveCurrent()
    {
        if (persistence == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Save failed: persistence reference is missing.");
            return;
        }

        if (currentMaze == null)
        {
            Debug.LogWarning("[MiroTestSceneController] Save skipped: no current maze data.");
            return;
        }

        bool saved = persistence.SaveMaze(currentMaze);
        if (logControllerActions)
        {
            Debug.Log(saved
                ? "[MiroTestSceneController] Save completed."
                : "[MiroTestSceneController] Save failed.");
        }
    }

    /// <summary>
    /// 최신 저장 미로 JSON을 불러와 즉시 렌더링한다.
    /// </summary>
    public void LoadLatest()
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

        currentMaze = loadedMaze;
        if (lineRenderer != null)
        {
            lineRenderer.Render(currentMaze);
        }

        if (logControllerActions)
        {
            Debug.Log("[MiroTestSceneController] Load completed.");
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
}
