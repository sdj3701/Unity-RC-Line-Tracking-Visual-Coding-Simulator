using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 마우스 휠을 이용한 UI 확대/축소 컨트롤러
/// 블록 코딩 영역에서 마우스 휠로 줌 인/아웃 가능
/// 
/// 사용법:
/// 1. 이 스크립트를 블록 코딩 UI 패널에 추가
/// 2. contentToZoom에 확대/축소할 RectTransform 연결 (예: ProgrammingEnv)
/// 3. zoomArea에 줌을 감지할 영역 연결 (예: BlockCodePanel)
/// </summary>
public class UIZoomController : MonoBehaviour
{
    [Header("=== Zoom Target ===")]
    [Tooltip("확대/축소할 콘텐츠 (예: ProgrammingEnv의 RectTransform)")]
    [SerializeField] RectTransform contentToZoom;
    
    [Tooltip("줌을 감지할 영역 (이 영역 위에서만 줌 동작)")]
    [SerializeField] RectTransform zoomArea;
    
    [Header("=== Zoom Settings ===")]
    [Tooltip("줌 속도")]
    [SerializeField] float zoomSpeed = 0.1f;
    
    [Tooltip("최소 줌 배율")]
    [SerializeField] float minScale = 0.5f;
    
    [Tooltip("최대 줌 배율")]
    [SerializeField] float maxScale = 2.5f;
    
    [Tooltip("줌 애니메이션 부드러움 (0 = 즉시, 높을수록 부드러움)")]
    [SerializeField] float zoomSmoothness = 10f;
    
    [Header("=== Pivot Settings ===")]
    [Tooltip("마우스 위치 기준으로 줌 (체크 해제 시 중앙 기준)")]
    [SerializeField] bool zoomTowardsMouse = true;
    
    // 현재 줌 스케일
    float currentScale = 1f;
    float targetScale = 1f;
    
    // 캐시된 카메라
    Camera mainCamera;
    
    void Awake()
    {
        mainCamera = Camera.main;
        
        // contentToZoom이 설정되지 않았으면 자기 자신 사용
        if (contentToZoom == null)
        {
            contentToZoom = GetComponent<RectTransform>();
        }
        
        // zoomArea가 설정되지 않았으면 contentToZoom 사용
        if (zoomArea == null)
        {
            zoomArea = contentToZoom;
        }
        
        // 현재 스케일 초기화
        if (contentToZoom != null)
        {
            currentScale = contentToZoom.localScale.x;
            targetScale = currentScale;
        }
    }
    
    void Update()
    {
        HandleZoomInput();
        ApplySmoothZoom();
    }
    
    /// <summary>
    /// 마우스 휠 입력 처리
    /// </summary>
    void HandleZoomInput()
    {
        // 마우스가 줌 영역 위에 있는지 확인
        if (!IsMouseOverZoomArea())
            return;
        
        // UI 요소 위에서 다른 상호작용 중인지 확인 (드래그 등)
        // 필요시 추가 조건 체크 가능
        
        float scrollDelta = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scrollDelta) > 0.01f)
        {
            // 타겟 스케일 계산
            targetScale += scrollDelta * zoomSpeed;
            targetScale = Mathf.Clamp(targetScale, minScale, maxScale);
            
            // 마우스 위치 기준 줌 (피벗 조정)
            if (zoomTowardsMouse && contentToZoom != null)
            {
                AdjustPivotToMouse();
            }
        }
    }
    
    /// <summary>
    /// 부드러운 줌 애니메이션 적용
    /// </summary>
    void ApplySmoothZoom()
    {
        if (contentToZoom == null)
            return;
        
        // 현재 스케일을 타겟 스케일로 보간
        if (zoomSmoothness > 0)
        {
            currentScale = Mathf.Lerp(currentScale, targetScale, Time.deltaTime * zoomSmoothness);
        }
        else
        {
            currentScale = targetScale;
        }
        
        // 스케일 적용
        contentToZoom.localScale = Vector3.one * currentScale;
    }
    
    /// <summary>
    /// 마우스가 줌 영역 위에 있는지 확인
    /// </summary>
    bool IsMouseOverZoomArea()
    {
        if (zoomArea == null)
            return false;
        
        return RectTransformUtility.RectangleContainsScreenPoint(
            zoomArea, Input.mousePosition, mainCamera);
    }
    
    /// <summary>
    /// 마우스 위치 기준으로 피벗 조정 (마우스 위치를 중심으로 줌)
    /// </summary>
    void AdjustPivotToMouse()
    {
        if (contentToZoom == null)
            return;
        
        // 마우스 위치를 로컬 좌표로 변환
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            contentToZoom, Input.mousePosition, mainCamera, out localPoint))
        {
            // 로컬 좌표를 정규화된 피벗 값으로 변환
            Vector2 normalizedPoint = new Vector2(
                (localPoint.x - contentToZoom.rect.x) / contentToZoom.rect.width,
                (localPoint.y - contentToZoom.rect.y) / contentToZoom.rect.height
            );
            
            // 피벗 변경 시 위치 보정
            Vector2 pivotDelta = normalizedPoint - contentToZoom.pivot;
            
            Vector3 positionDelta = new Vector3(
                pivotDelta.x * contentToZoom.rect.width * contentToZoom.localScale.x,
                pivotDelta.y * contentToZoom.rect.height * contentToZoom.localScale.y,
                0
            );
            
            contentToZoom.pivot = normalizedPoint;
            contentToZoom.anchoredPosition += new Vector2(positionDelta.x, positionDelta.y);
        }
    }
    
    /// <summary>
    /// 줌을 기본값(1.0)으로 리셋
    /// </summary>
    public void ResetZoom()
    {
        targetScale = 1f;
        
        // 피벗도 중앙으로 리셋
        if (contentToZoom != null)
        {
            Vector2 pivotDelta = new Vector2(0.5f, 0.5f) - contentToZoom.pivot;
            Vector3 positionDelta = new Vector3(
                pivotDelta.x * contentToZoom.rect.width * contentToZoom.localScale.x,
                pivotDelta.y * contentToZoom.rect.height * contentToZoom.localScale.y,
                0
            );
            
            contentToZoom.pivot = new Vector2(0.5f, 0.5f);
            contentToZoom.anchoredPosition += new Vector2(positionDelta.x, positionDelta.y);
        }
    }
    
    /// <summary>
    /// 현재 줌 레벨 반환
    /// </summary>
    public float GetCurrentZoom()
    {
        return currentScale;
    }
    
    /// <summary>
    /// 줌 레벨 직접 설정
    /// </summary>
    public void SetZoom(float scale)
    {
        targetScale = Mathf.Clamp(scale, minScale, maxScale);
    }
    
    /// <summary>
    /// 특정 비율만큼 줌 인
    /// </summary>
    public void ZoomIn(float amount = 0.1f)
    {
        targetScale = Mathf.Clamp(targetScale + amount, minScale, maxScale);
    }
    
    /// <summary>
    /// 특정 비율만큼 줌 아웃
    /// </summary>
    public void ZoomOut(float amount = 0.1f)
    {
        targetScale = Mathf.Clamp(targetScale - amount, minScale, maxScale);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Inspector에서 값 변경 시 즉시 반영
        if (Application.isPlaying && contentToZoom != null)
        {
            targetScale = Mathf.Clamp(targetScale, minScale, maxScale);
        }
    }
#endif
}
