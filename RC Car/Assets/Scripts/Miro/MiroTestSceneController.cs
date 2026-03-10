using UnityEngine;

/// <summary>
/// Orchestrates maze generation, rendering, and persistence for Miro Test Scene button workflows.
/// </summary>
public class MiroTestSceneController : MonoBehaviour
{
    [Header("References")]
    public MiroAlgorithm algorithm;
    public MiroLineRenderer lineRenderer;
    public MiroMazePersistence persistence;

    [Header("Behavior")]
    [Tooltip("Automatically save right after Generate() is executed.")]
    public bool autoSaveOnGenerate = true;
    [Tooltip("Automatically load and render latest saved maze when scene starts.")]
    public bool autoLoadOnStart = false;
    [Tooltip("Force random seed on each Generate button click even when MiroAlgorithm uses fixed seed.")]
    public bool forceRandomSeedOnGenerate = true;

    [Header("Debug")]
    [SerializeField] bool logControllerActions = true;
    [SerializeField] MiroMazeData currentMaze;

    /// <summary>
    /// Auto-fills local component references when this script is added or reset in Inspector.
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
    /// Optionally loads the latest saved maze at scene start.
    /// </summary>
    void Start()
    {
        if (autoLoadOnStart)
        {
            LoadLatest();
        }
    }

    /// <summary>
    /// Generates a new maze, renders it, and optionally saves it immediately.
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
    /// Saves the currently generated maze to persistent JSON storage.
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
    /// Loads the latest saved maze JSON and renders it.
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
    /// Clears generated line objects from scene without deleting save file.
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
    /// Clears rendered lines and removes latest saved JSON file.
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
