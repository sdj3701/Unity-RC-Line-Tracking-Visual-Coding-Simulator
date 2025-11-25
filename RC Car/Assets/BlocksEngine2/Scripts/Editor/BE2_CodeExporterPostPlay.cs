#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

// Copies the generated script from Play Mode temp location into Assets when Play stops
[InitializeOnLoad]
public static class BE2_CodeExporterPostPlay
{
    static BE2_CodeExporterPostPlay()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        // In case we reloaded domain while already back in Edit mode, try once.
        TryCopyIfPending();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            TryCopyIfPending();
        }
    }

    static void TryCopyIfPending()
    {
        string tempPath = EditorPrefs.GetString("BE2_CodeExporter_LastTempPath", string.Empty);
        string relAssetPath = EditorPrefs.GetString("BE2_CodeExporter_LastRelAssetPath", string.Empty);
        string tempXmlPath = EditorPrefs.GetString("BE2_CodeExporter_LastTempXmlPath", string.Empty);
        string relXmlAssetPath = EditorPrefs.GetString("BE2_CodeExporter_LastRelXmlAssetPath", string.Empty);
        if (string.IsNullOrEmpty(tempPath) || string.IsNullOrEmpty(relAssetPath))
        {
            // Even if code path is missing, we may still have XML to copy
            if (string.IsNullOrEmpty(tempXmlPath) || string.IsNullOrEmpty(relXmlAssetPath))
                return;
        }

        try
        {
            // Copy C# script if present
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                string fullPath = relAssetPath;
                if (!Path.IsPathRooted(fullPath))
                {
                    if (relAssetPath.StartsWith("Assets/") || relAssetPath.StartsWith("Assets\\"))
                    {
                        string sub = relAssetPath.Substring(7);
                        fullPath = Path.Combine(Application.dataPath, sub);
                    }
                    else
                    {
                        fullPath = Path.Combine(Application.dataPath, relAssetPath);
                    }
                }

                var dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.Copy(tempPath, fullPath, true);
                Debug.Log($"[BE2_CodeExporter] Copied generated script from PlayMode to: {fullPath}");
            }

            // Copy XML if present
            if (!string.IsNullOrEmpty(tempXmlPath) && File.Exists(tempXmlPath))
            {
                string fullXmlPath = relXmlAssetPath;
                if (!Path.IsPathRooted(fullXmlPath))
                {
                    if (relXmlAssetPath.StartsWith("Assets/") || relXmlAssetPath.StartsWith("Assets\\"))
                    {
                        string sub = relXmlAssetPath.Substring(7);
                        fullXmlPath = Path.Combine(Application.dataPath, sub);
                    }
                    else
                    {
                        fullXmlPath = Path.Combine(Application.dataPath, relXmlAssetPath);
                    }
                }

                var xmlDir = Path.GetDirectoryName(fullXmlPath);
                if (!Directory.Exists(xmlDir)) Directory.CreateDirectory(xmlDir);

                File.Copy(tempXmlPath, fullXmlPath, true);
                Debug.Log($"[BE2_CodeExporter] Copied generated XML from PlayMode to: {fullXmlPath}");
            }

            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[BE2_CodeExporter] Post-Play copy failed: {ex.Message}");
        }
        finally
        {
            EditorPrefs.DeleteKey("BE2_CodeExporter_LastTempPath");
            EditorPrefs.DeleteKey("BE2_CodeExporter_LastRelAssetPath");
            EditorPrefs.DeleteKey("BE2_CodeExporter_LastTempXmlPath");
            EditorPrefs.DeleteKey("BE2_CodeExporter_LastRelXmlAssetPath");
        }
    }
}
#endif
