using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BlockShareRemoteListPanel : MonoBehaviour
{
    [Header("Main Panel")]
    [SerializeField] private GameObject _mainPanel;

    [Header("List UI")]
    [SerializeField] private Transform _listContent;
    [SerializeField] private Toggle _itemTemplate;
    [SerializeField] private GameObject _emptyStateObject;

    [Header("Buttons")]
    [SerializeField] private Button _openButton;
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _refreshNowButton;

    [Header("Text")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private TMP_Text _selectedText;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private readonly List<BlockShareListItemViewModel> _items = new List<BlockShareListItemViewModel>();
    private readonly List<GameObject> _itemObjects = new List<GameObject>();
    private readonly List<Toggle> _itemToggles = new List<Toggle>();

    private int _selectedIndex = -1;
    private bool _isBusy;
    private bool _isSyncingToggle;
    private bool _refreshPendingOnEnable;

    public event Action RefreshRequested;

    public string SelectedShareId { get; private set; }
    public ChatRoomBlockShareInfo SelectedDetailInfo { get; private set; }

    private void Awake()
    {
        if (_mainPanel == null)
            _mainPanel = gameObject;

        if (_itemTemplate != null)
            _itemTemplate.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        BindButtons();
        UpdateButtons();

        if (_refreshPendingOnEnable)
        {
            _refreshPendingOnEnable = false;
            RaiseRefreshRequested();
        }
    }

    private void OnDisable()
    {
        UnbindButtons();
    }

    private void OpenPanel()
    {
        SetPanelVisible(true);

        if (isActiveAndEnabled)
            RaiseRefreshRequested();
        else
            _refreshPendingOnEnable = true;
    }

    private void ClosePanel()
    {
        SetPanelVisible(false);
    }

    public void RequestRefresh()
    {
        RaiseRefreshRequested();
    }

    public void RenderRemoteShares(IReadOnlyList<BlockShareListItemViewModel> items)
    {
        string previousShareId = SelectedShareId;

        ClearListUi();
        _items.Clear();
        _selectedIndex = -1;

        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                BlockShareListItemViewModel item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.ShareId))
                    continue;

                _items.Add(item);
            }
        }

        for (int i = 0; i < _items.Count; i++)
            CreateListItem(i, _items[i]);

        if (_emptyStateObject != null)
            _emptyStateObject.SetActive(_items.Count == 0);

        RestoreSelection(previousShareId);
        UpdateButtons();
    }

    public bool TryGetSelectedListItemInfo(out string message, out int userLevelSeq)
    {
        message = string.Empty;
        userLevelSeq = 0;

        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
            return false;

        BlockShareListItemViewModel selected = _items[_selectedIndex];
        if (selected == null)
            return false;

        message = Normalize(selected.FileName);
        userLevelSeq = selected.UserLevelSeq;
        return true;
    }

    public bool TryGetListItemInfoByShareId(string shareIdRaw, out string message, out int userLevelSeq)
    {
        message = string.Empty;
        userLevelSeq = 0;

        string shareId = Normalize(shareIdRaw);
        if (string.IsNullOrWhiteSpace(shareId))
            return false;

        for (int i = 0; i < _items.Count; i++)
        {
            BlockShareListItemViewModel item = _items[i];
            if (item == null)
                continue;

            if (!string.Equals(shareId, Normalize(item.ShareId), StringComparison.Ordinal))
                continue;

            message = Normalize(item.FileName);
            userLevelSeq = item.UserLevelSeq;
            return true;
        }

        return false;
    }

    public void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UpdateButtons();
    }

    public void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[BlockShareRemoteListPanel] {text}");
    }

    private void RaiseRefreshRequested()
    {
        if (_debugLog)
            Debug.Log("[BlockShareRemoteListPanel] Refresh requested.");

        RefreshRequested?.Invoke();
    }

    private void BindButtons()
    {
        if (_openButton != null && _openButton.onClick.GetPersistentEventCount() == 0)
        {
            _openButton.onClick.RemoveListener(OpenPanel);
            _openButton.onClick.AddListener(OpenPanel);
        }

        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveListener(ClosePanel);
            _closeButton.onClick.AddListener(ClosePanel);
        }

        if (_refreshNowButton != null)
        {
            _refreshNowButton.onClick.RemoveListener(RequestRefresh);
            _refreshNowButton.onClick.AddListener(RequestRefresh);
        }
    }

    private void UnbindButtons()
    {
        if (_openButton != null)
            _openButton.onClick.RemoveListener(OpenPanel);

        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(ClosePanel);

        if (_refreshNowButton != null)
            _refreshNowButton.onClick.RemoveListener(RequestRefresh);
    }

    private void CreateListItem(int index, BlockShareListItemViewModel item)
    {
        if (_itemTemplate == null || _listContent == null || item == null)
            return;

        GameObject itemObject = Instantiate(_itemTemplate.gameObject, _listContent);
        itemObject.SetActive(true);
        _itemObjects.Add(itemObject);

        Toggle toggle = itemObject.GetComponent<Toggle>();
        if (toggle == null)
            return;

        string label = string.IsNullOrWhiteSpace(item.DisplayLabel) ? BuildFallbackLabel(item) : item.DisplayLabel;
        TMP_Text text = toggle.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.text = label;
        }
        else
        {
            Text legacyText = toggle.GetComponentInChildren<Text>(true);
            if (legacyText != null)
                legacyText.text = label;
        }

        int capturedIndex = index;
        toggle.onValueChanged.AddListener((isOn) =>
        {
            if (_isSyncingToggle)
                return;

            if (isOn)
            {
                SelectIndex(capturedIndex, syncToggle: false);
                return;
            }

            if (_selectedIndex == capturedIndex)
                ClearSelection();
        });

        toggle.isOn = false;
        _itemToggles.Add(toggle);
    }

    private void RestoreSelection(string previousShareId)
    {
        if (_items.Count == 0)
        {
            ClearSelection();
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousShareId))
        {
            for (int i = 0; i < _items.Count; i++)
            {
                BlockShareListItemViewModel item = _items[i];
                if (item == null)
                    continue;

                if (string.Equals(previousShareId, Normalize(item.ShareId), StringComparison.Ordinal))
                {
                    SelectIndex(i, syncToggle: true);
                    return;
                }
            }
        }

        SelectIndex(0, syncToggle: true);
    }

    private void SelectIndex(int index, bool syncToggle)
    {
        if (index < 0 || index >= _items.Count)
        {
            ClearSelection();
            return;
        }

        _selectedIndex = index;
        BlockShareListItemViewModel item = _items[index];
        SelectedShareId = item != null ? Normalize(item.ShareId) : string.Empty;
        SelectedDetailInfo = item != null ? BuildSelectedDetailInfo(item) : null;

        if (syncToggle)
        {
            _isSyncingToggle = true;
            for (int i = 0; i < _itemToggles.Count; i++)
            {
                if (_itemToggles[i] != null)
                    _itemToggles[i].isOn = i == _selectedIndex;
            }

            _isSyncingToggle = false;
        }

        UpdateSelectionText();
        UpdateButtons();
    }

    private void ClearSelection()
    {
        _selectedIndex = -1;
        SelectedShareId = string.Empty;
        SelectedDetailInfo = null;
        UpdateSelectionText();
        UpdateButtons();
    }

    private void ClearListUi()
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
        if (_refreshNowButton != null)
            _refreshNowButton.interactable = !_isBusy;
    }

    private void UpdateSelectionText()
    {
        if (_selectedText == null)
            return;

        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            _selectedText.text = "Selected: -";
            return;
        }

        BlockShareListItemViewModel selected = _items[_selectedIndex];
        string shareId = selected != null ? Normalize(selected.ShareId) : string.Empty;
        int seq = selected != null ? selected.UserLevelSeq : 0;
        _selectedText.text = $"Selected: shareId={shareId}, seq={seq}";
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

    private static ChatRoomBlockShareInfo BuildSelectedDetailInfo(BlockShareListItemViewModel item)
    {
        return new ChatRoomBlockShareInfo
        {
            BlockShareId = Normalize(item.ShareId),
            RoomId = Normalize(item.RoomId),
            UserId = Normalize(item.UserId),
            UserLevelSeq = item.UserLevelSeq,
            Message = Normalize(item.FileName),
            CreatedAtUtc = Normalize(item.CreatedAtUtc)
        };
    }

    private static string BuildFallbackLabel(BlockShareListItemViewModel item)
    {
        if (item == null)
            return "- / -";

        string userId = Normalize(item.UserId);
        if (string.IsNullOrWhiteSpace(userId))
            userId = "-";

        string fileName = Normalize(item.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = item.UserLevelSeq > 0 ? item.UserLevelSeq.ToString() : "-";

        return $"{userId} / {fileName}";
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
