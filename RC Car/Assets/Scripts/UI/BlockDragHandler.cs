using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 워크스페이스 내에서 이미 생성된 블록을 드래그하여 이동/재배치하는 핸들러
public class BlockDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform rectTransform;
    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private Transform originalParent; 

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        canvasGroup = GetComponent<CanvasGroup>();

        // CanvasGroup이 없는 경우 추가 (레이캐스트 차단 관리를 위해)
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
            Debug.Log($"[BlockDragHandler Awake] '{gameObject.name}'에 CanvasGroup이 없어 새로 추가했습니다.");
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        canvasGroup.blocksRaycasts = false;
        
        // 1. 드래그 시작 시 임시로 Canvas의 최상위로 부모 변경 (레이아웃 그룹에서 분리)
        originalParent = rectTransform.parent; 
        rectTransform.SetParent(canvas.transform); 
        rectTransform.SetAsLastSibling(); 

        // **[추가된 로직]** 드래그 시작 시 블록 체인에서 논리적으로 분리
        BlockView blockView = GetComponent<BlockView>();
        if (blockView != null)
        {
            blockView.DisconnectFromChain();
        }

        Debug.Log($"[BlockDragHandler BeginDrag] 워크스페이스 블록 '{gameObject.name}' 드래그 시작. 원래 부모: {originalParent.name}. 레이아웃 그룹에서 분리.");
    }

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
        // Debug.Log($"[BlockDragHandler Dragging] 블록 '{gameObject.name}' 이동 중. 현재 위치: {rectTransform.anchoredPosition}"); // 너무 많은 로그 방지
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 2. 드래그 종료 시 원래 부모 (Scroll View B Content)로 복귀
        rectTransform.SetParent(originalParent); 
        
        // 레이아웃 그룹이 위치를 다시 잡도록 강제 업데이트
        // LayoutGroup이 Content B에 붙어있을 경우에만 필요합니다.
        LayoutRebuilder.ForceRebuildLayoutImmediate(originalParent.GetComponent<RectTransform>());

        canvasGroup.blocksRaycasts = true;
        Debug.Log($"[BlockDragHandler EndDrag] 워크스페이스 블록 '{gameObject.name}' 드래그 종료. 원래 부모 '{originalParent.name}'로 복귀 및 레이아웃 재구성.");
    }
}