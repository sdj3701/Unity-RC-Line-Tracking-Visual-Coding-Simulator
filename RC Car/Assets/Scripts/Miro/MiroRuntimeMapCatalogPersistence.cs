using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 런타임 생성 맵 슬롯 메타데이터를 JSON으로 저장/복원한다.
/// </summary>
public class MiroRuntimeMapCatalogPersistence : MonoBehaviour
{
    [Serializable]
    public class RuntimeMapSlotData
    {
        public string mapId = "";
        public string displayName = "";
        public string texturePath = "";
        public string generatedAtUtc = "";
        public int mazeSize = 0;
        public int seed = 0;
        public Vector3 spawnPosition = Vector3.zero;
        public Vector3 spawnRotationEuler = Vector3.zero;
    }

    [Serializable]
    class RuntimeMapCatalogData
    {
        public int version = 1;
        public RuntimeMapSlotData[] slots = Array.Empty<RuntimeMapSlotData>();
    }

    [Header("File Settings")]
    [Tooltip("Application.persistentDataPath 하위 런타임 맵 카탈로그 파일명.")]
    public string fileName = "miro_runtime_map_catalog.json";
    [Tooltip("디버깅을 위해 JSON pretty print를 사용할지 여부.")]
    public bool prettyPrintJson = true;

    [Header("Debug")]
    [SerializeField] bool logPersistence = true;

    /// <summary>
    /// 카탈로그 파일 절대 경로를 반환한다.
    /// </summary>
    public string GetCatalogPath()
    {
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    /// <summary>
    /// 슬롯 목록을 카탈로그 JSON으로 저장한다.
    /// </summary>
    public bool SaveSlots(IReadOnlyList<RuntimeMapSlotData> slots)
    {
        RuntimeMapCatalogData catalog = new RuntimeMapCatalogData
        {
            version = 1,
            slots = slots != null ? ToArray(slots) : Array.Empty<RuntimeMapSlotData>()
        };

        try
        {
            string path = GetCatalogPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(catalog, prettyPrintJson);
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            ReplaceFile(tempPath, path);

            if (logPersistence)
            {
                Debug.Log($"[MiroRuntimeMapCatalogPersistence] Saved catalog ({catalog.slots.Length} slots): {path}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MiroRuntimeMapCatalogPersistence] Save failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 카탈로그 JSON을 읽어 슬롯 목록으로 복원한다.
    /// </summary>
    public bool TryLoadSlots(out List<RuntimeMapSlotData> slots)
    {
        slots = new List<RuntimeMapSlotData>();
        string path = GetCatalogPath();
        if (!File.Exists(path))
        {
            if (logPersistence)
            {
                Debug.Log($"[MiroRuntimeMapCatalogPersistence] Catalog file not found: {path}");
            }

            return false;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            RuntimeMapCatalogData catalog = JsonUtility.FromJson<RuntimeMapCatalogData>(json);
            if (catalog == null || catalog.slots == null)
            {
                return false;
            }

            for (int i = 0; i < catalog.slots.Length; i++)
            {
                RuntimeMapSlotData slot = catalog.slots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.texturePath))
                {
                    continue;
                }

                slots.Add(slot);
            }

            if (logPersistence)
            {
                Debug.Log($"[MiroRuntimeMapCatalogPersistence] Loaded catalog ({slots.Count} slots): {path}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MiroRuntimeMapCatalogPersistence] Load failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 카탈로그 파일을 삭제한다.
    /// </summary>
    public bool DeleteCatalog()
    {
        string path = GetCatalogPath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            if (logPersistence)
            {
                Debug.Log($"[MiroRuntimeMapCatalogPersistence] Deleted catalog: {path}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MiroRuntimeMapCatalogPersistence] Delete failed: {ex.Message}");
            return false;
        }
    }

    static RuntimeMapSlotData[] ToArray(IReadOnlyList<RuntimeMapSlotData> slots)
    {
        RuntimeMapSlotData[] array = new RuntimeMapSlotData[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            array[i] = slots[i];
        }

        return array;
    }

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
