using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MG_BlocksEngine2.UI
{
    /// <summary>
    /// 저장 패널 UI: 파일명 입력, 저장/취소, 덮어쓰기 확인 기능을 제공합니다.
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

        private void Start()
        {
            _contextMenuManager = BE2_UI_ContextMenuManager.instance;

            if (saveButton != null)
                saveButton.onClick.AddListener(OnSaveClicked);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(Close);

            if (overwriteConfirmButton != null)
                overwriteConfirmButton.onClick.AddListener(OnOverwriteConfirmed);

            if (overwriteCancelButton != null)
                overwriteCancelButton.onClick.AddListener(CloseOverwriteConfirm);

            if (mainPanel != null)
                mainPanel.SetActive(false);

            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);
        }

        public void Open()
        {
            if (mainPanel != null)
                mainPanel.SetActive(true);

            if (fileNameInput != null)
            {
                fileNameInput.text = "";
                fileNameInput.Select();
                fileNameInput.ActivateInputField();
            }

            Debug.Log("[CodeSavePanel] Opened");
        }

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
        /// 저장 버튼 처리기(비동기): 덮어쓰기 대상 여부를 확인한 뒤 저장을 시작합니다.
        /// </summary>
        private async void OnSaveClicked()
        {
            if (fileNameInput == null) return;

            string fileName = fileNameInput.text.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                Debug.LogWarning("[CodeSavePanel] Please enter a file name.");
                return;
            }

            if (_contextMenuManager != null && await _contextMenuManager.FileExistsAsync(fileName))
            {
                _pendingFileName = fileName;
                if (overwriteConfirmPanel != null)
                    overwriteConfirmPanel.SetActive(true);
            }
            else
            {
                await SaveFileAsync(fileName);
            }
        }

        /// <summary>
        /// 덮어쓰기 확인 처리기(비동기): 대기 중인 파일을 저장합니다.
        /// </summary>
        private async void OnOverwriteConfirmed()
        {
            if (!string.IsNullOrEmpty(_pendingFileName))
            {
                await SaveFileAsync(_pendingFileName);
            }

            CloseOverwriteConfirm();
        }

        private void CloseOverwriteConfirm()
        {
            if (overwriteConfirmPanel != null)
                overwriteConfirmPanel.SetActive(false);

            _pendingFileName = null;
        }

        /// <summary>
        /// ContextMenuManager로 실제 비동기 저장 호출을 수행합니다.
        /// </summary>
        private async Task SaveFileAsync(string fileName)
        {
            if (_contextMenuManager == null)
                return;

            bool success = await _contextMenuManager.SaveCodeWithNameAsync(fileName);
            if (success)
            {
                Debug.Log($"[CodeSavePanel] Save completed: {fileName}");
                Close();
            }
            else
            {
                Debug.LogError($"[CodeSavePanel] Save failed: {fileName}");
            }
        }
    }
}

