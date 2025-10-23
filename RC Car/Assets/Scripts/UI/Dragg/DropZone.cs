// Assets/Scripts/UI/Dragg/DropZone.cs

using UnityEngine;
using UnityEngine.EventSystems;
using System.Linq;

public class DropZone : MonoBehaviour, IDropHandler
{
    private const float CONNECTION_DISTANCE_THRESHOLD = 300f; 
    private const float BLOCK_GAP = 10f; 

    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem item = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (item == null) return;

        // 1. 드롭 위치를 워크스페이스 Content의 로컬 좌표로 변환
        RectTransform contentRect = GetComponent<RectTransform>();
        Vector2 dropLocalPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRect, eventData.position, eventData.pressEventCamera, out dropLocalPoint))
        {
            return;
        }

        // 2. 워크스페이스 내 모든 블록을 찾고, 드롭 위치와 가장 가까운 블록을 찾습니다.
        BlockView[] existingBlocks = GetComponentsInChildren<BlockView>().Where(b => b.transform.parent == transform).ToArray();
        
        BlockView closestBlock = null;
        float minDistance = CONNECTION_DISTANCE_THRESHOLD;

        foreach (BlockView block in existingBlocks)
        {
            RectTransform blockRect = block.GetComponent<RectTransform>();
            float distance = Vector2.Distance(dropLocalPoint, blockRect.anchoredPosition);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestBlock = block;
            }
        }

        // 3. 블록 복제 (Instantiate)
        GameObject clone = Instantiate(item.gameObject, transform);
        clone.name = item.gameObject.name + " (Workspace)";
        BlockView newBlockView = clone.GetComponent<BlockView>();
        if (newBlockView == null) { Destroy(clone); return; }

        // 워크스페이스용 설정
        Destroy(clone.GetComponent<DraggableItem>());
        if (clone.GetComponent<BlockDragHandler>() == null) { clone.AddComponent<BlockDragHandler>(); }
        clone.transform.SetAsLastSibling();
        
        RectTransform newRect = newBlockView.GetComponent<RectTransform>();
        
        // 피벗 보정 값 계산
        Vector2 pivotOffset = new Vector2(
            (0.5f - newRect.pivot.x) * newRect.sizeDelta.x, 
            (0.5f - newRect.pivot.y) * newRect.sizeDelta.y
        );
        
        // 4. 연결/삽입 로직 실행
        if (closestBlock != null)
        {
            RectTransform closestRect = closestBlock.GetComponent<RectTransform>();
            float closestBlockBottomY = closestRect.anchoredPosition.y - closestRect.sizeDelta.y;
            
            // 드롭 위치가 블록의 아래 연결 지점(NextBlock)에 가까운 경우
            bool isNearConnectionPoint = dropLocalPoint.y < closestBlockBottomY + (BLOCK_GAP / 2);
            
            if (isNearConnectionPoint)
            {
                // 연결/삽입 위치의 Y 좌표 계산
                float targetY = closestBlockBottomY - BLOCK_GAP;
                float targetX = closestRect.anchoredPosition.x; // [핵심] X축은 가장 가까운 블록에 고정
                
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
                    
                    Debug.Log($"[DropZone] 블록 '{newBlockView.name}'을(를) '{blockAbove.name}'와 '{blockBelow.name}' 사이에 삽입했습니다. X={targetX}로 정렬.");
                }
                else
                {
                    // **끝에 연결**
                    closestBlock.NextBlock = newBlockView;
                    newBlockView.PreviousBlock = closestBlock;
                    
                    newRect.anchoredPosition = new Vector2(targetX, targetY);
                    closestBlock.UpdateNextNodeChain();
                    
                    Debug.Log($"[DropZone] 블록 '{newBlockView.name}'을(를) '{closestBlock.name}' 끝에 연결했습니다. X={targetX}로 정렬.");
                }
            }
            else
            {
                // **자유 배치** (가장 가까운 블록은 있지만 연결 지점이 아닌 경우)
                // [수정] Y는 마우스 위치를 따르되, X는 가장 가까운 블록의 X축을 따릅니다.
                float targetX = closestRect.anchoredPosition.x; // [핵심] X축은 가장 가까운 블록에 고정
                float targetY = dropLocalPoint.y + pivotOffset.y;
                
                newRect.anchoredPosition = new Vector2(targetX, targetY);
                newBlockView.UpdateNextNodeChain();
                
                Debug.Log($"[DropZone] 블록 '{newBlockView.name}'을(를) 자유 배치했습니다. X={targetX}로 정렬.");
            }
        }
        else 
        {
            // 5. 가장 가까운 블록이 없는 경우 (새 체인 시작)
            // [수정] 마우스 위치를 따릅니다. (가장 가까운 블록이 없으므로 정렬 기준이 없습니다.)
            newRect.anchoredPosition = dropLocalPoint + pivotOffset;
            newBlockView.UpdateNextNodeChain();
            
            Debug.Log($"[DropZone] 블록 '{newBlockView.name}'을(를) 새 체인으로 시작했습니다.");
        }
    }
}