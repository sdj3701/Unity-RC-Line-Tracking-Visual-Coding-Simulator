using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DropZone : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem item = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (item == null) 
        {
            Debug.LogWarning("[DropZone OnDrop] 드롭된 오브젝트에 DraggableItem 컴포넌트가 없습니다. 워크스페이스 내 드래그일 수 있습니다.");
            return;
        }

        // 1. 새로운 오브젝트 복제 (Content B (DropZone)의 자식으로)
        GameObject clone = Instantiate(item.gameObject, transform);
        clone.name = item.gameObject.name + " (Workspace)";
        
        Debug.Log($"[DropZone OnDrop] 팔레트 블록 '{item.gameObject.name}' 드롭 감지. 워크스페이스 블록 '{clone.name}' 복제 완료.");

        // **수정된 코드: 마우스 드롭 위치에 블록 배치 및 피벗 보정**
        RectTransform contentRect = GetComponent<RectTransform>();
        RectTransform cloneRect = clone.GetComponent<RectTransform>();
        Vector2 localPoint;
        
        // Screen Point (마우스 위치)를 Content RectTransform의 Local Point로 변환
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRect, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            // ----------------------------------------------------------------------------------
            // **새로운 디버그 로그 추가: 입력 및 변환 정보**
            Debug.Log($"[DEBUG POS] 1. 마우스 드롭 위치 (스크린): {eventData.position}");
            Debug.Log($"[DEBUG POS] 2. 드롭존 로컬 위치 (localPoint): {localPoint}");
            // ----------------------------------------------------------------------------------

            // **피벗 보정 적용:**
            // 블록의 중앙 (0.5, 0.5)을 마우스 위치(localPoint)에 정확히 맞추기 위해 피벗을 보정합니다.
            Vector2 pivotOffset = new Vector2(
                (0.5f - cloneRect.pivot.x) * cloneRect.sizeDelta.x,
                (0.5f - cloneRect.pivot.y) * cloneRect.sizeDelta.y
            );
            
            // 마우스 위치 (localPoint)에 피벗 오프셋을 더하여 블록의 중심이 localPoint에 오도록 설정
            cloneRect.anchoredPosition = localPoint + pivotOffset;
            
            // ----------------------------------------------------------------------------------
            // **새로운 디버그 로그 추가: 보정 및 최종 위치 정보**
            Debug.Log($"[DEBUG POS] 3. 클론 RectTransform 정보 - Pivot: {cloneRect.pivot}, SizeDelta: {cloneRect.sizeDelta}");
            Debug.Log($"[DEBUG POS] 4. 계산된 피벗 오프셋: {pivotOffset}");
            Debug.Log($"[DEBUG POS] 5. 최종 설정된 AnchoredPosition: {cloneRect.anchoredPosition}");
            // ----------------------------------------------------------------------------------
        }
        else
        {
            Debug.LogWarning("[DropZone OnDrop] ScreenPointToLocalPointInRectangle 변환 실패. 블록 위치 설정에 실패했습니다.");
        }
        // **------------------------------------------**


        // 2. 복제된 블록을 워크스페이스용으로 설정 (이하 동일)
        
        // 2-1. 팔레트용 DraggableItem 제거
        DraggableItem originalDraggable = clone.GetComponent<DraggableItem>();
        if (originalDraggable != null)
        {
             Destroy(originalDraggable);
             Debug.Log($"[DropZone OnDrop] 복제된 블록에서 DraggableItem 제거.");
        }

        // 2-2. 워크스페이스 내 이동을 위한 BlockDragHandler 추가/확인
        BlockDragHandler workspaceDragHandler = clone.GetComponent<BlockDragHandler>();
        if (workspaceDragHandler == null)
        {
            // BlockDragHandler가 없다면 추가합니다.
            workspaceDragHandler = clone.AddComponent<BlockDragHandler>();
            Debug.Log($"[DropZone OnDrop] 복제된 블록에 BlockDragHandler 추가.");
        }
        else
        {
            // 이미 있다면 활성화만 합니다.
            workspaceDragHandler.enabled = true;
            Debug.Log($"[DropZone OnDrop] 복제된 블록의 BlockDragHandler 활성화.");
        }

        // (선택 사항) UI 계층 구조에서 가장 위에 오도록 설정
        clone.transform.SetAsLastSibling();

        Debug.Log($"[DropZone OnDrop] 블록 '{clone.name}'이(가) 스크롤 뷰 B(워크스페이스)로 성공적으로 이동(복제)되었습니다.");
    }
}