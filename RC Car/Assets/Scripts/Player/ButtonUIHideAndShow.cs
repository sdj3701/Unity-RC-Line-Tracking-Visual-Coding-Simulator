using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ButtonUIHideAndShow : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("확장/축소할 UI 패널")]
    public RectTransform targetPanel;
    
    [Tooltip("회전할 화살표 이미지 (RectTransform)")]
    public RectTransform arrowImage;
    
    [Tooltip("활성화/비활성화할 Canvas")]
    public GameObject targetCanvas;
    public GameObject RenderTextureCanvas;
    public GameObject RenderTextureCanvas2;
    
    [Header("Settings")]
    [Tooltip("UI가 현재 확장되어 있는지 여부")]
    public bool isExpanded = true;
    
    [Header("Panel Width")]
    [Tooltip("축소 상태일 때 너비")]
    public float collapsedWidth = 837.7477f;
    
    [Tooltip("확장 상태일 때 너비")]
    public float expandedWidth = 1850f;
    
    [Header("Button Position")]
    [Tooltip("축소 상태일 때 버튼 X 위치")]
    public float collapsedButtonX = 760f;
    
    [Tooltip("확장 상태일 때 버튼 X 위치")]
    public float expandedButtonX = 1770f;
    
    private RectTransform buttonRectTransform;
    
    void Start()
    {       
        // 버튼의 RectTransform 가져오기
        buttonRectTransform = GetComponent<RectTransform>();
        
        // 초기 상태 설정
        UpdateUIState();
    }
    
    /// <summary>
    /// UI 확장/축소 토글 및 이미지 Y축 180도 회전
    /// </summary>
    public void ToggleUI()
    {
        isExpanded = !isExpanded;
        UpdateUIState();
    }
    
    /// <summary>
    /// 현재 상태에 따라 UI 업데이트
    /// </summary>
    private void UpdateUIState()
    {
        // 타겟 패널 너비 변경
        if (targetPanel != null)
        {
            Vector2 sizeDelta = targetPanel.sizeDelta;
            sizeDelta.x = isExpanded ? collapsedWidth : expandedWidth;
            targetPanel.sizeDelta = sizeDelta;
        }
        
        // 화살표 이미지 Y축 180도 회전
        if (arrowImage != null)
        {
            // isExpanded가 true면 0도, false면 180도
            float rotationY = isExpanded ? 180f : 0f;
            arrowImage.localRotation = Quaternion.Euler(0f, rotationY, 0f);
        }
        
        // 버튼 위치 변경
        if (buttonRectTransform != null)
        {
            Vector2 anchoredPos = buttonRectTransform.anchoredPosition;
            anchoredPos.x = isExpanded ? collapsedButtonX : expandedButtonX;
            buttonRectTransform.anchoredPosition = anchoredPos;
        }
        
        // Canvas 활성화/비활성화
        if (targetCanvas != null)
        {
            targetCanvas.SetActive(isExpanded);
            RenderTextureCanvas.SetActive(isExpanded);
            RenderTextureCanvas2.SetActive(isExpanded);
        }
    }

}
