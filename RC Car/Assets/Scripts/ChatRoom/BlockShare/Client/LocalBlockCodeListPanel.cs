using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LocalBlockCodeListPanel : MonoBehaviour
{
    [Header("Main Panel")]
    [SerializeField] private GameObject _mainPanel;

    [Header("List")]
    [SerializeField] private Transform _listContent;
    [SerializeField] private Toggle _itemTemplate;

    [Header("Buttons")]
    [SerializeField] private Button _openButton;
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _refreshButton;
    [SerializeField] private BlockShareUploadButtonView _uploadButtonView;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private readonly List<GameObject> _itemObjects = new List<GameObject>();
    private readonly List<Toggle> _itemToggles = new List<Toggle>();
    private readonly List<LocalBlockCodeEntry> _entries = new List<LocalBlockCodeEntry>();

    private int _selectedIndex = -1;
    private bool _isBusy;
    private bool _isSyncingToggle;
    private bool _refreshPendingOnEnable;

    public event Action RefreshRequested;
    public event Action SelectionChanged;

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

    public bool IsOwnedActionButton(Button button)
    {
        if (button == null)
            return false;

        if (button == _openButton || button == _closeButton || button == _refreshButton)
            return true;

        return _uploadButtonView != null && _uploadButtonView.IsOwnedUploadButton(button);
    }

    private void OpenPanel()
    {
        SetPanelVisible(true);

        if (isActiveAndEnabled)
            RaiseRefreshRequested();
        else
            _refreshPendingOnEnable = true;
    }

    public void ClosePanel()
    {
        SetPanelVisible(false);
    }

    public void TogglePanel()
    {
        if (IsPanelVisible())
        {
            ClosePanel();
            return;
        }

        OpenPanel();
    }

    public void RenderLocalFiles(IReadOnlyList<LocalBlockCodeEntry> entries)
    {
        string previousFileName = GetSelectedFileName();

        ClearListObjects();
        _entries.Clear();
        _selectedIndex = -1;

        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                LocalBlockCodeEntry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FileName))
                    continue;

                _entries.Add(entry);
            }
        }

        for (int i = 0; i < _entries.Count; i++)
            CreateListItem(i, _entries[i]);

        RestoreSelection(previousFileName);
        UpdateButtons();
    }

    public bool TryGetSelectedEntry(out LocalBlockCodeEntry entry)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _entries.Count)
        {
            entry = _entries[_selectedIndex];
            return entry != null;
        }

        entry = null;
        return false;
    }

    public void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        UpdateButtons();
    }

    public void SetStatus(string message)
    {
        if (_debugLog && !string.IsNullOrWhiteSpace(message))
            Debug.Log($"[LocalBlockCodeListPanel] {message}");
    }

    private void RaiseRefreshRequested()
    {
        if (_debugLog)
            Debug.Log("[LocalBlockCodeListPanel] Refresh requested.");

        RefreshRequested?.Invoke();
    }

    private void BindButtons()
    {
        if (_openButton != null && !HasPersistentOpenBinding())
        {
            _openButton.onClick.RemoveListener(TogglePanel);
            _openButton.onClick.AddListener(TogglePanel);
        }

        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveListener(ClosePanel);
            _closeButton.onClick.AddListener(ClosePanel);
        }

        if (_refreshButton != null)
        {
            _refreshButton.onClick.RemoveListener(RaiseRefreshRequested);
            _refreshButton.onClick.AddListener(RaiseRefreshRequested);
        }
    }

    private void UnbindButtons()
    {
        if (_openButton != null)
            _openButton.onClick.RemoveListener(TogglePanel);

        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(ClosePanel);

        if (_refreshButton != null)
            _refreshButton.onClick.RemoveListener(RaiseRefreshRequested);
    }

    private bool HasPersistentOpenBinding()
    {
        return _openButton != null &&
               _openButton.onClick != null &&
               _openButton.onClick.GetPersistentEventCount() > 0;
    }

    private void CreateListItem(int index, LocalBlockCodeEntry entry)
    {
        if (_itemTemplate == null || _listContent == null || entry == null)
            return;

        GameObject itemObject = Instantiate(_itemTemplate.gameObject, _listContent);
        itemObject.SetActive(true);
        _itemObjects.Add(itemObject);

        Toggle toggle = itemObject.GetComponent<Toggle>();
        if (toggle == null)
            return;

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

    private void RestoreSelection(string previousFileName)
    {
        if (_entries.Count == 0)
        {
            ClearSelection();
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousFileName))
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                LocalBlockCodeEntry entry = _entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.FileName))
                    continue;

                if (string.Equals(previousFileName, entry.FileName.Trim(), StringComparison.Ordinal))
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
        if (index < 0 || index >= _entries.Count)
        {
            ClearSelection();
            return;
        }

        _selectedIndex = index;

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

        SelectionChanged?.Invoke();
        UpdateButtons();
    }

    private void ClearSelection()
    {
        _selectedIndex = -1;
        SelectionChanged?.Invoke();
        UpdateButtons();
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
        if (_refreshButton != null)
            _refreshButton.interactable = !_isBusy;
    }

    private string GetSelectedFileName()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
            return string.Empty;

        LocalBlockCodeEntry entry = _entries[_selectedIndex];
        return entry != null && !string.IsNullOrWhiteSpace(entry.FileName)
            ? entry.FileName.Trim()
            : string.Empty;
    }

    private static string BuildListLabel(LocalBlockCodeEntry entry)
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
}
