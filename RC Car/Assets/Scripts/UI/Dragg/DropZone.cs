using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DropZone : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData eventData)
    {
        DraggableItem item = eventData.pointerDrag.GetComponent<DraggableItem>();
        if (item == null) return;

        // 새로운 오브젝트 복제
        GameObject clone = Instantiate(item.gameObject, transform);
        clone.transform.SetParent(transform, false);

        // 위치 조정
        RectTransform rect = clone.GetComponent<RectTransform>();
        rect.anchoredPosition = Vector2.zero; // 필요시 eventData.position 기반으로 보정

        Debug.Log($"드롭됨: {clone.name}");
    }
}
