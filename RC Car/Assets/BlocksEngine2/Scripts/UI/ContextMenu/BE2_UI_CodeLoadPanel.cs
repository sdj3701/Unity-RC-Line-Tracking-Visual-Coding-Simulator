using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

namespace MG_BlocksEngine2.UI
{
    /// <summary>
    /// 코드 불러오기 패널 UI
    /// 파일 목록 표시 + 불러오기/삭제/취소 버튼 + 삭제 확인
    /// 다중 선택 지원
    /// </summary>
    public class BE2_UI_CodeLoadPanel : MonoBehaviour
    {
        [Header("Main Panel")]
        [SerializeField] private GameObject mainPanel;
        
        [Header("File List")]
        [SerializeField] private Transform fileListContent;
        [SerializeField] private Toggle fileItemTemplate;
        // ToggleGroup 제거 - 다중 선택 지원을 위해 사용 안 함
        
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
        private List<GameObject> _fileItemObjects = new List<GameObject>();
        private Dictionary<string, Toggle> _fileToggles = new Dictionary<string, Toggle>();
        
        // 선택된 파일들 (다중 선택 지원)
        private HashSet<string> _selectedFileNames = new HashSet<string>();
        
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
            
            _selectedFileNames.Clear();
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
            
            _selectedFileNames.Clear();
            
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
            _selectedFileNames.Clear();
            
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
                _selectedFileNames.Add(_lastLoadedFileName);
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
                // ToggleGroup 없이 다중 선택 가능
                toggle.group = null;
                
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
                
                // 선택 이벤트 - 다중 선택 지원
                string capturedFileName = fileName;
                toggle.onValueChanged.AddListener((isOn) =>
                {
                    if (isOn)
                    {
                        _selectedFileNames.Add(capturedFileName);
                        Debug.Log($"[CodeLoadPanel] Added to selection: {capturedFileName} (Total: {_selectedFileNames.Count})");
                    }
                    else
                    {
                        _selectedFileNames.Remove(capturedFileName);
                        Debug.Log($"[CodeLoadPanel] Removed from selection: {capturedFileName} (Total: {_selectedFileNames.Count})");
                    }
                    UpdateButtonStates();
                });
                
                // 초기에는 선택되지 않은 상태로 설정
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
            bool hasSelection = _selectedFileNames.Count > 0;
            
            if (loadButton != null)
                loadButton.interactable = hasSelection;
            
            if (deleteButton != null)
                deleteButton.interactable = hasSelection;
        }
        
        /// <summary>
        /// 선택된 파일 목록 가져오기
        /// </summary>
        public List<string> GetSelectedFiles()
        {
            return _selectedFileNames.ToList();
        }
        
        /// <summary>
        /// 불러오기 버튼 클릭
        /// 다중 파일 로드 준비됨 (현재는 디버그 출력만)
        /// </summary>
        private void OnLoadClicked()
        {
            if (_selectedFileNames.Count == 0) return;
            
            List<string> selectedFiles = GetSelectedFiles();
            
            // 다중 파일 로드 (현재는 디버그만 출력)
            if (selectedFiles.Count > 1)
            {
                Debug.Log($"[CodeLoadPanel] 다중 파일 로드 요청됨 - {selectedFiles.Count}개 파일:");
                foreach (var file in selectedFiles)
                {
                    Debug.Log($"  - {file}");
                }
                // TODO: 다중 파일 로드 기능 구현 시 여기에 추가
                Debug.Log("[CodeLoadPanel] 다중 파일 로드 기능은 아직 구현되지 않았습니다.");
                return;
            }
            
            // 단일 파일 로드
            string fileToLoad = selectedFiles[0];
            if (_contextMenuManager != null)
            {
                bool success = _contextMenuManager.LoadCodeFromFile(fileToLoad);
                if (success)
                {
                    // 마지막 로드 파일명 저장
                    _lastLoadedFileName = fileToLoad;
                    Debug.Log($"[CodeLoadPanel] 로드 완료: {fileToLoad}");
                    Close();
                }
                else
                {
                    Debug.LogError($"[CodeLoadPanel] 로드 실패: {fileToLoad}");
                }
            }
        }
        
        /// <summary>
        /// 삭제 버튼 클릭
        /// </summary>
        private void OnDeleteClicked()
        {
            if (_selectedFileNames.Count == 0) return;
            
            // 삭제 확인 패널 표시
            if (deleteConfirmPanel != null)
                deleteConfirmPanel.SetActive(true);
        }
        
        /// <summary>
        /// 삭제 확인 - 다중 파일 삭제 지원
        /// </summary>
        private void OnDeleteConfirmed()
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
                bool success = _contextMenuManager.DeleteSaveFile(fileName);
                if (success)
                {
                    successCount++;
                    Debug.Log($"[CodeLoadPanel] 삭제 완료: {fileName}");
                    
                    // 마지막 로드 파일이 삭제되면 기억 초기화
                    if (_lastLoadedFileName == fileName)
                    {
                        _lastLoadedFileName = null;
                    }
                }
                else
                {
                    failCount++;
                    Debug.LogError($"[CodeLoadPanel] 삭제 실패: {fileName}");
                }
            }
            
            Debug.Log($"[CodeLoadPanel] 삭제 결과: 성공 {successCount}개, 실패 {failCount}개");
            
            _selectedFileNames.Clear();
            RefreshFileList();
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
