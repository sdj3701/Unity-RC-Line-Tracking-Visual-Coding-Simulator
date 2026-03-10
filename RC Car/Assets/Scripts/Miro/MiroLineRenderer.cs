using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders generated maze data as line-like surface segments that can be detected by VirtualLineSensor.
/// </summary>
public class MiroLineRenderer : MonoBehaviour
{
    [Header("Line Root")]
    [Tooltip("Parent transform for generated runtime objects. If null, one is created automatically.")]
    public Transform lineRoot;
    [Tooltip("Optional world origin transform. If not set, worldOrigin is used.")]
    public Transform worldOriginTransform;
    [Tooltip("Base world offset used when worldOriginTransform is not assigned.")]
    public Vector3 worldOrigin = Vector3.zero;

    [Header("Line Shape")]
    [Min(0.05f)] public float lineWidth = 0.75f;
    [Min(0.005f)] public float lineThickness = 0.03f;
    [Min(0f)] public float lineVerticalOffset = 0.005f;
    [Min(0f)] public float segmentOverlap = 0.02f;
    [Tooltip("Unity layer applied to generated line segments.")]
    public int lineLayer = 0;

    [Header("Path Visibility")]
    [Tooltip("Render '*' cells.")]
    public bool renderMainPath = true;
    [Tooltip("Render '.' cells.")]
    public bool renderBranchPath = false;
    public Color mainPathColor = Color.black;
    public Color branchPathColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("Ground")]
    [Tooltip("Creates a white ground plane below line segments for stable sensor contrast.")]
    public bool createGroundPlane = true;
    [Min(0.005f)] public float groundThickness = 0.02f;
    [Min(0f)] public float groundPadding = 2f;
    public Color groundColor = Color.white;

    [Header("Debug")]
    [SerializeField] bool logRenderSummary = true;

    readonly List<GameObject> spawnedObjects = new List<GameObject>();
    Material runtimeMainMaterial;
    Material runtimeBranchMaterial;
    Material runtimeGroundMaterial;

    /// <summary>
    /// Clears old render objects and rebuilds line segments from provided maze data.
    /// </summary>
    public void Render(MiroMazeData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[MiroLineRenderer] Render request ignored because maze data is null.");
            return;
        }

        ClearRendered();
        EnsureRuntimeMaterials();

        if (createGroundPlane)
        {
            CreateGroundPlane(data);
        }

        int segmentCount = CreateAllSegments(data);
        if (logRenderSummary)
        {
            Debug.Log($"[MiroLineRenderer] Rendered segments={segmentCount}, size={data.mazeSize}");
        }
    }

    /// <summary>
    /// Removes all generated runtime objects from the scene.
    /// </summary>
    public void ClearRendered()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            GameObject obj = spawnedObjects[i];
            if (obj == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        spawnedObjects.Clear();
    }

    /// <summary>
    /// Creates all adjacency-based segments by scanning right/down neighbors once.
    /// </summary>
    int CreateAllSegments(MiroMazeData data)
    {
        int created = 0;
        int size = data.mazeSize;

        for (int y = 1; y <= size; y++)
        {
            for (int x = 1; x <= size; x++)
            {
                MiroCellType current = data.GetCellType(y, x);
                if (!ShouldRenderCell(current))
                {
                    continue;
                }

                if (x < size)
                {
                    MiroCellType right = data.GetCellType(y, x + 1);
                    if (ShouldRenderCell(right))
                    {
                        bool mainSegment = current == MiroCellType.MainPath && right == MiroCellType.MainPath;
                        CreateSegment(data, x, y, x + 1, y, mainSegment);
                        created++;
                    }
                }

                if (y < size)
                {
                    MiroCellType down = data.GetCellType(y + 1, x);
                    if (ShouldRenderCell(down))
                    {
                        bool mainSegment = current == MiroCellType.MainPath && down == MiroCellType.MainPath;
                        CreateSegment(data, x, y, x, y + 1, mainSegment);
                        created++;
                    }
                }
            }
        }

        return created;
    }

    /// <summary>
    /// Creates a single cube-based line segment between two adjacent one-based maze cells.
    /// </summary>
    void CreateSegment(MiroMazeData data, int x1, int y1, int x2, int y2, bool isMainSegment)
    {
        Vector3 start = ToWorldPosition(data, x1, y1);
        Vector3 end = ToWorldPosition(data, x2, y2);
        Vector3 direction = end - start;
        float length = direction.magnitude + segmentOverlap;

        if (length <= 0.0001f)
        {
            return;
        }

        Vector3 center = (start + end) * 0.5f;
        center.y += (lineThickness * 0.5f) + lineVerticalOffset;

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        segment.name = isMainSegment ? "MiroLine_Main" : "MiroLine_Branch";
        segment.layer = lineLayer;
        segment.transform.SetParent(GetOrCreateLineRoot(), true);
        segment.transform.position = center;
        segment.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        segment.transform.localScale = new Vector3(lineWidth, lineThickness, length);

        Renderer renderer = segment.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = isMainSegment ? runtimeMainMaterial : runtimeBranchMaterial;
        }

        spawnedObjects.Add(segment);
    }

    /// <summary>
    /// Creates a bright ground plane under the lines to improve black/white sensor contrast.
    /// </summary>
    void CreateGroundPlane(MiroMazeData data)
    {
        int size = data.mazeSize;
        float spanX = Mathf.Max(lineWidth, data.cellStepX * Mathf.Max(1, size - 1) + lineWidth + groundPadding * 2f);
        float spanZ = Mathf.Max(lineWidth, data.cellStepZ * Mathf.Max(1, size - 1) + lineWidth + groundPadding * 2f);
        float centerX = data.cellStepX * (size + 1) * 0.5f;
        float centerZ = data.cellStepZ * (size + 1) * 0.5f;

        Vector3 center = GetBaseOrigin() + new Vector3(centerX, -groundThickness * 0.5f, centerZ);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "MiroGround";
        ground.transform.SetParent(GetOrCreateLineRoot(), true);
        ground.transform.position = center;
        ground.transform.localScale = new Vector3(spanX, groundThickness, spanZ);

        Renderer renderer = ground.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = runtimeGroundMaterial;
        }

        spawnedObjects.Add(ground);
    }

    /// <summary>
    /// Returns true when a cell type should be rendered with the current visibility toggles.
    /// </summary>
    bool ShouldRenderCell(MiroCellType type)
    {
        if (type == MiroCellType.MainPath)
        {
            return renderMainPath;
        }

        if (type == MiroCellType.BranchPath)
        {
            return renderBranchPath;
        }

        return false;
    }

    /// <summary>
    /// Converts one-based maze coordinates into world-space position.
    /// </summary>
    Vector3 ToWorldPosition(MiroMazeData data, int oneBasedX, int oneBasedY)
    {
        return GetBaseOrigin() + new Vector3(data.cellStepX * oneBasedX, 0f, data.cellStepZ * oneBasedY);
    }

    /// <summary>
    /// Resolves the world origin from transform or fallback vector.
    /// </summary>
    Vector3 GetBaseOrigin()
    {
        if (worldOriginTransform != null)
        {
            return worldOriginTransform.position;
        }

        return worldOrigin;
    }

    /// <summary>
    /// Creates and caches runtime materials for main path, branch path, and ground.
    /// </summary>
    void EnsureRuntimeMaterials()
    {
        if (runtimeMainMaterial == null)
        {
            runtimeMainMaterial = CreateRuntimeMaterial("MiroMainMaterial_Runtime", mainPathColor);
        }

        if (runtimeBranchMaterial == null)
        {
            runtimeBranchMaterial = CreateRuntimeMaterial("MiroBranchMaterial_Runtime", branchPathColor);
        }

        if (runtimeGroundMaterial == null)
        {
            runtimeGroundMaterial = CreateRuntimeMaterial("MiroGroundMaterial_Runtime", groundColor);
        }
    }

    /// <summary>
    /// Creates a runtime material using the first available supported shader and assigns the given color.
    /// </summary>
    Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = FindSupportedShader();
        if (shader == null)
        {
            Debug.LogError("[MiroLineRenderer] No compatible shader found for runtime material creation.");
            return null;
        }

        Material material = new Material(shader)
        {
            name = materialName
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    /// <summary>
    /// Finds a shader that is usually available across Built-in and URP projects.
    /// </summary>
    Shader FindSupportedShader()
    {
        if (IsSrpActive())
        {
            // When SRP is active, prefer URP-compatible shaders first.
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

        // Built-in pipeline fallback path.
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

        builtIn = Shader.Find("Unlit/Color");
        if (builtIn != null)
        {
            return builtIn;
        }

        return Shader.Find("Sprites/Default");
    }

    /// <summary>
    /// Returns true when a Scriptable Render Pipeline asset is currently active.
    /// </summary>
    bool IsSrpActive()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null)
        {
            return true;
        }

        // Fallback check used by some Unity versions where currentRenderPipeline can be null in editor states.
        pipeline = QualitySettings.renderPipeline;
        return pipeline != null;
    }

    /// <summary>
    /// Editor-time helper to reset cached materials when values change in Inspector.
    /// </summary>
    void OnValidate()
    {
        runtimeMainMaterial = null;
        runtimeBranchMaterial = null;
        runtimeGroundMaterial = null;
    }

    /// <summary>
    /// Cleans up runtime-created materials to avoid hidden leaked material instances.
    /// </summary>
    void OnDestroy()
    {
        DestroyRuntimeMaterial(runtimeMainMaterial);
        DestroyRuntimeMaterial(runtimeBranchMaterial);
        DestroyRuntimeMaterial(runtimeGroundMaterial);
    }

    /// <summary>
    /// Destroys a material safely in play mode and edit mode.
    /// </summary>
    void DestroyRuntimeMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(material);
        }
        else
        {
            DestroyImmediate(material);
        }
    }

    /// <summary>
    /// Returns an existing root or creates a dedicated runtime root under this component.
    /// </summary>
    Transform GetOrCreateLineRoot()
    {
        if (lineRoot != null)
        {
            return lineRoot;
        }

        Transform existing = transform.Find("MiroGeneratedLines");
        if (existing != null)
        {
            lineRoot = existing;
            return lineRoot;
        }

        GameObject root = new GameObject("MiroGeneratedLines");
        root.transform.SetParent(transform, false);
        lineRoot = root.transform;
        return lineRoot;
    }
}
