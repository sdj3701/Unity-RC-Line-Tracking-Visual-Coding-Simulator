using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MG_BlocksEngine2.UI
{
    /// <summary>
    /// 코드 저장 패널 UI
    /// 파일명 입력 + 저장/취소 버튼 + 덮어쓰기 확인
    /// </summary>
    public class BE2_UI_CodeSavePanel : MonoBehaviour
    {
        [Header("Main Panel")]
        [SerializeField] private GameObject mainPanel;
        
        [Header("Input")]
        [SerializeField] private TMP_InputField fileNameInput;
        
        [Header("Buttons")]
        [SerializeField] private Button saveButton;
        [SerializeField] private Button cancelButton;
        
        [Header("Overwrite Confirmation")]
        [SerializeField] private GameObject overwriteConfirmPanel;
        [SerializeField] private Button overwriteConfirmButton;
        [SerializeField] private Button overwriteCancelButton;
        
        private BE2_UI_ContextMenuManager _contextMenuManager;
        private string _pendingFileName;
        
        void Start()
        {
            _contextMenuManager = BE2_UI_ContextMenuManager.instance;
            
            // 버튼 이벤트 연결
            if (saveButton != null)
                saveButton.onClick.AddListener(OnSaveClicked);
            
            if (cancelButton != null)
                cancelButton.onClick.AddListener(Close);
            
            if (overwriteConfirmButton != null)
                overwriteConfirmButton.onClick.AddListener(OnOverwriteConfirmed);
            
            if (overwriteCancelButton != null)
                overwriteCancelButton.onClick.AddListener(CloseOverwriteConfirm);
            
            // 초기 상태
            if (mainPanel != null)
                mainPanel.SetActive(false);
            
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
        }
        
        /// <summary>
        /// 저장 패널 열기
        /// </summary>
        public void Open()
        {
            if (mainPanel != null)
                mainPanel.SetActive(true);
            
            // 입력 필드 초기화
            if (fileNameInput != null)
            {
                fileNameInput.text = "";
                fileNameInput.Select();
                fileNameInput.ActivateInputField();
            }
            
            Debug.Log("[CodeSavePanel] Opened");
        }
        
        /// <summary>
        /// 저장 패널 닫기
        /// </summary>
        public void Close()
        {
            if (mainPanel != null)
                mainPanel.SetActive(false);
            
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
            
            _pendingFileName = null;
            
            Debug.Log("[CodeSavePanel] Closed");
        }
        
        /// <summary>
        /// 저장 버튼 클릭
        /// </summary>
        private void OnSaveClicked()
        {
            if (fileNameInput == null) return;
            
            string fileName = fileNameInput.text.Trim();
            
            if (string.IsNullOrEmpty(fileName))
            {
                Debug.LogWarning("[CodeSavePanel] 파일명을 입력하세요.");
                return;
            }
            
            // 파일 존재 여부 확인
            if (_contextMenuManager != null && _contextMenuManager.FileExists(fileName))
            {
                // 덮어쓰기 확인 패널 표시
                _pendingFileName = fileName;
                if (overwriteConfirmPanel != null)
                    overwriteConfirmPanel.SetActive(true);
            }
            else
            {
                // 바로 저장
                SaveFile(fileName);
            }
        }
        
        /// <summary>
        /// 덮어쓰기 확인
        /// </summary>
        private void OnOverwriteConfirmed()
        {
            if (!string.IsNullOrEmpty(_pendingFileName))
            {
                SaveFile(_pendingFileName);
            }
            CloseOverwriteConfirm();
        }
        
        /// <summary>
        /// 덮어쓰기 취소
        /// </summary>
        private void CloseOverwriteConfirm()
        {
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
            
            _pendingFileName = null;
        }
        
        /// <summary>
        /// 파일 저장 실행
        /// </summary>
        private void SaveFile(string fileName)
        {
            if (_contextMenuManager != null)
            {
                bool success = _contextMenuManager.SaveCodeWithName(fileName);
                if (success)
                {
                    Debug.Log($"[CodeSavePanel] 저장 완료: {fileName}");
                    Close();
                }
                else
                {
                    Debug.LogError($"[CodeSavePanel] 저장 실패: {fileName}");
                }
            }
        }
    }
}
