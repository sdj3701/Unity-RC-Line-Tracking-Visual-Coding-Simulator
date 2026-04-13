using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ContentNextPager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform content;

    [Header("Paging")]
    [SerializeField] private int[] pageStartIndices = { 0, 4, 8 };
    [SerializeField] private float moveDuration = 0.2f;
    [SerializeField] private bool snapToFirstPageOnStart = true;

    private int currentPageIndex;
    private float itemStride;
    private Coroutine moveCoroutine;

    private void Start()
    {
        CacheItemStride();

        if (snapToFirstPageOnStart)
        {
            currentPageIndex = 0;
            MoveToPage(currentPageIndex, true);
        }
    }

    // Connect this to the Next button OnClick().
    public void OnClickNext()
    {
        if (content == null || pageStartIndices == null || pageStartIndices.Length == 0)
        {
            return;
        }

        currentPageIndex = (currentPageIndex + 1) % pageStartIndices.Length;
        MoveToPage(currentPageIndex, false);
    }

    public void MoveToFirstPage()
    {
        currentPageIndex = 0;
        MoveToPage(currentPageIndex, false);
    }

    private void CacheItemStride()
    {
        if (content == null)
        {
            Debug.LogError("[ContentNextPager] Content is not assigned.");
            return;
        }

        if (content.childCount <= 0)
        {
            itemStride = 0f;
            Debug.LogWarning("[ContentNextPager] Content has no child items.");
            return;
        }

        Canvas.ForceUpdateCanvases();

        RectTransform first = content.GetChild(0) as RectTransform;
        RectTransform second = content.childCount > 1 ? content.GetChild(1) as RectTransform : null;

        float spacing = 0f;
        HorizontalLayoutGroup horizontalLayoutGroup = content.GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayoutGroup != null)
        {
            spacing = horizontalLayoutGroup.spacing;
        }

        itemStride = first != null ? first.rect.width + spacing : 0f;

        if (first != null && second != null)
        {
            float measuredStride = Mathf.Abs(second.anchoredPosition.x - first.anchoredPosition.x);
            if (measuredStride > 0.001f)
            {
                itemStride = measuredStride;
            }
        }

        if (itemStride <= 0.001f)
        {
            Debug.LogWarning("[ContentNextPager] Item stride is too small. Check Content layout settings.");
        }
    }

    private void MoveToPage(int pageIndex, bool instant)
    {
        if (content == null || pageStartIndices == null || pageStartIndices.Length == 0)
        {
            return;
        }

        int safeStartIndex = Mathf.Max(0, pageStartIndices[pageIndex]);
        float targetX = -safeStartIndex * itemStride;
        Vector2 targetPosition = new Vector2(targetX, content.anchoredPosition.y);

        if (instant || moveDuration <= 0f)
        {
            content.anchoredPosition = targetPosition;
            return;
        }

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        moveCoroutine = StartCoroutine(MoveContentCoroutine(targetPosition));
    }

    private IEnumerator MoveContentCoroutine(Vector2 targetPosition)
    {
        Vector2 startPosition = content.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            content.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, t);
            yield return null;
        }

        content.anchoredPosition = targetPosition;
        moveCoroutine = null;
    }

    private void OnValidate()
    {
        if (pageStartIndices == null || pageStartIndices.Length == 0)
        {
            pageStartIndices = new[] { 0, 4, 8 };
        }

        for (int i = 0; i < pageStartIndices.Length; i++)
        {
            pageStartIndices[i] = Mathf.Max(0, pageStartIndices[i]);
        }

        if (moveDuration < 0f)
        {
            moveDuration = 0f;
        }
    }
}
