using UnityEngine;

/// <summary>
/// Changes the color of a target Cube renderer at runtime.
/// Attach this to the RC Car root object.
/// </summary>
public class VirtualCubeColorController : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Target")]
    [Tooltip("Target renderer to recolor. If empty, it searches a child named Cube.")]
    public Renderer targetRenderer;
    [Tooltip("Child object name to search when targetRenderer is not assigned.")]
    public string cubeChildName = "Cube";
    [Tooltip("Include inactive children when searching for the cube.")]
    public bool includeInactive = true;

    [Header("Runtime")]
    [Tooltip("Apply initial color on Start.")]
    public bool applyOnStart = false;
    [Tooltip("Initial color used when applyOnStart is true.")]
    public Color initialColor = Color.white;

    MaterialPropertyBlock propertyBlock;

    void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ResolveTargetRenderer();
    }

    void Start()
    {
        if (applyOnStart)
        {
            SetColor(initialColor);
        }
    }

    /// <summary>
    /// Sets cube color using a Unity Color value.
    /// </summary>
    public void SetColor(Color color)
    {
        if (!ResolveTargetRenderer())
            return;
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>
    /// Sets cube color with RGB values in 0~1 range.
    /// </summary>
    public void SetColorRGB(float r, float g, float b)
    {
        SetColor(new Color(
            Mathf.Clamp01(r),
            Mathf.Clamp01(g),
            Mathf.Clamp01(b),
            1f));
    }

    /// <summary>
    /// Sets cube grayscale from PWM (0~255).
    /// </summary>
    public void SetColorByPwm(float pwm)
    {
        float t = Mathf.Clamp01(pwm / 255f);
        SetColor(new Color(t, t, t, 1f));
    }

    bool ResolveTargetRenderer()
    {
        if (targetRenderer != null)
            return true;

        Transform cube = FindChildByName(transform, cubeChildName, includeInactive);
        if (cube != null)
        {
            targetRenderer = cube.GetComponent<Renderer>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>(includeInactive);
        }

        if (targetRenderer == null)
        {
            Debug.LogWarning("[VirtualCubeColorController] Target renderer not found.");
            return false;
        }

        return true;
    }

    static Transform FindChildByName(Transform root, string nameToFind, bool includeInactiveChildren)
    {
        if (root == null || string.IsNullOrWhiteSpace(nameToFind))
            return null;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactiveChildren))
        {
            if (child.name == nameToFind)
                return child;
        }

        return null;
    }
}
