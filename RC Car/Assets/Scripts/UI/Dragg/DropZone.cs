using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;
using UnityEngine.UI;

public class DropZone : MonoBehaviour, IDropHandler
{
    private const float CONNECTION_DISTANCE_THRESHOLD = 100f; 
    private const float BLOCK_GAP = 10f; 

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem item = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (item == null) return;

        RectTransform contentRect = GetComponent<RectTransform>();
        Vector2 dropLocalPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRect, eventData.position, eventData.pressEventCamera, out dropLocalPoint))
        {
            return;
        }

        BlockView[] existingBlocks = GetComponentsInChildren<BlockView>().Where(b => b.transform.parent == transform).ToArray();
        
        BlockView closestBlock = null;
        float minDistance = CONNECTION_DISTANCE_THRESHOLD;
        // [추가] 가장 가까운 연결 지점이 'Top'인지 'Bottom'인지 저장
        bool connectToTop = false; 

        // 1. 가장 가까운 '연결 지점(블록의 상단 또는 하단)'을 찾습니다.
        foreach (BlockView block in existingBlocks)
        {
            RectTransform blockRect = block.GetComponent<RectTransform>();
            float blockHeight = blockRect.rect.height;

            // 1-1. 하단 연결 지점 (NextBlock) 계산
            float blockBottomY = blockRect.anchoredPosition.y - blockHeight;
            float distanceToBottom = Mathf.Abs(dropLocalPoint.y - blockBottomY);

            // 1-2. 상단 연결 지점 (PreviousBlock) 계산
            float blockTopY = blockRect.anchoredPosition.y; 
            float distanceToTop = Mathf.Abs(dropLocalPoint.y - blockTopY);
            
            // X축 거리 (너무 먼 옆의 블록에 붙는 것 방지)
            float distanceX = Mathf.Abs(dropLocalPoint.x - blockRect.anchoredPosition.x);

            // X축 거리가 너무 멀면 패스
            if (distanceX >= 150f) continue; 

            // 1-3. 가장 가까운 지점 판별 (Top vs Bottom)
            if (distanceToBottom < minDistance && distanceToBottom <= distanceToTop)
            {
                // 하단(Bottom)이 더 가깝고 임계값 이내인 경우
                minDistance = distanceToBottom;
                closestBlock = block;
                connectToTop = false; // 하단에 연결
            }
            else if (distanceToTop < minDistance && distanceToTop < distanceToBottom)
            {
                // 상단(Top)이 더 가깝고 임계값 이내인 경우
                minDistance = distanceToTop;
                closestBlock = block;
                connectToTop = true; // 상단에 연결
            }
        }

        // 2. 블록 복제 및 워크스페이스용 설정
        GameObject clone = Instantiate(item.gameObject, transform);
        clone.name = item.gameObject.name + " (Workspace)";
        BlockView newBlockView = clone.GetComponent<BlockView>();
        if (newBlockView == null) { Destroy(clone); return; }

        Destroy(clone.GetComponent<DraggableItem>());
        if (clone.GetComponent<BlockDragHandler>() == null) { clone.AddComponent<BlockDragHandler>(); }
        clone.transform.SetAsLastSibling();
        
        RectTransform newRect = newBlockView.GetComponent<RectTransform>();
        // 새로 생성된 블록의 레이아웃을 강제 업데이트하여 rect.height를 확정 (BlockConnector.cs 참고)
        LayoutRebuilder.ForceRebuildLayoutImmediate(newRect); 
        float newBlockHeight = newRect.rect.height;
        
        Vector2 pivotOffset = new Vector2(
            (0.5f - newRect.pivot.x) * newRect.sizeDelta.x, 
            (0.5f - newRect.pivot.y) * newRect.sizeDelta.y
        );
        
        // 3. 연결/삽입 로직 실행
        if (closestBlock != null)
        {
            RectTransform closestRect = closestBlock.GetComponent<RectTransform>();
            float targetX = closestRect.anchoredPosition.x;

            if (connectToTop)
            {
                // **[새로운 로직]** 상단에 연결 (PreviousBlock으로 삽입)
                
                // 기존 체인 분리 및 새 연결 관계 설정
                BlockView blockBelow = closestBlock;
                BlockView blockAbove = closestBlock.PreviousBlock;

                // 새로운 블록의 Y 위치 = 아래 블록의 Y 위치 + 새 블록의 높이 + 간격
                float targetY = blockBelow.GetComponent<RectTransform>().anchoredPosition.y + newBlockHeight + BLOCK_GAP;
                
                // 3-1. 아래 블록 (closestBlock) 연결 업데이트
                blockBelow.PreviousBlock = newBlockView;

                // 3-2. 새로운 블록 연결 업데이트
                newBlockView.NextBlock = blockBelow;
                newBlockView.PreviousBlock = blockAbove;

                // 3-3. 위 블록 연결 업데이트
                if (blockAbove != null)
                {
                    blockAbove.NextBlock = newBlockView;
                    blockAbove.UpdateNextNodeChain(); // 노드 체인 업데이트
                }
                
                // 위치 및 체인 업데이트 (새 블록에서 시작하여 위로 밀어 올리는 것이 아니라, 아래 블록의 위치를 기준으로 새 블록의 위치를 정하고, 새 블록에서 아래 체인을 재정렬합니다.)
                newRect.anchoredPosition = new Vector2(targetX, targetY);
                // 새 블록의 위치가 정해졌으므로, 새 블록에서 시작하여 아래 체인의 위치를 재조정합니다.
                newBlockView.UpdatePositionOfChain(targetX); 
                
                Debug.Log($"<color=green>[DropZone] 상단 삽입 성공: { (blockAbove != null ? blockAbove.name : "NULL") } -> [{newBlockView.name}] -> {blockBelow.name}</color>");
            }
            else // connectToBottom (기존 로직: 중간 삽입 또는 끝에 연결)
            {
                // 하단 연결 위치 계산
                float closestBlockBottomY = closestRect.anchoredPosition.y - closestRect.rect.height;
                float targetY = closestBlockBottomY - BLOCK_GAP;
                
                if (closestBlock.NextBlock != null)
                {
                    // **중간 삽입**
                    BlockView blockAbove = closestBlock;
                    BlockView blockBelow = closestBlock.NextBlock;

                    blockAbove.NextBlock = newBlockView;
                    newBlockView.PreviousBlock = blockAbove;
                    newBlockView.NextBlock = blockBelow;
                    blockBelow.PreviousBlock = newBlockView;

                    newRect.anchoredPosition = new Vector2(targetX, targetY);
                    newBlockView.UpdatePositionOfChain(targetX);
                    blockAbove.UpdateNextNodeChain(); 
                    
                    Debug.Log($"<color=red>[DropZone] 하단 중간 삽입 성공: {blockAbove.name} -> [{newBlockView.name}] -> {blockBelow.name}</color>");
                }
                else
                {
                    // **끝에 연결**
                    closestBlock.NextBlock = newBlockView;
                    newBlockView.PreviousBlock = closestBlock;
                    
                    newRect.anchoredPosition = new Vector2(targetX, targetY);
                    closestBlock.UpdateNextNodeChain();
                    
                    Debug.Log($"<color=blue>[DropZone] 하단 끝 연결 성공: {closestBlock.name} -> [{newBlockView.name}]</color>");
                }
            }
        }
        else 
        {
            // 4. 자유 배치 (새 체인 시작)
            newRect.anchoredPosition = dropLocalPoint + pivotOffset;
            newBlockView.UpdateNextNodeChain();
            Debug.Log($"<color=yellow>[DropZone] 자유 배치 (연결할 가까운 지점 없음)</color>");
        }
    }
}