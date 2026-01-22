using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Render Texture 기반 화면 분할 레이아웃 관리자
/// 
/// 레이아웃 구조:
/// ┌────────────────────────────┬───────────────┐
/// │                            │   Pin View    │
/// │   Block Coding UI (70%)    ├───────────────┤
/// │                            │  RC Car View  │
/// └────────────────────────────┴───────────────┘
/// </summary>
public class ScreenLayoutManager : MonoBehaviour
{
    [Header("=== Panels ===")]
    [Tooltip("블록 코딩 UI가 들어갈 왼쪽 패널")]
    [SerializeField] RectTransform blockCodePanel;
    
    [Tooltip("오른쪽 영역 전체를 감싸는 패널")]
    [SerializeField] RectTransform rightPanel;
    
    [Tooltip("아두이노 핀 연결 화면 패널 (오른쪽 상단)")]
    [SerializeField] RectTransform pinPanel;
    
    [Tooltip("RC Car 뷰 패널 (오른쪽 하단)")]
    [SerializeField] RectTransform rcCarPanel;
    
    [Header("=== RawImages ===")]
    [Tooltip("RC Car 카메라 렌더링을 표시할 RawImage")]
    [SerializeField] RawImage rcCarRawImage;
    
    [Tooltip("Pin View 카메라 렌더링을 표시할 RawImage (선택사항)")]
    [SerializeField] RawImage pinViewRawImage;
    
    [Header("=== Render Textures ===")]
    [Tooltip("RC Car 카메라가 렌더링할 Render Texture")]
    [SerializeField] RenderTexture rcCarRenderTexture;
    
    [Tooltip("Pin View 카메라가 렌더링할 Render Texture (선택사항)")]
    [SerializeField] RenderTexture pinViewRenderTexture;
    
    [Header("=== Cameras ===")]
    [Tooltip("RC Car를 렌더링할 카메라")]
    [SerializeField] Camera rcCarCamera;
    
    [Tooltip("Pin View를 렌더링할 카메라 (선택사항)")]
    [SerializeField] Camera pinViewCamera;
    
    [Header("=== Layout Settings ===")]
    [Tooltip("블록 코딩 UI가 차지할 화면 비율 (0.7 = 70%)")]
    [Range(0.5f, 0.9f)]
    public float blockCodeWidthRatio = 0.7f;
    
    [Tooltip("오른쪽 패널에서 핀 연결 화면이 차지할 비율 (0.5 = 50%)")]
    [Range(0.3f, 0.7f)]
    public float pinPanelHeightRatio = 0.5f;
    
    void Start()
    {
        SetupRenderTextures();
        SetupCameras();
        ApplyLayout();
    }
    
    /// <summary>
    /// Render Texture를 RawImage에 연결
    /// </summary>
    void SetupRenderTextures()
    {
        if (rcCarRawImage != null && rcCarRenderTexture != null)
        {
            rcCarRawImage.texture = rcCarRenderTexture;
            Debug.Log("[ScreenLayoutManager] RC Car RenderTexture connected.");
        }
            
        if (pinViewRawImage != null && pinViewRenderTexture != null)
        {
            pinViewRawImage.texture = pinViewRenderTexture;
            Debug.Log("[ScreenLayoutManager] Pin View RenderTexture connected.");
        }
    }
    
    /// <summary>
    /// 카메라에 Render Texture 연결
    /// </summary>
    void SetupCameras()
    {
        if (rcCarCamera != null && rcCarRenderTexture != null)
        {
            rcCarCamera.targetTexture = rcCarRenderTexture;
            Debug.Log("[ScreenLayoutManager] RC Car Camera targetTexture set.");
        }
        
        if (pinViewCamera != null && pinViewRenderTexture != null)
        {
            pinViewCamera.targetTexture = pinViewRenderTexture;
            Debug.Log("[ScreenLayoutManager] Pin View Camera targetTexture set.");
        }
    }
    
    /// <summary>
    /// 패널 레이아웃 적용
    /// </summary>
    public void ApplyLayout()
    {
        // 블록 코드 패널 (왼쪽)
        if (blockCodePanel != null)
        {
            blockCodePanel.anchorMin = new Vector2(0, 0);
            blockCodePanel.anchorMax = new Vector2(blockCodeWidthRatio, 1);
            blockCodePanel.offsetMin = Vector2.zero;
            blockCodePanel.offsetMax = Vector2.zero;
        }
        
        // 오른쪽 패널
        if (rightPanel != null)
        {
            rightPanel.anchorMin = new Vector2(blockCodeWidthRatio, 0);
            rightPanel.anchorMax = new Vector2(1, 1);
            rightPanel.offsetMin = Vector2.zero;
            rightPanel.offsetMax = Vector2.zero;
        }
        
        // 핀 연결 패널 (오른쪽 상단)
        if (pinPanel != null)
        {
            pinPanel.anchorMin = new Vector2(0, 1 - pinPanelHeightRatio);
            pinPanel.anchorMax = new Vector2(1, 1);
            pinPanel.offsetMin = Vector2.zero;
            pinPanel.offsetMax = Vector2.zero;
        }
        
        // RC Car 패널 (오른쪽 하단)
        if (rcCarPanel != null)
        {
            rcCarPanel.anchorMin = new Vector2(0, 0);
            rcCarPanel.anchorMax = new Vector2(1, 1 - pinPanelHeightRatio);
            rcCarPanel.offsetMin = Vector2.zero;
            rcCarPanel.offsetMax = Vector2.zero;
        }
        
        Debug.Log($"[ScreenLayoutManager] Layout applied: BlockCode={blockCodeWidthRatio*100}%, Right={100-blockCodeWidthRatio*100}%");
    }
    
    /// <summary>
    /// 런타임에서 레이아웃 비율 변경
    /// </summary>
    public void SetLayoutRatio(float blockCodeRatio, float pinPanelRatio)
    {
        blockCodeWidthRatio = Mathf.Clamp(blockCodeRatio, 0.5f, 0.9f);
        pinPanelHeightRatio = Mathf.Clamp(pinPanelRatio, 0.3f, 0.7f);
        ApplyLayout();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector에서 값 변경 시 실시간 미리보기
    /// </summary>
    void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyLayout();
        }
    }
#endif
}
