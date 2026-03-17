using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 생성된 미로 데이터를 선 형태의 표면 세그먼트로 렌더링한다.
/// VirtualLineSensor가 감지할 수 있도록 Collider + Renderer 기반 오브젝트를 만든다.
/// </summary>
public class MiroLineRenderer : MonoBehaviour
{
    [Header("Line Root")]
    [Tooltip("런타임에 생성된 라인 오브젝트의 부모 Transform. 비어 있으면 자동 생성한다.")]
    public Transform lineRoot;
    [Tooltip("월드 원점으로 사용할 Transform. 비어 있으면 worldOrigin 값을 사용한다.")]
    public Transform worldOriginTransform;
    [Tooltip("worldOriginTransform 미지정 시 사용할 기본 월드 오프셋.")]
    public Vector3 worldOrigin = Vector3.zero;

    [Header("Line Shape")]
    [Min(0.05f)] public float lineWidth = 0.75f;
    [Min(0.005f)] public float lineThickness = 0.03f;
    [Min(0f)] public float lineVerticalOffset = 0.005f;
    [Min(0f)] public float segmentOverlap = 0.02f;
    [Tooltip("생성되는 라인 세그먼트에 적용할 Unity Layer.")]
    public int lineLayer = 0;

    [Header("Path Visibility")]
    [Tooltip("정답 경로('*') 셀 렌더링 여부.")]
    public bool renderMainPath = true;
    [Tooltip("분기/불필요한 길('.') 셀 렌더링 여부.")]
    public bool renderBranchPath = true;
    public Color mainPathColor = Color.black;
    public Color branchPathColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [Tooltip("불필요한 길('.')을 테스트용 전용 색상(초록색)으로 강조한다.")]
    public bool highlightUnnecessaryPathsInGreen = true;
    public Color unnecessaryPathColor = Color.green;

    [Header("Ground")]
    [Tooltip("센서 명암 대비를 위해 라인 아래 흰색 바닥을 생성한다.")]
    public bool createGroundPlane = true;
    [Min(0.005f)] public float groundThickness = 0.02f;
    [Min(0f)] public float groundPadding = 2f;
    public Color groundColor = Color.white;
    [Tooltip("런타임 Ground를 맞출 기준 Plane Transform. 비우면 이름이 'Plane'인 오브젝트를 자동 탐색한다.")]
    public Transform groundReferencePlane;
    [Tooltip("true면 런타임 Ground 위치/회전/스케일을 기준 Plane과 동일하게 맞춘다.")]
    public bool alignGroundToReferencePlane = true;
    [Tooltip("기준 Plane과 겹침(Z-fighting) 방지를 위해 런타임 Ground를 Plane 법선 방향으로 추가 오프셋한다.")]
    [Min(0f)] public float alignedGroundSeparation = 0.03f;
    [Tooltip("true면 기준 Plane 위쪽(+up)으로, false면 아래쪽(-up)으로 오프셋한다.")]
    public bool placeAlignedGroundAboveReferencePlane = false;
    [Tooltip("true면 런타임 Ground 생성 위치를 아래 좌표로 강제한다.")]
    public bool overrideGroundPosition = false;
    [Tooltip("overrideGroundPosition=true일 때 사용할 런타임 Ground 월드 좌표.")]
    public Vector3 groundPositionOverride = Vector3.zero;
    [Tooltip("true면 groundPositionOverride 기준 이동을 런타임 Ground뿐 아니라 모든 생성 오브젝트(라인/브랜치)에도 동일 적용한다.")]
    public bool overrideGroundPositionAffectsAllGeneratedObjects = true;
    [Tooltip("기준 Plane을 찾지 못한 경우 사용할 런타임 Ground 기본 스케일.")]
    public Vector3 fallbackGroundScale = new Vector3(2f, 1f, 2f);

    [Header("Debug")]
    [SerializeField] bool logRenderSummary = true;

    readonly List<GameObject> spawnedObjects = new List<GameObject>();
    Material runtimeMainMaterial;
    Material runtimeBranchMaterial;
    Material runtimeUnnecessaryMaterial;
    Material runtimeGroundMaterial;
    bool hasCoordinateAlignment;
    Vector3 alignedSourceCenter;
    Vector3 alignedTargetCenter;
    Quaternion alignedTargetRotation = Quaternion.identity;
    float alignedScaleX = 1f;
    float alignedScaleZ = 1f;
    float alignedUniformScale = 1f;
    Vector3 generatedWorldOffset = Vector3.zero;

    /// <summary>
    /// 기존 렌더 오브젝트를 지우고 전달받은 미로 데이터로 라인 세그먼트를 다시 생성한다.
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
        PrepareCoordinateAlignment(data);
        UpdateGeneratedWorldOffset(data);

        if (createGroundPlane)
        {
            CreateGroundPlane(data);
        }

        int segmentCount = CreateAllSegments(data, out int unnecessaryPathCellCount);
        if (logRenderSummary)
        {
            Debug.Log($"[MiroLineRenderer] Rendered segments={segmentCount}, unnecessaryPaths={unnecessaryPathCellCount}, size={data.mazeSize}");
        }
    }

    /// <summary>
    /// 현재 씬에 생성된 라인/바닥 오브젝트를 모두 제거한다.
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
    /// 각 셀의 우측/하단 이웃만 1회 스캔해 중복 없이 세그먼트를 생성한다.
    /// </summary>
    int CreateAllSegments(MiroMazeData data, out int unnecessaryPathCellCount)
    {
        int created = 0;
        int size = data.mazeSize;
        unnecessaryPathCellCount = CountCellsByType(data, MiroCellType.BranchPath);

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
                        bool unnecessarySegment = IsUnnecessarySegment(current, right);
                        CreateSegment(data, x, y, x + 1, y, mainSegment, unnecessarySegment);
                        created++;
                    }
                }

                if (y < size)
                {
                    MiroCellType down = data.GetCellType(y + 1, x);
                    if (ShouldRenderCell(down))
                    {
                        bool mainSegment = current == MiroCellType.MainPath && down == MiroCellType.MainPath;
                        bool unnecessarySegment = IsUnnecessarySegment(current, down);
                        CreateSegment(data, x, y, x, y + 1, mainSegment, unnecessarySegment);
                        created++;
                    }
                }
            }
        }

        return created;
    }

    /// <summary>
    /// 인접한 두 셀 사이에 큐브 기반 선 세그먼트 하나를 생성한다.
    /// </summary>
    void CreateSegment(MiroMazeData data, int x1, int y1, int x2, int y2, bool isMainSegment, bool isUnnecessarySegment)
    {
        float effectiveLineWidth = GetScaledLineWidth();
        float effectiveLineThickness = GetScaledLineThickness();
        float effectiveLineVerticalOffset = GetScaledLineVerticalOffset();
        float effectiveSegmentOverlap = GetScaledSegmentOverlap();

        Vector3 start = ToWorldPosition(data, x1, y1);
        Vector3 end = ToWorldPosition(data, x2, y2);
        Vector3 direction = end - start;
        float length = direction.magnitude + effectiveSegmentOverlap;

        if (length <= 0.0001f)
        {
            return;
        }

        Vector3 center = (start + end) * 0.5f;
        center.y += (effectiveLineThickness * 0.5f) + effectiveLineVerticalOffset;

        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        segment.name = isMainSegment
            ? "MiroLine_Main"
            : (isUnnecessarySegment ? "MiroLine_Unnecessary" : "MiroLine_Branch");
        segment.layer = lineLayer;
        segment.transform.SetParent(GetOrCreateLineRoot(), true);
        segment.transform.position = center;
        segment.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        segment.transform.localScale = new Vector3(effectiveLineWidth, effectiveLineThickness, length);

        Renderer renderer = segment.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetSegmentMaterial(isMainSegment, isUnnecessarySegment);
        }

        spawnedObjects.Add(segment);
    }

    /// <summary>
    /// 세그먼트 타입(주 경로/불필요 경로/일반 분기)에 맞는 머티리얼을 반환한다.
    /// </summary>
    Material GetSegmentMaterial(bool isMainSegment, bool isUnnecessarySegment)
    {
        if (isMainSegment)
        {
            return runtimeMainMaterial;
        }

        if (isUnnecessarySegment && highlightUnnecessaryPathsInGreen && runtimeUnnecessaryMaterial != null)
        {
            return runtimeUnnecessaryMaterial;
        }

        return runtimeBranchMaterial;
    }

    /// <summary>
    /// 세그먼트가 불필요한 길(BranchPath)에 해당하는지 판정한다.
    /// </summary>
    bool IsUnnecessarySegment(MiroCellType typeA, MiroCellType typeB)
    {
        return typeA == MiroCellType.BranchPath || typeB == MiroCellType.BranchPath;
    }

    /// <summary>
    /// 지정한 셀 타입의 개수를 계산한다.
    /// </summary>
    int CountCellsByType(MiroMazeData data, MiroCellType targetType)
    {
        int size = data.mazeSize;
        int count = 0;
        for (int y = 1; y <= size; y++)
        {
            for (int x = 1; x <= size; x++)
            {
                if (data.GetCellType(y, x) == targetType)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 흑백 센서 대비를 높이기 위해 라인 아래 밝은 바닥 평면을 생성한다.
    /// </summary>
    void CreateGroundPlane(MiroMazeData data)
    {
        Vector3 defaultGroundPosition = GetDefaultGroundPosition(data, out bool aligned, out Transform referencePlane);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "MiroGround";
        ground.transform.SetParent(GetOrCreateLineRoot(), true);
        if (aligned && referencePlane != null)
        {
            ground.transform.rotation = referencePlane.rotation;
            ground.transform.localScale = referencePlane.localScale;
        }
        else
        {
            ground.transform.rotation = Quaternion.identity;

            // Fallback: keep a fixed baseline close to the default map plane scale.
            Vector3 safeScale = fallbackGroundScale;
            safeScale.x = Mathf.Max(0.01f, Mathf.Abs(safeScale.x));
            safeScale.y = Mathf.Max(0.01f, Mathf.Abs(safeScale.y));
            safeScale.z = Mathf.Max(0.01f, Mathf.Abs(safeScale.z));
            ground.transform.localScale = safeScale;
        }

        Vector3 groundPosition = defaultGroundPosition + generatedWorldOffset;
        if (overrideGroundPosition && !overrideGroundPositionAffectsAllGeneratedObjects)
        {
            groundPosition = groundPositionOverride;
        }
        ground.transform.position = groundPosition;

        Renderer renderer = ground.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = runtimeGroundMaterial;
        }

        spawnedObjects.Add(ground);
    }

    void UpdateGeneratedWorldOffset(MiroMazeData data)
    {
        generatedWorldOffset = Vector3.zero;
        if (!overrideGroundPosition || !overrideGroundPositionAffectsAllGeneratedObjects)
        {
            return;
        }

        Vector3 defaultGroundPosition = GetDefaultGroundPosition(data, out _, out _);
        generatedWorldOffset = groundPositionOverride - defaultGroundPosition;
    }

    Vector3 GetDefaultGroundPosition(MiroMazeData data, out bool aligned, out Transform referencePlane)
    {
        GetGroundBounds(data, out _, out _, out float centerX, out float centerZ);
        Vector3 center = GetBaseOrigin() + new Vector3(centerX, -groundThickness * 0.5f, centerZ);

        referencePlane = null;
        aligned = false;
        if (alignGroundToReferencePlane)
        {
            aligned = TryGetGroundReferencePlane(out referencePlane);
        }

        if (!aligned || referencePlane == null)
        {
            return center;
        }

        float signedDirection = placeAlignedGroundAboveReferencePlane ? 1f : -1f;
        float normalOffset = (groundThickness * 0.5f) + Mathf.Max(0f, alignedGroundSeparation);
        return referencePlane.position + (referencePlane.up * (signedDirection * normalOffset));
    }

    void PrepareCoordinateAlignment(MiroMazeData data)
    {
        ResetCoordinateAlignment();

        if (!alignGroundToReferencePlane || !TryGetGroundReferencePlane(out Transform referencePlane))
        {
            return;
        }

        GetGroundBounds(data, out float sourceSpanX, out float sourceSpanZ, out float centerX, out float centerZ);
        float targetSpanX = Mathf.Max(0.01f, Mathf.Abs(referencePlane.lossyScale.x) * 10f);
        float targetSpanZ = Mathf.Max(0.01f, Mathf.Abs(referencePlane.lossyScale.z) * 10f);

        alignedSourceCenter = GetBaseOrigin() + new Vector3(centerX, 0f, centerZ);
        alignedTargetCenter = referencePlane.position;
        alignedTargetRotation = referencePlane.rotation;
        alignedScaleX = targetSpanX / Mathf.Max(0.01f, sourceSpanX);
        alignedScaleZ = targetSpanZ / Mathf.Max(0.01f, sourceSpanZ);
        alignedUniformScale = Mathf.Sqrt(alignedScaleX * alignedScaleZ);
        hasCoordinateAlignment = true;
    }

    void ResetCoordinateAlignment()
    {
        hasCoordinateAlignment = false;
        alignedSourceCenter = Vector3.zero;
        alignedTargetCenter = Vector3.zero;
        alignedTargetRotation = Quaternion.identity;
        alignedScaleX = 1f;
        alignedScaleZ = 1f;
        alignedUniformScale = 1f;
    }

    void GetGroundBounds(MiroMazeData data, out float spanX, out float spanZ, out float centerX, out float centerZ)
    {
        int size = data.mazeSize;
        spanX = Mathf.Max(lineWidth, data.cellStepX * Mathf.Max(1, size - 1) + lineWidth + groundPadding * 2f);
        spanZ = Mathf.Max(lineWidth, data.cellStepZ * Mathf.Max(1, size - 1) + lineWidth + groundPadding * 2f);
        centerX = data.cellStepX * (size + 1) * 0.5f;
        centerZ = data.cellStepZ * (size + 1) * 0.5f;
    }

    bool TryGetGroundReferencePlane(out Transform referencePlane)
    {
        if (groundReferencePlane != null)
        {
            referencePlane = groundReferencePlane;
            return true;
        }

        GameObject planeObj = GameObject.Find("Plane");
        if (planeObj != null)
        {
            groundReferencePlane = planeObj.transform;
            referencePlane = groundReferencePlane;
            return true;
        }

        referencePlane = null;
        return false;
    }

    /// <summary>
    /// 현재 렌더링 옵션 기준으로 해당 셀 타입을 표시할지 판단한다.
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
    /// 1-based 미로 좌표를 월드 좌표로 변환한다.
    /// </summary>
    Vector3 ToWorldPosition(MiroMazeData data, int oneBasedX, int oneBasedY)
    {
        Vector3 sourcePosition = GetBaseOrigin() + new Vector3(data.cellStepX * oneBasedX, 0f, data.cellStepZ * oneBasedY);
        if (!hasCoordinateAlignment)
        {
            return sourcePosition + generatedWorldOffset;
        }

        Vector3 sourceDelta = sourcePosition - alignedSourceCenter;
        Vector3 scaledDelta = new Vector3(sourceDelta.x * alignedScaleX, 0f, sourceDelta.z * alignedScaleZ);
        return alignedTargetCenter + alignedTargetRotation * scaledDelta + generatedWorldOffset;
    }

    float GetScaledLineWidth()
    {
        return hasCoordinateAlignment
            ? Mathf.Max(0.01f, lineWidth * alignedUniformScale)
            : lineWidth;
    }

    float GetScaledLineThickness()
    {
        return hasCoordinateAlignment
            ? Mathf.Max(0.002f, lineThickness * alignedUniformScale)
            : lineThickness;
    }

    float GetScaledLineVerticalOffset()
    {
        return hasCoordinateAlignment
            ? Mathf.Max(0f, lineVerticalOffset * alignedUniformScale)
            : lineVerticalOffset;
    }

    float GetScaledSegmentOverlap()
    {
        return hasCoordinateAlignment
            ? Mathf.Max(0f, segmentOverlap * alignedUniformScale)
            : segmentOverlap;
    }

    /// <summary>
    /// 기준 원점(Transform 또는 Vector3)을 반환한다.
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
    /// 주 경로/분기/불필요 경로/바닥용 런타임 머티리얼을 생성하고 캐시한다.
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

        if (runtimeUnnecessaryMaterial == null)
        {
            runtimeUnnecessaryMaterial = CreateRuntimeMaterial("MiroUnnecessaryMaterial_Runtime", unnecessaryPathColor);
        }

        if (runtimeGroundMaterial == null)
        {
            runtimeGroundMaterial = CreateRuntimeMaterial("MiroGroundMaterial_Runtime", groundColor);
        }
    }

    /// <summary>
    /// 사용 가능한 셰이더를 찾아 런타임 머티리얼을 만들고 색상을 지정한다.
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
    /// Built-in/URP 환경에서 사용 가능한 셰이더를 우선순위에 따라 찾는다.
    /// </summary>
    Shader FindSupportedShader()
    {
        if (IsSrpActive())
        {
            // SRP 활성 시 URP 셰이더를 우선 사용한다.
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

        // Built-in 렌더 파이프라인 fallback 경로.
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
    /// 현재 Scriptable Render Pipeline(SRP) 자산이 활성화되어 있는지 확인한다.
    /// </summary>
    bool IsSrpActive()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null)
        {
            return true;
        }

        // 일부 Unity 버전에서 currentRenderPipeline이 null일 수 있어 QualitySettings도 함께 확인한다.
        pipeline = QualitySettings.renderPipeline;
        return pipeline != null;
    }

    /// <summary>
    /// 인스펙터 값 변경 시 머티리얼 캐시를 초기화해 색상/셰이더 변경이 즉시 반영되게 한다.
    /// </summary>
    void OnValidate()
    {
        runtimeMainMaterial = null;
        runtimeBranchMaterial = null;
        runtimeUnnecessaryMaterial = null;
        runtimeGroundMaterial = null;
    }

    /// <summary>
    /// 런타임 생성 머티리얼을 정리해 숨은 메모리 누수를 방지한다.
    /// </summary>
    void OnDestroy()
    {
        DestroyRuntimeMaterial(runtimeMainMaterial);
        DestroyRuntimeMaterial(runtimeBranchMaterial);
        DestroyRuntimeMaterial(runtimeUnnecessaryMaterial);
        DestroyRuntimeMaterial(runtimeGroundMaterial);
    }

    /// <summary>
    /// 플레이 모드/에디터 모드에 맞춰 머티리얼을 안전하게 파괴한다.
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
    /// 기존 루트를 반환하거나, 없으면 현재 컴포넌트 하위에 전용 루트를 생성한다.
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
