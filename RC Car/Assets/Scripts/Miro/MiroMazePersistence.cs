using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 생성된 미로 데이터를 JSON 파일로 저장/불러오기하는 모듈.
/// </summary>
public class MiroMazePersistence : MonoBehaviour
{
    [Header("File Settings")]
    [Tooltip("Application.persistentDataPath 아래에 저장할 파일명.")]
    public string fileName = "miro_latest.json";
    [Tooltip("디버깅을 위해 JSON을 사람이 읽기 쉬운 형태로 저장할지 여부.")]
    public bool prettyPrintJson = true;

    [Header("Debug")]
    [SerializeField] bool logPersistence = true;

    /// <summary>
    /// 현재 저장 파일의 절대 경로를 반환한다.
    /// </summary>
    public string GetSavePath()
    {
        string folder = Application.persistentDataPath;
        return Path.Combine(folder, fileName);
    }

    /// <summary>
    /// 미로 데이터를 UTF-8 JSON으로 저장한다.
    /// 임시 파일 작성 후 교체하는 방식으로 저장 안정성을 높인다.
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
    /// 최신 저장 JSON 파일을 불러오고 유효성을 검사한다.
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
    /// 저장 파일이 존재하면 삭제한다.
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
    /// 로드된 JSON 데이터가 렌더링 가능한 최소 조건을 만족하는지 검사한다.
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
    /// 임시 파일을 최종 저장 파일로 교체한다.
    /// Replace가 실패하면 삭제 후 Move로 안전하게 대체한다.
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
