using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DropZone : MonoBehaviour, IDropHandler
{
    // 워크스페이스에서 마지막으로 생성된 블록을 추적하는 변수 추가
    // 이 블록의 NextBlock에 새로운 블록을 연결합니다.
    private BlockView lastPlacedBlock = null;

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
        
        // BlockView 컴포넌트 가져오기
        BlockView newBlockView = clone.GetComponent<BlockView>();
        if (newBlockView == null)
        {
            Debug.LogError($"[DropZone OnDrop] 복제된 블록 '{clone.name}'에 BlockView 컴포넌트가 없습니다. 연결 로직을 실행할 수 없습니다.");
            Destroy(clone); // BlockView가 없으면 블록이 아님
            return;
        }

        // **새로운 로직 1: 이전 블록과의 연결**
        if (lastPlacedBlock != null)
        {
            // 이전에 생성된 블록의 NextBlock 변수에 현재 생성된 블록을 연결합니다.
            lastPlacedBlock.NextBlock = newBlockView;
            Debug.Log($"[DropZone OnDrop] 블록 '{lastPlacedBlock.name}'을(를) 블록 '{newBlockView.name}'에 연결했습니다.");
            
            // **선택 사항: 연결된 블록의 위치를 이전 블록 아래로 재조정**
            // 마우스 위치 대신, 연결된 블록의 위치를 이전 블록 바로 아래에 붙여서 생성합니다.
            // 이 기능을 원치 않으면 아래 위치 설정 로직을 주석 처리하고 마우스 위치 로직을 사용하세요.
            
            RectTransform lastRect = lastPlacedBlock.GetComponent<RectTransform>();
            RectTransform newRect = newBlockView.GetComponent<RectTransform>();

            // 이전 블록의 Y 위치 - 이전 블록의 높이 - 간격
            float newY = lastRect.anchoredPosition.y - lastRect.sizeDelta.y - 10f; // 10f는 간격 (원하는 값으로 조정)
            
            // X 위치는 이전 블록과 동일하게 설정
            newRect.anchoredPosition = new Vector2(lastRect.anchoredPosition.x, newY);
            
            Debug.Log($"[DropZone OnDrop] 연결된 블록 '{newBlockView.name}'을(를) '{lastPlacedBlock.name}' 아래 ({newRect.anchoredPosition})로 배치했습니다.");
        }


        // **새로운 로직 2: 마우스 드롭 위치 설정 (첫 번째 블록이거나 연결된 블록을 자유롭게 배치할 경우)**
        if (lastPlacedBlock == null || lastPlacedBlock.NextBlock == null)
        {
            // 첫 번째 블록이거나, 연결된 블록을 마우스 위치에 자유롭게 배치하고 싶다면 이 로직을 사용합니다.
            RectTransform contentRect = GetComponent<RectTransform>();
            RectTransform cloneRect = clone.GetComponent<RectTransform>();
            Vector2 localPoint;
            
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRect, eventData.position, eventData.pressEventCamera, out localPoint))
            {
                // 피벗 보정 적용 (블록의 중앙을 마우스 위치에 맞춤)
                Vector2 pivotOffset = new Vector2(
                    (0.5f - cloneRect.pivot.x) * cloneRect.sizeDelta.x,
                    (0.5f - cloneRect.pivot.y) * cloneRect.sizeDelta.y
                );
                
                cloneRect.anchoredPosition = localPoint + pivotOffset;
                Debug.Log($"[DropZone OnDrop] 첫 블록 또는 자유 배치 블록 '{clone.name}'을(를) 로컬 위치 {cloneRect.anchoredPosition}에 배치했습니다.");
            }
        }


        // **새로운 로직 3: 마지막 블록 업데이트**
        // 현재 생성된 블록을 다음 연결을 위한 마지막 블록으로 설정합니다.
        lastPlacedBlock = newBlockView;


        // 2. 복제된 블록을 워크스페이스용으로 설정 (기존 코드)
        
        // 2-1. 팔레트용 DraggableItem 제거
        DraggableItem originalDraggable = clone.GetComponent<DraggableItem>();
        if (originalDraggable != null)
        {
             Destroy(originalDraggable);
        }

        // 2-2. 워크스페이스 내 이동을 위한 BlockDragHandler 추가/확인
        BlockDragHandler workspaceDragHandler = clone.GetComponent<BlockDragHandler>();
        if (workspaceDragHandler == null)
        {
            workspaceDragHandler = clone.AddComponent<BlockDragHandler>();
        }
        else
        {
            workspaceDragHandler.enabled = true;
        }

        // (선택 사항) UI 계층 구조에서 가장 위에 오도록 설정
        clone.transform.SetAsLastSibling();

        Debug.Log($"[DropZone OnDrop] 블록 '{clone.name}'이(가) 스크롤 뷰 B(워크스페이스)로 성공적으로 이동(복제)되었습니다.");
    }
}