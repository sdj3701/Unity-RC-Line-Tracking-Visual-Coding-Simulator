using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MG_BlocksEngine2.Storage;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientBlockShareListPanel : MonoBehaviour
{
    [Serializable]
    private struct LocalCodeEntry
    {
        public string FileName;
        public int UserLevelSeq;
        public bool HasServerSeq;
    }

    [Header("Main Panel")]
    [SerializeField] private GameObject _mainPanel;

    [Header("List")]
    [SerializeField] private Transform _listContent;
    [SerializeField] private Toggle _itemTemplate;
    [SerializeField] private GameObject _emptyStateObject;

    [Header("Buttons")]
    [SerializeField] private Button _openButton;
    [SerializeField] private Button _loadButton;
    [SerializeField] private Button _cancelButton;
    [SerializeField] private Button _refreshButton;

    [Header("Text")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private TMP_Text _selectedText;

    [Header("Upload Target")]
    [SerializeField] private ClientBlockShareUploadButton _uploadButtonTarget;
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private bool _autoRoomFromSession = true;
    [SerializeField] private int _fallbackUserLevelSeq = 1;
    [SerializeField] private bool _allowFallbackSeqUpload = false;
    [SerializeField] private bool _closeOnLoad = true;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private readonly List<GameObject> _itemObjects = new List<GameObject>();
    private readonly List<Toggle> _itemToggles = new List<Toggle>();
    private readonly List<LocalCodeEntry> _entries = new List<LocalCodeEntry>();

    private int _selectedIndex = -1;
    private bool _isRefreshing;

    public string SelectedBlockShareId { get; private set; }

    private void Start()
    {
        if (_itemTemplate != null)
            _itemTemplate.gameObject.SetActive(false);

        if (_mainPanel != null)
            _mainPanel.SetActive(false);

        BindButtons();
        UpdateButtons();
        UpdateSelectedText();
    }

    private void OnEnable()
    {
        BindButtons();
    }

    private void OnDisable()
    {
        UnbindButtons();
    }

    public async void OpenGui()
    {
        await OpenAsync(fetchList: true);
    }

    public void CloseGui()
    {
        SetPanelVisible(false);
    }

    public void ToggleGuiVisibility()
    {
        if (IsPanelVisible())
            CloseGui();
        else
            OpenGui();
    }

    public async void OpenAndFetch()
    {
        await OpenAsync(fetchList: true);
    }

    public async void FetchBlockShareListNow()
    {
        await RefreshDbListAsync();
    }

    public void SendSelectedToHost()
    {
        OnLoadClicked();
    }

    private async Task OpenAsync(bool fetchList)
    {
        SetPanelVisible(true);

        if (fetchList)
            await RefreshDbListAsync();
    }

    private async Task RefreshDbListAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        UpdateButtons();
        SetStatus("Loading local code list...");

        try
        {
            BE2_CodeStorageManager storageManager = BE2_CodeStorageManager.Instance;
            if (storageManager == null)
            {
                RebuildList(new List<string>());
                SetStatus("CodeStorageManager is null.");
                return;
            }

            List<BE2_CodeStorageFileEntry> fileEntries = await storageManager.GetFileEntriesAsync();
            if (fileEntries != null && fileEntries.Count > 0)
            {
                RebuildList(fileEntries);
            }
            else
            {
                List<string> fileNames = await storageManager.GetFileListAsync();
                RebuildList(fileNames ?? new List<string>());
            }

            SetStatus($"Loaded {_entries.Count} local code item(s).");
        }
        catch (Exception e)
        {
            RebuildList(new List<string>());
            SetStatus($"Failed to load local code list. ({e.Message})");
        }
        finally
        {
            _isRefreshing = false;
            UpdateButtons();
        }
    }

    private void RebuildList(List<string> files)
    {
        ClearListObjects();
        _entries.Clear();
        _selectedIndex = -1;
        SelectedBlockShareId = null;

        for (int i = 0; i < files.Count; i++)
        {
            string fileName = string.IsNullOrWhiteSpace(files[i]) ? string.Empty : files[i].Trim();
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            _entries.Add(new LocalCodeEntry
            {
                FileName = fileName,
                UserLevelSeq = ResolveUserLevelSeq(fileName),
                HasServerSeq = false
            });
        }

        for (int i = 0; i < _entries.Count; i++)
            CreateListItem(i, _entries[i]);

        if (_entries.Count > 0)
            SelectIndex(0, syncToggle: true);
        else
            UpdateSelectedText();

        if (_emptyStateObject != null)
            _emptyStateObject.SetActive(_entries.Count == 0);

        UpdateButtons();
    }

    private void RebuildList(List<BE2_CodeStorageFileEntry> fileEntries)
    {
        ClearListObjects();
        _entries.Clear();
        _selectedIndex = -1;
        SelectedBlockShareId = null;

        for (int i = 0; i < fileEntries.Count; i++)
        {
            BE2_CodeStorageFileEntry source = fileEntries[i];
            if (source == null || string.IsNullOrWhiteSpace(source.FileName))
                continue;

            int seq = source.UserLevelSeq > 0
                ? source.UserLevelSeq
                : ResolveUserLevelSeq(source.FileName);

            _entries.Add(new LocalCodeEntry
            {
                FileName = source.FileName.Trim(),
                UserLevelSeq = seq,
                HasServerSeq = source.UserLevelSeq > 0
            });
        }

        for (int i = 0; i < _entries.Count; i++)
            CreateListItem(i, _entries[i]);

        if (_entries.Count > 0)
            SelectIndex(0, syncToggle: true);
        else
            UpdateSelectedText();

        if (_emptyStateObject != null)
            _emptyStateObject.SetActive(_entries.Count == 0);

        UpdateButtons();
    }

    private void CreateListItem(int index, LocalCodeEntry entry)
    {
        if (_itemTemplate == null || _listContent == null)
            return;

        GameObject itemObject = Instantiate(_itemTemplate.gameObject, _listContent);
        itemObject.SetActive(true);
        _itemObjects.Add(itemObject);

        Toggle toggle = itemObject.GetComponent<Toggle>();
        if (toggle == null)
            return;

        toggle.group = null;

        TMP_Text text = toggle.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.text = BuildListLabel(entry);
        }
        else
        {
            Text legacyText = toggle.GetComponentInChildren<Text>(true);
            if (legacyText != null)
                legacyText.text = BuildListLabel(entry);
        }

        int capturedIndex = index;
        toggle.onValueChanged.AddListener((isOn) =>
        {
            if (isOn)
            {
                SelectIndex(capturedIndex, syncToggle: false);
                return;
            }

            if (_selectedIndex == capturedIndex)
            {
                _selectedIndex = -1;
                SelectedBlockShareId = null;
                UpdateSelectedText();
                UpdateButtons();
            }
        });

        toggle.isOn = false;
        _itemToggles.Add(toggle);
    }

    private void SelectIndex(int index, bool syncToggle)
    {
        if (index < 0 || index >= _entries.Count)
        {
            _selectedIndex = -1;
            SelectedBlockShareId = null;
            UpdateSelectedText();
            UpdateButtons();
            return;
        }

        _selectedIndex = index;
        SelectedBlockShareId = _entries[index].FileName;

        if (syncToggle)
        {
            for (int i = 0; i < _itemToggles.Count; i++)
            {
                if (_itemToggles[i] != null)
                    _itemToggles[i].isOn = i == _selectedIndex;
            }
        }

        UpdateSelectedText();
        UpdateButtons();
    }

    private void OnLoadClicked()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
        {
            SetStatus("Select one item first.");
            return;
        }

        LocalCodeEntry entry = _entries[_selectedIndex];
        if (entry.UserLevelSeq <= 0)
        {
            SetStatus("Invalid userLevelSeq. Check DB entry.");
            return;
        }

        if (!entry.HasServerSeq && !_allowFallbackSeqUpload)
        {
            SetStatus("This item does not include server userLevelSeq. Enable fallback upload only if you must.");
            return;
        }

        bool photonSent = TrySendSelectedCodeByPhoton(entry, out string photonMessage);
        bool apiRequested = TrySendSelectedCodeByApi(entry, out string apiMessage);

        if (photonSent && apiRequested)
            SetStatus($"Send requested via Photon/API: [{entry.UserLevelSeq}] {entry.FileName}");
        else if (photonSent)
            SetStatus($"Send requested via Photon: [{entry.UserLevelSeq}] {entry.FileName}");
        else if (apiRequested)
            SetStatus($"Send requested via API: [{entry.UserLevelSeq}] {entry.FileName}");
        else
        {
            SetStatus($"Send failed. photon={photonMessage}, api={apiMessage}");
            return;
        }

        if (_closeOnLoad)
            SetPanelVisible(false);
    }

    private bool TrySendSelectedCodeByPhoton(LocalCodeEntry entry, out string message)
    {
        message = string.Empty;

        NetworkRCCar car = FindLocalInputAuthorityCar();
        if (car == null)
        {
            message = "local input-authority NetworkRCCar not found";
            return false;
        }

        if (!car.TrySubmitCodeSelectionToHost(entry.UserLevelSeq, entry.FileName, out string error))
        {
            message = error;
            return false;
        }

        message = "sent";
        return true;
    }

    private bool TrySendSelectedCodeByApi(LocalCodeEntry entry, out string message)
    {
        message = string.Empty;

        if (_uploadButtonTarget == null)
        {
            message = "upload target is not assigned";
            return false;
        }

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            message = "API roomId is empty";
            return false;
        }

        _uploadButtonTarget.ApplyDraft(entry.UserLevelSeq, entry.FileName, roomId);
        _uploadButtonTarget.OnClickUploadBlockShare();
        message = "requested";
        return true;
    }

    private NetworkRCCar FindLocalInputAuthorityCar()
    {
        NetworkRCCar[] cars = FindObjectsOfType<NetworkRCCar>(true);
        if (cars == null || cars.Length == 0)
            return null;

        for (int i = 0; i < cars.Length; i++)
        {
            NetworkRCCar car = cars[i];
            if (car != null && car.CanSubmitCodeSelectionToHost)
                return car;
        }

        return null;
    }

    private void OnCancelClicked()
    {
        SetPanelVisible(false);
    }

    private void OnRefreshClicked()
    {
        _ = RefreshDbListAsync();
    }

    private void BindButtons()
    {
        if (_openButton != null)
        {
            _openButton.onClick.RemoveListener(OpenGui);
            _openButton.onClick.AddListener(OpenGui);
        }

        if (_loadButton != null)
        {
            _loadButton.onClick.RemoveListener(OnLoadClicked);
            _loadButton.onClick.AddListener(OnLoadClicked);
        }

        if (_cancelButton != null)
        {
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        if (_refreshButton != null)
        {
            _refreshButton.onClick.RemoveListener(OnRefreshClicked);
            _refreshButton.onClick.AddListener(OnRefreshClicked);
        }
    }

    private void UnbindButtons()
    {
        if (_openButton != null)
            _openButton.onClick.RemoveListener(OpenGui);

        if (_loadButton != null)
            _loadButton.onClick.RemoveListener(OnLoadClicked);

        if (_cancelButton != null)
            _cancelButton.onClick.RemoveListener(OnCancelClicked);

        if (_refreshButton != null)
            _refreshButton.onClick.RemoveListener(OnRefreshClicked);
    }

    private void ClearListObjects()
    {
        for (int i = 0; i < _itemObjects.Count; i++)
        {
            if (_itemObjects[i] != null)
                Destroy(_itemObjects[i]);
        }

        _itemObjects.Clear();
        _itemToggles.Clear();
    }

    private void UpdateButtons()
    {
        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _entries.Count;
        bool canUploadSelection = hasSelection && IsEntryUploadable(_entries[_selectedIndex]);

        if (_loadButton != null)
            _loadButton.interactable = !_isRefreshing && canUploadSelection;

        if (_refreshButton != null)
            _refreshButton.interactable = !_isRefreshing;
    }

    private void UpdateSelectedText()
    {
        if (_selectedText == null)
            return;

        if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
        {
            _selectedText.text = "Selected: -";
            return;
        }

        LocalCodeEntry entry = _entries[_selectedIndex];
        string seqState = entry.HasServerSeq ? "server-seq" : "fallback-seq";
        string uploadState = IsEntryUploadable(entry) ? "upload-ready" : "db-save-required";
        _selectedText.text = $"Selected: [{entry.UserLevelSeq}] {entry.FileName} ({seqState}, {uploadState})";
    }

    private bool IsEntryUploadable(LocalCodeEntry entry)
    {
        return entry.UserLevelSeq > 0 && (entry.HasServerSeq || _allowFallbackSeqUpload);
    }

    private string ResolveTargetRoomId()
    {
    if (!_autoRoomFromSession && string.IsNullOrWhiteSpace(_roomIdOverride))
        return string.Empty;

    return NetworkRoomIdentity.ResolveApiRoomId(_roomIdOverride);
    }

    private int ResolveUserLevelSeq(string fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            Match match = Regex.Match(fileName, "\\d+");
            if (match.Success && int.TryParse(match.Value, out int parsed) && parsed > 0)
                return parsed;
        }

        return Mathf.Max(1, _fallbackUserLevelSeq);
    }

    private static string BuildListLabel(LocalCodeEntry entry)
    {
        string suffix = entry.HasServerSeq ? string.Empty : " (fallback)";
        return $"[{entry.UserLevelSeq}] {entry.FileName}{suffix}";
    }

    private bool IsPanelVisible()
    {
        return _mainPanel == null || _mainPanel.activeSelf;
    }

    private void SetPanelVisible(bool visible)
    {
        if (_mainPanel != null)
            _mainPanel.SetActive(visible);
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;

        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[ClientBlockShareListPanel] {text}");
    }
}
