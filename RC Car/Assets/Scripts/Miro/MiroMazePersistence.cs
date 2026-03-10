using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Handles JSON persistence for generated maze data.
/// </summary>
public class MiroMazePersistence : MonoBehaviour
{
    [Header("File Settings")]
    [Tooltip("File name used under Application.persistentDataPath.")]
    public string fileName = "miro_latest.json";
    [Tooltip("Pretty-print JSON file for easier debugging.")]
    public bool prettyPrintJson = true;

    [Header("Debug")]
    [SerializeField] bool logPersistence = true;

    /// <summary>
    /// Returns absolute path of the current save file.
    /// </summary>
    public string GetSavePath()
    {
        string folder = Application.persistentDataPath;
        return Path.Combine(folder, fileName);
    }

    /// <summary>
    /// Saves maze data as JSON using UTF-8 and a temp-file replace strategy.
    /// </summary>
    public bool SaveMaze(MiroMazeData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[MiroMazePersistence] Save skipped because data is null.");
            return false;
        }

        try
        {
            string savePath = GetSavePath();
            string directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(data, prettyPrintJson);
            string tempPath = savePath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            ReplaceFile(tempPath, savePath);

            if (logPersistence)
            {
                Debug.Log($"[MiroMazePersistence] Saved maze JSON to: {savePath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MiroMazePersistence] Save failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads the most recent maze JSON file and validates the result.
    /// </summary>
    public bool TryLoadLatest(out MiroMazeData data)
    {
        data = null;
        string savePath = GetSavePath();

        if (!File.Exists(savePath))
        {
            if (logPersistence)
            {
                Debug.Log($"[MiroMazePersistence] Save file not found: {savePath}");
            }

            return false;
        }

        try
        {
            string json = File.ReadAllText(savePath, Encoding.UTF8);
            MiroMazeData loaded = JsonUtility.FromJson<MiroMazeData>(json);
            if (!ValidateLoadedData(loaded))
            {
                Debug.LogWarning("[MiroMazePersistence] Loaded maze JSON failed validation.");
                return false;
            }

            data = loaded;

            if (logPersistence)
            {
                Debug.Log($"[MiroMazePersistence] Loaded maze JSON from: {savePath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MiroMazePersistence] Load failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes the current save file if it exists.
    /// </summary>
    public bool DeleteSave()
    {
        string savePath = GetSavePath();
        if (!File.Exists(savePath))
        {
            return false;
        }

        try
        {
            File.Delete(savePath);
            if (logPersistence)
            {
                Debug.Log($"[MiroMazePersistence] Deleted save file: {savePath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MiroMazePersistence] Delete failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Validates required fields after loading JSON to prevent invalid rendering data.
    /// </summary>
    bool ValidateLoadedData(MiroMazeData loaded)
    {
        if (loaded == null)
        {
            return false;
        }

        if (loaded.mazeSize < 5)
        {
            return false;
        }

        if (loaded.cells == null)
        {
            return false;
        }

        int expectedCellCount = loaded.mazeSize * loaded.mazeSize;
        if (loaded.cells.Length != expectedCellCount)
        {
            return false;
        }

        if (loaded.cellStepX <= 0f || loaded.cellStepZ <= 0f)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Replaces destination file with temp file and falls back to safe overwrite when replace is unavailable.
    /// </summary>
    void ReplaceFile(string tempPath, string destinationPath)
    {
        if (!File.Exists(tempPath))
        {
            return;
        }

        try
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(tempPath, destinationPath, null);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }
        }
        catch
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }
    }
}
