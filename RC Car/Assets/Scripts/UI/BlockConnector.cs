using UnityEngine;
using UnityEngine.EventSystems;

public class BlockConnector : MonoBehaviour, IDropHandler
{
    // 이 커넥터가 붙어있는 '위쪽' 블록
    public BlockView ParentBlock; 
    
    // 워크스페이스 Content의 RectTransform (캐싱용)
    private RectTransform workspaceContentRect;

    void Awake()
    {
        // 부모 BlockView는 항상 자신의 바로 위에 존재하므로 Awake에서 찾아도 안전합니다.
        ParentBlock = GetComponentInParent<BlockView>();
        if (ParentBlock == null)
        {
             // 이 로그는 프리팹 구조가 잘못되었을 때만 떠야 합니다.
             Debug.LogError($"[BlockConnector] '{gameObject.name}'의 부모에서 BlockView를 찾을 수 없습니다.");
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem item = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (item == null) return;

        // 드롭이 일어난 시점에 워크스페이스 Content 찾기 (Lazy Initialization)
        if (workspaceContentRect == null)
        {
            // 현재 이 커넥터가 포함된 블록(ParentBlock)의 부모가 바로 워크스페이스 Content여야 합니다.
            if (ParentBlock != null && ParentBlock.transform.parent != null)
            {
                workspaceContentRect = ParentBlock.transform.parent.GetComponent<RectTransform>();
                
                // 안전장치: 찾은 부모가 진짜 워크스페이스인지 확인 (DropZone 컴포넌트 유무로 판단)
                if (ParentBlock.transform.parent.GetComponent<DropZone>() == null)
                {
                    Debug.LogWarning("[BlockConnector] 현재 블록이 워크스페이스에 있지 않은 것 같습니다. 삽입을 취소합니다.");
                    workspaceContentRect = null; // 잘못 찾았으므로 다시 null 처리
                    return;
                }
            }
        }

        if (workspaceContentRect == null)
        {
            Debug.LogError("[BlockConnector] 워크스페이스 Content를 찾을 수 없어 삽입할 수 없습니다.");
            return;
        }

        // 1. 새로운 오브젝트 복제 (워크스페이스 Content의 자식으로)
        GameObject clone = Instantiate(item.gameObject, workspaceContentRect);
        clone.name = item.gameObject.name + " (Inserted)";
        
        BlockView newBlockView = clone.GetComponent<BlockView>();
        if (newBlockView == null)
        {
            Destroy(clone);
            return;
        }
        
        // 2. 복제된 블록을 워크스페이스용으로 설정
        Destroy(clone.GetComponent<DraggableItem>());
        if (clone.GetComponent<BlockDragHandler>() == null)
        {
            clone.AddComponent<BlockDragHandler>();
        }

        // 3. 연결/삽입 로직
        BlockView blockAbove = ParentBlock;
        BlockView blockBelow = blockAbove.NextBlock;

        // A. 새로운 블록을 상위 블록에 연결
        blockAbove.NextBlock = newBlockView;
        newBlockView.PreviousBlock = blockAbove;

        // B. 새로운 블록을 하위 블록에 연결
        newBlockView.NextBlock = blockBelow;
        if (blockBelow != null)
        {
            blockBelow.PreviousBlock = newBlockView;
        }

        // 4. BlockNode 체인 업데이트
        blockAbove.UpdateNextNodeChain(); 

        // 5. 위치 조정
        RectTransform newBlockRect = newBlockView.GetComponent<RectTransform>();
        RectTransform blockAboveRect = blockAbove.GetComponent<RectTransform>();
        
        // 삽입된 블록 초기 Y 위치 설정
        float newY = blockAboveRect.anchoredPosition.y - blockAboveRect.sizeDelta.y - 10f; 
        newBlockRect.anchoredPosition = new Vector2(blockAboveRect.anchoredPosition.x, newY);
        
        // 전체 체인 위치 재조정
        newBlockView.UpdatePositionOfChain(blockAboveRect.anchoredPosition.x); 
        
        Debug.Log($"[BlockConnector] '{newBlockView.name}' 삽입 완료.");
    }
}