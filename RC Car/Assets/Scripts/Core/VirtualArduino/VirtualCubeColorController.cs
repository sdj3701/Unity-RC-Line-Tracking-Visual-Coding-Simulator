using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 런타임에서 대상 Cube 렌더러의 색상을 변경합니다.
/// RC Car 루트 오브젝트에 부착해서 사용합니다.
/// </summary>
public class VirtualCubeColorController : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("대상 설정")]
    [Tooltip("색상을 변경할 대상 렌더러입니다. 비워두면 이름이 Cube인 자식을 찾습니다.")]
    public Renderer targetRenderer;
    [Tooltip("targetRenderer가 비어 있을 때 검색할 자식 오브젝트 이름입니다.")]
    public string cubeChildName = "Cube";
    [Tooltip("Cube 검색 시 비활성 자식까지 포함할지 여부입니다.")]
    public bool includeInactive = true;

    [Header("런타임 설정")]
    [Tooltip("Start 시 초기 색상을 적용합니다.")]
    public bool applyOnStart = false;
    [Tooltip("applyOnStart가 true일 때 사용할 초기 색상입니다.")]
    public Color initialColor = Color.white;

    [Header("UI 색상 버튼")]
    [Tooltip("RC카 색상을 변경할 팔레트 버튼 배열입니다.")]
    public Button[] colorButtons;
    [Tooltip("버튼 인덱스별 색상을 직접 지정합니다. 비워두면 버튼 그래픽 색상을 사용합니다.")]
    public Color[] buttonColors;
    [Tooltip("활성화 시 버튼 클릭 이벤트를 자동으로 연결합니다.")]
    public bool autoBindColorButtons = true;
    [Tooltip("해당 인덱스에 buttonColors가 없으면 버튼 그래픽 색상을 사용합니다.")]
    public bool useButtonGraphicColor = true;

    MaterialPropertyBlock propertyBlock;
    Button[] boundButtons;
    UnityAction[] boundActions;

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

    void OnEnable()
    {
        if (autoBindColorButtons)
            BindColorButtons();
    }

    void OnDisable()
    {
        UnbindColorButtons();
    }

    /// <summary>
    /// Unity Color 값을 사용해 Cube 색상을 설정합니다.
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
    /// 0~1 범위의 RGB 값으로 Cube 색상을 설정합니다.
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
    /// PWM(0~255) 값으로 Cube의 회색조 색상을 설정합니다.
    /// </summary>
    public void SetColorByPwm(float pwm)
    {
        float t = Mathf.Clamp01(pwm / 255f);
        SetColor(new Color(t, t, t, 1f));
    }

    /// <summary>
    /// 설정된 모든 색상 버튼에 onClick 리스너를 연결합니다.
    /// </summary>
    public void BindColorButtons()
    {
        UnbindColorButtons();

        if (colorButtons == null || colorButtons.Length == 0)
            return;

        boundButtons = new Button[colorButtons.Length];
        boundActions = new UnityAction[colorButtons.Length];

        for (int i = 0; i < colorButtons.Length; i++)
        {
            Button button = colorButtons[i];
            if (button == null)
                continue;

            int capturedIndex = i;
            UnityAction action = () => SetColorFromButtonIndex(capturedIndex);
            button.onClick.AddListener(action);

            boundButtons[i] = button;
            boundActions[i] = action;
        }
    }

    /// <summary>
    /// 이전에 연결한 색상 버튼 onClick 리스너를 해제합니다.
    /// </summary>
    public void UnbindColorButtons()
    {
        if (boundButtons == null || boundActions == null)
            return;

        int count = Mathf.Min(boundButtons.Length, boundActions.Length);
        for (int i = 0; i < count; i++)
        {
            Button button = boundButtons[i];
            UnityAction action = boundActions[i];
            if (button != null && action != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        boundButtons = null;
        boundActions = null;
    }

    /// <summary>
    /// 설정된 버튼 인덱스에 해당하는 색으로 RC카 색상을 설정합니다.
    /// </summary>
    public void SetColorFromButtonIndex(int index)
    {
        if (colorButtons == null || index < 0 || index >= colorButtons.Length)
            return;

        if (TryGetButtonColor(colorButtons[index], index, out Color buttonColor))
        {
            SetColor(buttonColor);
        }
    }

    /// <summary>
    /// 특정 버튼의 그래픽 색상을 사용해 RC카 색상을 설정합니다.
    /// </summary>
    public void SetColorFromButton(Button button)
    {
        if (TryGetButtonColor(button, -1, out Color buttonColor))
        {
            SetColor(buttonColor);
        }
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
            Debug.LogWarning("[VirtualCubeColorController] 대상 렌더러를 찾지 못했습니다.");
            return false;
        }

        return true;
    }

    bool TryGetButtonColor(Button button, int buttonIndex, out Color color)
    {
        if (buttonColors != null && buttonIndex >= 0 && buttonIndex < buttonColors.Length)
        {
            color = buttonColors[buttonIndex];
            color.a = 1f;
            return true;
        }

        if (!useButtonGraphicColor || button == null)
        {
            color = Color.white;
            return false;
        }

        Graphic targetGraphic = button.targetGraphic;
        if (targetGraphic == null)
        {
            targetGraphic = button.GetComponent<Graphic>();
        }

        if (targetGraphic != null)
        {
            color = targetGraphic.color;
            color.a = 1f;
            return true;
        }

        color = Color.white;
        return false;
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
