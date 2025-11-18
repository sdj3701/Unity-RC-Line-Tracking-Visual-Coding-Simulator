using UnityEngine;
using UnityEngine.UI;

namespace UBlockly.UGUI
{
    // BtnRun 클릭 시 생성된 C# 스크립트를 GUI로 미리보기하는 간단한 뷰
    public class CodePreviewView : MonoBehaviour
    {
        [SerializeField] private GameObject m_Panel;         // 전체 프리뷰 패널
        [SerializeField] private Text m_TitleText;            // 제목 표시용 텍스트
        [SerializeField] private Text m_BodyText;             // 코드 표시용 텍스트(멀티라인)
        [SerializeField] private Button m_CloseButton;        // 닫기 버튼
        [SerializeField] private Button m_CopyButton;         // 복사 버튼(선택)

        private void Awake()
        {
            // 패널이 있다면 시작 시 감춰둡니다.
            if (m_Panel != null)
                m_Panel.SetActive(false);

            if (m_CloseButton != null)
                m_CloseButton.onClick.AddListener(Hide);

            if (m_CopyButton != null)
                m_CopyButton.onClick.AddListener(CopyToClipboard);
        }

        // 코드 미리보기 표시
        public void Show(string code, string title = "C# Preview")
        {
            if (m_TitleText != null)
                m_TitleText.text = title;

            if (m_BodyText != null)
                m_BodyText.text = string.IsNullOrEmpty(code) ? "<empty>" : code;

            if (m_Panel != null)
                m_Panel.SetActive(true);
        }

        // 미리보기 숨김
        public void Hide()
        {
            if (m_Panel != null)
                m_Panel.SetActive(false);
        }

        // 클립보드로 복사 (에디터/런타임 공통)
        private void CopyToClipboard()
        {
            if (m_BodyText == null) return;
#if UNITY_EDITOR
            UnityEditor.EditorGUIUtility.systemCopyBuffer = m_BodyText.text;
#else
            GUIUtility.systemCopyBuffer = m_BodyText.text;
#endif
        }
    }
}
