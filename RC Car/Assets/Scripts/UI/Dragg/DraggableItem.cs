using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Canvas canvas;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        canvas = GetComponentInParent<Canvas>();

        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            Debug.Log($"[DraggableItem Awake] '{gameObject.name}'에 CanvasGroup이 없어 새로 추가했습니다.");
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = false;
        DragImageController.Instance.Show(GetComponent<Image>().sprite);
        Debug.Log($"[DraggableItem BeginDrag] 팔레트 블록 '{gameObject.name}' 드래그 시작. Drag Image Controller 활성화.");
    }

    public void OnDrag(PointerEventData eventData)
    {
        DragImageController.Instance.Move(eventData.position);
        // Debug.Log($"[DraggableItem Dragging] Drag Image 이동 중."); // 너무 많은 로그 방지
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = true;
        DragImageController.Instance.Hide();
        Debug.Log($"[DraggableItem EndDrag] 팔레트 블록 '{gameObject.name}' 드래그 종료. Drag Image Controller 비활성화.");
    }
}