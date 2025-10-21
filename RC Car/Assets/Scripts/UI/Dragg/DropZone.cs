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
            // 2. 피벗 보정 계산
            // 피벗(pivot)은 블록의 중심점 비율 (0~1)
            // sizeDelta는 블록의 크기
            // pivot.x * sizeDelta.x = 피벗부터 왼쪽 모서리까지의 거리
            // (1 - pivot.x) * sizeDelta.x = 피벗부터 오른쪽 모서리까지의 거리
            
            // localPoint는 마우스가 클릭된 Content 내의 위치입니다.
            // 이 위치가 블록의 피벗 위치가 되도록 설정합니다.
            
            // 만약 블록의 피벗이 (0.5, 0.5)라면 보정값은 (0, 0)
            // 만약 블록의 피벗이 (0, 1) (왼쪽 상단)이라면, 블록의 왼쪽 상단이 localPoint에 오게 됩니다.
            // 블록의 anchoredPosition = localPoint - (블록의 크기에 따른 오프셋) 이 필요할 수 있습니다.
            
            // **하지만, 가장 일반적인 UI 드래그앤드롭에서는**
            // **클릭된 지점(localPoint)을 블록의 피벗 위치로 설정하는 것이 직관적입니다.**
            // **따라서, `anchoredPosition = localPoint`를 유지하고,**
            // **블록 프리팹의 피벗을 (0.5, 0.5) (중앙)으로 설정하는 것을 강력히 권장합니다.**
            
            // **임시 보정 코드 (블록의 피벗이 중앙이 아닐 경우):**
            // Vector2 pivotOffset = new Vector2(
            //     (0.5f - cloneRect.pivot.x) * cloneRect.sizeDelta.x,
            //     (0.5f - cloneRect.pivot.y) * cloneRect.sizeDelta.y
            // );
            // cloneRect.anchoredPosition = localPoint + pivotOffset;

            // **일반적인 정답 (Content의 로컬 좌표 = 블록의 앵커 위치):**
            cloneRect.anchoredPosition = localPoint;
            
            Debug.Log($"[DropZone OnDrop] 블록 '{clone.name}'을(를) 로컬 위치 {localPoint}에 배치했습니다. (클론 피벗: {cloneRect.pivot})");
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