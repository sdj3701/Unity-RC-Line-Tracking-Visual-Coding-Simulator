using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MG_BlocksEngine2.Environment;

public class ButtonUIHideAndShow : MonoBehaviour
{
    [Header("UI 참조")]
    public RectTransform targetPanel;
    public RectTransform arrowImage;
    public GameObject[] targetCanvas;

    [Header("설정")]
    public bool isExpanded = true;

    [Header("패널 너비")]
    public float collapsedWidth = 567f;
    public float expandedWidth = 1500f;

    [Header("숨김 레이아웃")]
    public float hiddenTop = 137.55f;
    public float hiddenBottom = 0f;
    public float hiddenWidth = 1450f;
    public float hiddenWidth2 = 1045f;
    public float hiddenWidth3 = 1988f;

    [Header("버튼 위치")]
    public float collapsedButtonX = 482f;
    public float expandedButtonX = 1400f;

    [Header("연동")]
    public BE2_HideBlocksSelection hideBlocksSelection;

    private RectTransform buttonRectTransform;

    void Start()
    {
        buttonRectTransform = GetComponent<RectTransform>();

        if (hideBlocksSelection == null)
            hideBlocksSelection = FindObjectOfType<BE2_HideBlocksSelection>();

        UpdateUIState();
    }

    private bool IsBlocksSelectionHidden()
    {
        if (hideBlocksSelection == null)
            hideBlocksSelection = FindObjectOfType<BE2_HideBlocksSelection>();

        if (hideBlocksSelection == null || hideBlocksSelection._blocksSelectionCanvas == null)
            return false;

        return !hideBlocksSelection._blocksSelectionCanvas.gameObject.activeSelf;
    }

    public void ToggleUI()
    {
        isExpanded = !isExpanded;
        UpdateUIState();
    }

    private void UpdateUIState()
    {
        if (arrowImage != null)
        {
            float rotationY = isExpanded ? 0f : 180f;
            arrowImage.localRotation = Quaternion.Euler(0f, rotationY, 0f);
        }

        if (buttonRectTransform != null)
        {
            Vector2 anchoredPos = buttonRectTransform.anchoredPosition;
            anchoredPos.x = isExpanded ? collapsedButtonX : expandedButtonX;
            buttonRectTransform.anchoredPosition = anchoredPos;
        }

        if (targetCanvas != null)
        {
            for (int i = 0; i < targetCanvas.Length; i++)
            {
                if (targetCanvas[i] != null)
                {
                    targetCanvas[i].SetActive(isExpanded);
                }
            }
        }

        if (targetPanel != null)
        {
            bool blocksSelectionHidden = IsBlocksSelectionHidden();
            if (blocksSelectionHidden)
            {
                targetPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, hiddenWidth);

                // 앵커 설정에 따라 SetSizeWithCurrentAnchors 반영이 약할 수 있어 sizeDelta를 함께 보정
                Vector2 hiddenSizeDelta = targetPanel.sizeDelta;
                hiddenSizeDelta.x = targetCanvas[0].gameObject.activeSelf ? hiddenWidth2 : hiddenWidth3;
                targetPanel.sizeDelta = hiddenSizeDelta;

                Vector2 offsetMin = targetPanel.offsetMin;
                offsetMin.y = hiddenBottom;
                targetPanel.offsetMin = offsetMin;

                Vector2 offsetMax = targetPanel.offsetMax;
                offsetMax.y = -hiddenTop;
                targetPanel.offsetMax = offsetMax;
            }
            else
            {
                Vector2 sizeDelta = targetPanel.sizeDelta;
                sizeDelta.x = isExpanded ? collapsedWidth : expandedWidth;
                targetPanel.sizeDelta = sizeDelta;
            }
        }
    }

}
