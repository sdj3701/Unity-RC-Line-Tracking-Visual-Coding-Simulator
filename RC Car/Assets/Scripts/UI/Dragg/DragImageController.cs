using UnityEngine;
using UnityEngine.UI;

public class DragImageController : MonoBehaviour
{
    public static DragImageController Instance;
    public Image dragPreviewImage;
    // 추가할 변수: 캔버스 컴포넌트
    private Canvas dragCanvas;

    private void Awake()
    {
        Instance = this;
        dragPreviewImage.gameObject.SetActive(false);

        // **새로운 코드: DragImage에 Canvas 컴포넌트를 추가하고 최상위 렌더링 설정**
        dragCanvas = GetComponent<Canvas>();
        if (dragCanvas == null)
        {
            dragCanvas = gameObject.AddComponent<Canvas>();
        }

        // 1. Render Mode는 Root Canvas를 따르거나, 필요하다면 Screen Space - Overlay로 설정
        // dragCanvas.renderMode = RenderMode.ScreenSpaceOverlay; // Root Canvas가 Overlay가 아닐 경우 필요
        
        // 2. 다른 모든 Canvas보다 높은 Sort Order를 강제로 부여합니다.
        // 예를 들어, 10000과 같은 매우 큰 값을 사용하여 최상위로 만듭니다.
        dragCanvas.overrideSorting = true; // 이 Canvas의 정렬 순서를 강제로 사용
        dragCanvas.sortingOrder = 10000;
        
        // (선택 사항) 드래그 이미지가 UI 이벤트(클릭)를 막지 않도록 Canvas Group 설정 확인
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        canvasGroup.blocksRaycasts = false; // 드래그 이미지는 레이캐스트를 막지 않음
        
        Debug.Log($"[DragImageController] Drag Canvas Sorting Order를 {dragCanvas.sortingOrder}로 설정하여 최상위 렌더링을 강제했습니다.");
    }

    public void Show(Sprite sprite)
    {
        dragPreviewImage.sprite = sprite;
        dragPreviewImage.gameObject.SetActive(true);
    }

    public void Move(Vector3 position)
    {
        dragPreviewImage.transform.position = position;
    }

    public void Hide()
    {
        dragPreviewImage.gameObject.SetActive(false);
    }
}
