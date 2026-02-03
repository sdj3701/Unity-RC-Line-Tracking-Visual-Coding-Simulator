using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MG_BlocksEngine2.UI
{
    /// <summary>
    /// 코드 불러오기 패널 UI
    /// 파일 목록 표시 + 불러오기/삭제/취소 버튼 + 삭제 확인
    /// </summary>
    public class BE2_UI_CodeLoadPanel : MonoBehaviour
    {
        [Header("Main Panel")]
        [SerializeField] private GameObject mainPanel;
        
        [Header("File List")]
        [SerializeField] private Transform fileListContent;
        [SerializeField] private Toggle fileItemTemplate;
        [SerializeField] private ToggleGroup toggleGroup;
        
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
        private string _selectedFileName;
        private List<GameObject> _fileItemObjects = new List<GameObject>();
        private Dictionary<string, Toggle> _fileToggles = new Dictionary<string, Toggle>();
        
        // 마지막으로 로드한 파일명 기억 (static으로 앱 실행 중 유지)
        private static string _lastLoadedFileName;
        
        void Start()
        {
            _contextMenuManager = BE2_UI_ContextMenuManager.instance;
            
            // 버튼 이벤트 연결
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
            
            // 템플릿 비활성화
            if (fileItemTemplate != null)
                fileItemTemplate.gameObject.SetActive(false);
            
            // 초기 상태
            if (mainPanel != null)
                mainPanel.SetActive(false);
            
            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(false);
            
            UpdateButtonStates();
        }
        
        /// <summary>
        /// 불러오기 패널 열기
        /// </summary>
        public void Open()
        {
            if (mainPanel != null)
                mainPanel.SetActive(true);
            
            _selectedFileName = null;
            RefreshFileList();
            UpdateButtonStates();
            
            Debug.Log("[CodeLoadPanel] Opened");
        }
        
        /// <summary>
        /// 패널 닫기
        /// </summary>
        public void Close()
        {
            if (mainPanel != null)
                mainPanel.SetActive(false);
            
            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(false);
            
            _selectedFileName = null;
            
            Debug.Log("[CodeLoadPanel] Closed");
        }
        
        /// <summary>
        /// 파일 목록 새로고침
        /// </summary>
        private void RefreshFileList()
        {
            // 기존 아이템 삭제
            foreach (var obj in _fileItemObjects)
            {
                if (obj != null)
                    Destroy(obj);
            }
            _fileItemObjects.Clear();
            _fileToggles.Clear();
            _selectedFileName = null;
            
            // 파일 목록 가져오기
            List<string> files = _contextMenuManager?.GetSavedFileList() ?? new List<string>();
            
            // 빈 상태 표시
            if (emptyStateText != null)
                emptyStateText.SetActive(files.Count == 0);
            
            // 파일 아이템 생성
            foreach (var fileName in files)
            {
                CreateFileItem(fileName);
            }
            
            // 마지막 로드한 파일이 있으면 선택
            if (!string.IsNullOrEmpty(_lastLoadedFileName) && _fileToggles.ContainsKey(_lastLoadedFileName))
            {
                _fileToggles[_lastLoadedFileName].isOn = true;
                _selectedFileName = _lastLoadedFileName;
                Debug.Log($"[CodeLoadPanel] Auto-selected last loaded file: {_lastLoadedFileName}");
            }
            
            UpdateButtonStates();
            
            Debug.Log($"[CodeLoadPanel] Refreshed - {files.Count} files found");
        }
        
        /// <summary>
        /// 파일 아이템 생성
        /// </summary>
        private void CreateFileItem(string fileName)
        {
            if (fileItemTemplate == null || fileListContent == null) return;
            
            GameObject itemObj = Instantiate(fileItemTemplate.gameObject, fileListContent);
            itemObj.SetActive(true);
            _fileItemObjects.Add(itemObj);
            
            Toggle toggle = itemObj.GetComponent<Toggle>();
            if (toggle != null)
            {
                // ToggleGroup 설정
                if (toggleGroup != null)
                {
                    toggle.group = toggleGroup;
                    toggleGroup.allowSwitchOff = true;  // 아무것도 선택 안 한 상태 허용
                }
                
                // 텍스트 설정
                TMP_Text label = toggle.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = fileName;
                else
                {
                    Text legacyLabel = toggle.GetComponentInChildren<Text>();
                    if (legacyLabel != null)
                        legacyLabel.text = fileName;
                }
                
                // 선택 이벤트
                string capturedFileName = fileName;
                toggle.onValueChanged.AddListener((isOn) =>
                {
                    if (isOn)
                    {
                        _selectedFileName = capturedFileName;
                        UpdateButtonStates();
                        Debug.Log($"[CodeLoadPanel] Selected: {capturedFileName}");
                    }
                });
                
                // 초기에는 선택되지 않은 상태로 설정 (나중에 RefreshFileList에서 선택)
                toggle.isOn = false;
                
                // Dictionary에 저장
                _fileToggles[fileName] = toggle;
            }
        }
        
        /// <summary>
        /// 버튼 상태 업데이트
        /// </summary>
        private void UpdateButtonStates()
        {
            bool hasSelection = !string.IsNullOrEmpty(_selectedFileName);
            
            if (loadButton != null)
                loadButton.interactable = hasSelection;
            
            if (deleteButton != null)
                deleteButton.interactable = hasSelection;
        }
        
        /// <summary>
        /// 불러오기 버튼 클릭
        /// </summary>
        private void OnLoadClicked()
        {
            if (string.IsNullOrEmpty(_selectedFileName)) return;
            
            if (_contextMenuManager != null)
            {
                bool success = _contextMenuManager.LoadCodeFromFile(_selectedFileName);
                if (success)
                {
                    // 마지막 로드 파일명 저장
                    _lastLoadedFileName = _selectedFileName;
                    Debug.Log($"[CodeLoadPanel] 로드 완료: {_selectedFileName}");
                    Close();
                }
                else
                {
                    Debug.LogError($"[CodeLoadPanel] 로드 실패: {_selectedFileName}");
                }
            }
        }
        
        /// <summary>
        /// 삭제 버튼 클릭
        /// </summary>
        private void OnDeleteClicked()
        {
            if (string.IsNullOrEmpty(_selectedFileName)) return;
            
            // 삭제 확인 패널 표시
            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(true);
        }
        
        /// <summary>
        /// 삭제 확인
        /// </summary>
        private void OnDeleteConfirmed()
        {
            if (!string.IsNullOrEmpty(_selectedFileName) && _contextMenuManager != null)
            {
                bool success = _contextMenuManager.DeleteSaveFile(_selectedFileName);
                if (success)
                {
                    Debug.Log($"[CodeLoadPanel] 삭제 완료: {_selectedFileName}");
                    _selectedFileName = null;
                    RefreshFileList();
                }
                else
                {
                    Debug.LogError($"[CodeLoadPanel] 삭제 실패: {_selectedFileName}");
                }
            }
            
            CloseDeleteConfirm();
        }
        
        /// <summary>
        /// 삭제 취소
        /// </summary>
        private void CloseDeleteConfirm()
        {
            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(false);
        }
    }
}
