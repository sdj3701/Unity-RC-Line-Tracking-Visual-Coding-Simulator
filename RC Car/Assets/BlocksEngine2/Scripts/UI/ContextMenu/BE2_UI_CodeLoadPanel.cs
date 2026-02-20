using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MG_BlocksEngine2.Storage;

namespace MG_BlocksEngine2.UI
{
    /// <summary>
    /// 불러오기 패널 UI: 파일 목록, 불러오기/삭제 동작, 삭제 확인 기능을 제공합니다.
    /// </summary>
    public class BE2_UI_CodeLoadPanel : MonoBehaviour
    {
        private static void LogDbInfo(string message)
        {
            Debug.Log($"<color=cyan>{message}</color>");
        }

        private static bool IsDatabaseStorageActive()
        {
            var manager = BE2_CodeStorageManager.Instance;
            return manager != null && manager.GetStorageProvider() is DatabaseStorageProvider;
        }

        private static void LogInfoByStorageMode(string message)
        {
            if (IsDatabaseStorageActive())
            {
                LogDbInfo(message);
                return;
            }

            Debug.Log(message);
        }

        [Header("Main Panel")]
        [SerializeField] private GameObject mainPanel;

        [Header("File List")]
        [SerializeField] private Transform fileListContent;
        [SerializeField] private Toggle fileItemTemplate;

        [Header("Buttons")]
        [SerializeField] private Button loadButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button cancelButton;

        [Header("Delete Confirmation")]
        [SerializeField] private GameObject deleteConfirmPanel;
        [SerializeField] private Button deleteConfirmButton;
        [SerializeField] private Button deleteCancelButton;

        [Header("Empty State")]
        [SerializeField] private GameObject emptyStateText;

        private BE2_UI_ContextMenuManager _contextMenuManager;
        private readonly List<GameObject> _fileItemObjects = new List<GameObject>();
        private readonly Dictionary<string, Toggle> _fileToggles = new Dictionary<string, Toggle>();
        private readonly HashSet<string> _selectedFileNames = new HashSet<string>();

        // 패널을 다시 열 때 마지막으로 불러온 파일이 선택되도록 유지합니다.
        private static string _lastLoadedFileName;

        private void Start()
        {
            _contextMenuManager = BE2_UI_ContextMenuManager.instance;

            if (loadButton != null)
                loadButton.onClick.AddListener(OnLoadClicked);

            if (deleteButton != null)
                deleteButton.onClick.AddListener(OnDeleteClicked);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(Close);

            if (deleteConfirmButton != null)
                deleteConfirmButton.onClick.AddListener(OnDeleteConfirmed);

            if (deleteCancelButton != null)
                deleteCancelButton.onClick.AddListener(CloseDeleteConfirm);

            if (fileItemTemplate != null)
                fileItemTemplate.gameObject.SetActive(false);

            if (mainPanel != null)
                mainPanel.SetActive(false);

            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(false);

            UpdateButtonStates();
        }

        /// <summary>
        /// 패널을 열고 파일 목록을 비동기로 가져옵니다.
        /// </summary>
        public async void Open()
        {
            if (mainPanel != null)
                mainPanel.SetActive(true);

            _selectedFileNames.Clear();
            await RefreshFileListAsync();
            UpdateButtonStates();

            Debug.Log("[CodeLoadPanel] Opened");
        }

        public void Close()
        {
            if (mainPanel != null)
                mainPanel.SetActive(false);

            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(false);

            _selectedFileNames.Clear();
            Debug.Log("[CodeLoadPanel] Closed");
        }

        /// <summary>
        /// 저장소 제공자에서 파일 목록을 새로고침합니다(원격 제공자는 GET 사용).
        /// </summary>
        private async Task RefreshFileListAsync()
        {
            foreach (var obj in _fileItemObjects)
            {
                if (obj != null)
                    Destroy(obj);
            }

            _fileItemObjects.Clear();
            _fileToggles.Clear();
            _selectedFileNames.Clear();

            List<string> files = _contextMenuManager != null
                ? await _contextMenuManager.GetSavedFileListAsync()
                : new List<string>();

            if (emptyStateText != null)
                emptyStateText.SetActive(files.Count == 0);

            foreach (var fileName in files)
            {
                CreateFileItem(fileName);
            }

            if (!string.IsNullOrEmpty(_lastLoadedFileName) && _fileToggles.ContainsKey(_lastLoadedFileName))
            {
                _fileToggles[_lastLoadedFileName].isOn = true;
                _selectedFileNames.Add(_lastLoadedFileName);
            }

            UpdateButtonStates();
            LogInfoByStorageMode($"[CodeLoadPanel] Refreshed - {files.Count} files found");
        }

        private void CreateFileItem(string fileName)
        {
            if (fileItemTemplate == null || fileListContent == null) return;

            GameObject itemObj = Instantiate(fileItemTemplate.gameObject, fileListContent);
            itemObj.SetActive(true);
            _fileItemObjects.Add(itemObj);

            Toggle toggle = itemObj.GetComponent<Toggle>();
            if (toggle == null) return;

            toggle.group = null;

            TMP_Text label = toggle.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = fileName;
            }
            else
            {
                Text legacyLabel = toggle.GetComponentInChildren<Text>();
                if (legacyLabel != null)
                    legacyLabel.text = fileName;
            }

            string capturedFileName = fileName;
            toggle.onValueChanged.AddListener((isOn) =>
            {
                if (isOn)
                {
                    _selectedFileNames.Add(capturedFileName);
                }
                else
                {
                    _selectedFileNames.Remove(capturedFileName);
                }

                UpdateButtonStates();
            });

            toggle.isOn = false;
            _fileToggles[fileName] = toggle;
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = _selectedFileNames.Count > 0;

            if (loadButton != null)
                loadButton.interactable = hasSelection;

            if (deleteButton != null)
                deleteButton.interactable = hasSelection;
        }

        public List<string> GetSelectedFiles()
        {
            return _selectedFileNames.ToList();
        }

        /// <summary>
        /// 불러오기 처리기(비동기): 현재는 단일 파일 불러오기만 지원합니다.
        /// </summary>
        private async void OnLoadClicked()
        {
            if (_selectedFileNames.Count == 0) return;

            List<string> selectedFiles = GetSelectedFiles();
            if (selectedFiles.Count > 1)
            {
                Debug.Log($"[CodeLoadPanel] Multi-select load is not implemented yet. Count={selectedFiles.Count}");
                return;
            }

            string fileToLoad = selectedFiles[0];
            if (_contextMenuManager == null) return;

            bool success = await _contextMenuManager.LoadCodeFromFileAsync(fileToLoad);
            if (success)
            {
                _lastLoadedFileName = fileToLoad;
                LogInfoByStorageMode($"[CodeLoadPanel] Load completed: {fileToLoad}");
                Close();
            }
            else
            {
                Debug.LogError($"[CodeLoadPanel] Load failed: {fileToLoad}");
            }
        }

        private void OnDeleteClicked()
        {
            if (_selectedFileNames.Count == 0) return;

            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(true);
        }

        /// <summary>
        /// 삭제 처리기(비동기): 다중 삭제를 지원합니다.
        /// </summary>
        private async void OnDeleteConfirmed()
        {
            if (_selectedFileNames.Count == 0 || _contextMenuManager == null)
            {
                CloseDeleteConfirm();
                return;
            }

            List<string> filesToDelete = GetSelectedFiles();
            int successCount = 0;
            int failCount = 0;

            foreach (var fileName in filesToDelete)
            {
                bool success = await _contextMenuManager.DeleteSaveFileAsync(fileName);
                if (success)
                {
                    successCount++;
                    if (_lastLoadedFileName == fileName)
                    {
                        _lastLoadedFileName = null;
                    }
                }
                else
                {
                    failCount++;
                }
            }

            Debug.Log($"[CodeLoadPanel] Delete result: success={successCount}, fail={failCount}");

            _selectedFileNames.Clear();
            await RefreshFileListAsync();
            CloseDeleteConfirm();
        }

        private void CloseDeleteConfirm()
        {
            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(false);
        }
    }
}

