using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 미로 셀 데이터를 Plane 적용용 텍스처/머티리얼로 변환한다.
/// </summary>
public class MiroMapBaker : MonoBehaviour
{
    [Header("Texture")]
    [Min(2)] public int pixelsPerCell = 16;
    [Range(0.05f, 1f)] public float pathWidthRatio = 1f;
    [Min(1)] public int minPathWidthPixels = 1;
    [Tooltip("true면 MainPath('*')를 텍스처에 그린다.")]
    public bool bakeMainPath = true;
    [Tooltip("true면 BranchPath('.')를 텍스처에 그린다.")]
    public bool bakeBranchPath = true;
    [Tooltip("텍스처 세로 방향을 뒤집어 Plane 좌표계와 맞춘다.")]
    public bool flipVertical = false;
    [Tooltip("런타임 맵 머티리얼 적용 시 텍스처를 180도 회전해 맞출지 여부.")]
    public bool rotateRuntimeTexture180 = true;
    public FilterMode textureFilterMode = FilterMode.Bilinear;
    public TextureWrapMode textureWrapMode = TextureWrapMode.Clamp;
    public Color backgroundColor = Color.white;
    public Color mainPathColor = Color.black;
    public Color branchPathColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Persistence")]
    [Tooltip("런타임 생성 텍스처 PNG를 저장할 폴더명(Application.persistentDataPath 하위).")]
    public string runtimeTextureFolderName = "MiroRuntimeMaps";
    [Tooltip("생성되는 파일명 prefix. 예: miro_map_yyyyMMdd_HHmmss.png")]
    public string runtimeTexturePrefix = "miro_map_";

    [Header("Debug")]
    [SerializeField] bool logBaking = true;

    /// <summary>
    /// MiroMazeData를 Texture2D로 베이크한다.
    /// </summary>
    public bool TryBake(MiroMazeData data, out Texture2D texture, out string message)
    {
        texture = null;
        if (!ValidateMazeData(data, out message))
        {
            return false;
        }

        int size = data.mazeSize;
        int cellPixels = Mathf.Max(2, pixelsPerCell);
        int linePixels = Mathf.Clamp(
            Mathf.RoundToInt(cellPixels * Mathf.Clamp01(pathWidthRatio)),
            Mathf.Max(1, minPathWidthPixels),
            cellPixels);
        int width = size * cellPixels;
        int height = size * cellPixels;
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = $"MiroTexture_{data.generatedAtUtc}",
            filterMode = textureFilterMode,
            wrapMode = textureWrapMode
        };

        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
        }

        for (int y = 1; y <= size; y++)
        {
            for (int x = 1; x <= size; x++)
            {
                MiroCellType cellType = data.GetCellType(y, x);
                if (!TryGetCellColor(cellType, out Color cellColor))
                {
                    continue;
                }

                int gridX = x - 1;
                int gridY = ToGridY(y, size);
                PaintPathCore(pixels, width, height, gridX, gridY, cellPixels, linePixels, cellColor);

                if (x < size &&
                    TryGetConnectionColor(cellType, data.GetCellType(y, x + 1), out Color rightColor))
                {
                    int rightGridX = x;
                    int rightGridY = gridY;
                    PaintConnection(
                        pixels, width, height,
                        gridX, gridY, rightGridX, rightGridY,
                        cellPixels, linePixels, rightColor);
                }

                if (y < size &&
                    TryGetConnectionColor(cellType, data.GetCellType(y + 1, x), out Color upColor))
                {
                    int upGridX = gridX;
                    int upGridY = ToGridY(y + 1, size);
                    PaintConnection(
                        pixels, width, height,
                        gridX, gridY, upGridX, upGridY,
                        cellPixels, linePixels, upColor);
                }
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        message = $"Baked texture {width}x{height} from mazeSize={size}.";
        if (logBaking)
        {
            Debug.Log($"[MiroMapBaker] {message}");
        }

        return true;
    }

    /// <summary>
    /// 베이크된 텍스처를 Plane 적용용 런타임 머티리얼로 생성한다.
    /// </summary>
    public Material CreateRuntimeMaterial(Texture2D texture, string materialName)
    {
        if (texture == null)
        {
            Debug.LogWarning("[MiroMapBaker] CreateRuntimeMaterial skipped: texture is null.");
            return null;
        }

        Shader shader = FindSupportedShader();
        if (shader == null)
        {
            Debug.LogError("[MiroMapBaker] No compatible shader found for runtime material creation.");
            return null;
        }

        Material material = new Material(shader)
        {
            name = string.IsNullOrWhiteSpace(materialName) ? "MiroPlaneRuntimeMaterial" : materialName
        };

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        else
        {
            material.mainTexture = texture;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        ApplyRuntimeTextureTransform(material);
        return material;
    }

    void ApplyRuntimeTextureTransform(Material material)
    {
        if (material == null)
        {
            return;
        }

        Vector2 scale = rotateRuntimeTexture180 ? new Vector2(-1f, -1f) : Vector2.one;
        Vector2 offset = rotateRuntimeTexture180 ? Vector2.one : Vector2.zero;

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureOffset("_MainTex", offset);
        }

        material.mainTextureScale = scale;
        material.mainTextureOffset = offset;
    }

    /// <summary>
    /// 텍스처를 PNG로 저장하고 절대 경로를 반환한다.
    /// </summary>
    public bool TrySaveTexturePng(Texture2D texture, out string savedPath, out string message)
    {
        savedPath = "";
        if (texture == null)
        {
            message = "Save failed: texture is null.";
            return false;
        }

        try
        {
            string folder = GetTextureDirectoryPath();
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string timeStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string fileName = $"{runtimeTexturePrefix}{timeStamp}.png";
            savedPath = Path.Combine(folder, fileName);

            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(savedPath, pngBytes);

            message = $"Saved PNG: {savedPath}";
            if (logBaking)
            {
                Debug.Log($"[MiroMapBaker] {message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            message = $"Save failed: {ex.Message}";
            Debug.LogError($"[MiroMapBaker] {message}");
            return false;
        }
    }

    /// <summary>
    /// 저장된 PNG 파일을 읽어 Texture2D로 로드한다.
    /// </summary>
    public bool TryLoadTexturePng(string texturePath, out Texture2D texture, out string message)
    {
        texture = null;
        if (string.IsNullOrWhiteSpace(texturePath))
        {
            message = "Load failed: texturePath is empty.";
            return false;
        }

        if (!File.Exists(texturePath))
        {
            message = $"Load failed: file not found -> {texturePath}";
            return false;
        }

        try
        {
            byte[] pngBytes = File.ReadAllBytes(texturePath);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"MiroTexture_Loaded_{Path.GetFileNameWithoutExtension(texturePath)}"
            };

            bool loaded = texture.LoadImage(pngBytes, markNonReadable: false);
            if (!loaded)
            {
                message = "Load failed: Texture2D.LoadImage returned false.";
                return false;
            }

            texture.filterMode = textureFilterMode;
            texture.wrapMode = textureWrapMode;
            message = $"Loaded PNG: {texturePath}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Load failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 런타임 텍스처 폴더 절대 경로를 반환한다.
    /// </summary>
    public string GetTextureDirectoryPath()
    {
        return Path.Combine(Application.persistentDataPath, runtimeTextureFolderName);
    }

    bool TryGetCellColor(MiroCellType cellType, out Color color)
    {
        if (cellType == MiroCellType.MainPath && bakeMainPath)
        {
            color = mainPathColor;
            return true;
        }

        if (cellType == MiroCellType.BranchPath && bakeBranchPath)
        {
            color = branchPathColor;
            return true;
        }

        color = default;
        return false;
    }

    bool TryGetConnectionColor(MiroCellType from, MiroCellType to, out Color color)
    {
        bool fromPainted = TryGetCellColor(from, out _);
        bool toPainted = TryGetCellColor(to, out _);
        if (!fromPainted || !toPainted)
        {
            color = default;
            return false;
        }

        // Main-Main 연결은 메인 라인 색상, 그 외(브랜치 포함)는 브랜치 색상으로 통일.
        if (from == MiroCellType.MainPath && to == MiroCellType.MainPath)
        {
            color = mainPathColor;
            return true;
        }

        color = branchPathColor;
        return true;
    }

    int ToGridY(int oneBasedY, int mazeSize)
    {
        return flipVertical ? (mazeSize - oneBasedY) : (oneBasedY - 1);
    }

    void PaintPathCore(Color[] pixels, int width, int height, int gridX, int gridY, int cellPixels, int linePixels, Color color)
    {
        int centerX = GetCellCenterPixel(gridX, cellPixels);
        int centerY = GetCellCenterPixel(gridY, cellPixels);
        int half = linePixels / 2;
        int xMin = centerX - half;
        int yMin = centerY - half;
        int xMax = xMin + linePixels;
        int yMax = yMin + linePixels;
        FillRect(pixels, width, height, xMin, yMin, xMax, yMax, color);
    }

    void PaintConnection(
        Color[] pixels,
        int width,
        int height,
        int fromGridX,
        int fromGridY,
        int toGridX,
        int toGridY,
        int cellPixels,
        int linePixels,
        Color color)
    {
        int x0 = GetCellCenterPixel(fromGridX, cellPixels);
        int y0 = GetCellCenterPixel(fromGridY, cellPixels);
        int x1 = GetCellCenterPixel(toGridX, cellPixels);
        int y1 = GetCellCenterPixel(toGridY, cellPixels);
        int half = linePixels / 2;

        if (y0 == y1)
        {
            int xMin = Mathf.Min(x0, x1);
            int xMax = Mathf.Max(x0, x1) + 1;
            int yMin = y0 - half;
            int yMax = yMin + linePixels;
            FillRect(pixels, width, height, xMin, yMin, xMax, yMax, color);
            return;
        }

        if (x0 == x1)
        {
            int yMin = Mathf.Min(y0, y1);
            int yMax = Mathf.Max(y0, y1) + 1;
            int xMin = x0 - half;
            int xMax = xMin + linePixels;
            FillRect(pixels, width, height, xMin, yMin, xMax, yMax, color);
        }
    }

    int GetCellCenterPixel(int gridIndex, int cellPixels)
    {
        return (gridIndex * cellPixels) + (cellPixels / 2);
    }

    void FillRect(Color[] pixels, int width, int height, int xMin, int yMin, int xMaxExclusive, int yMaxExclusive, Color color)
    {
        int clampedXMin = Mathf.Clamp(xMin, 0, width);
        int clampedYMin = Mathf.Clamp(yMin, 0, height);
        int clampedXMax = Mathf.Clamp(xMaxExclusive, 0, width);
        int clampedYMax = Mathf.Clamp(yMaxExclusive, 0, height);

        if (clampedXMin >= clampedXMax || clampedYMin >= clampedYMax)
        {
            return;
        }

        for (int y = clampedYMin; y < clampedYMax; y++)
        {
            int rowOffset = y * width;
            for (int x = clampedXMin; x < clampedXMax; x++)
            {
                pixels[rowOffset + x] = color;
            }
        }
    }

    bool ValidateMazeData(MiroMazeData data, out string message)
    {
        if (data == null)
        {
            message = "maze data is null";
            return false;
        }

        if (data.mazeSize < 5)
        {
            message = "mazeSize must be >= 5";
            return false;
        }

        if (data.cells == null || data.cells.Length != data.mazeSize * data.mazeSize)
        {
            message = "cells array is invalid";
            return false;
        }

        message = "ok";
        return true;
    }

    Shader FindSupportedShader()
    {
        if (IsSrpActive())
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader != null)
            {
                return shader;
            }

            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                return shader;
            }
        }

        Shader builtIn = Shader.Find("Standard");
        if (builtIn != null)
        {
            return builtIn;
        }

        builtIn = Shader.Find("Legacy Shaders/Diffuse");
        if (builtIn != null)
        {
            return builtIn;
        }

        builtIn = Shader.Find("Unlit/Texture");
        if (builtIn != null)
        {
            return builtIn;
        }

        return Shader.Find("Sprites/Default");
    }

    bool IsSrpActive()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null)
        {
            return true;
        }

        pipeline = QualitySettings.renderPipeline;
        return pipeline != null;
    }
}
