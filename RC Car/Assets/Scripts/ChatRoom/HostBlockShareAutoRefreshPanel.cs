using System;
using System.Collections;
using System.Collections.Generic;
using Auth;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HostBlockShareAutoRefreshPanel : MonoBehaviour
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
    [SerializeField] private TMP_Text _detailText;

    [Header("Polling")]
    [SerializeField] private bool _pollOnEnable = true;
    [SerializeField] private float _pollIntervalSeconds = 5f;
    [SerializeField] private int _defaultPage = 1;
    [SerializeField] private int _defaultSize = 20;
    [SerializeField] private bool _hostOnly = true;
    [SerializeField] private bool _fetchDetailWhenSelected = true;
    [SerializeField] private bool _refreshDetailAfterListUpdate = false;

    [Header("Request")]
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private string _tokenOverride = string.Empty;
    [SerializeField] private bool _autoRoomFromSession = true;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private readonly List<ChatRoomBlockShareInfo> _items = new List<ChatRoomBlockShareInfo>();
    private readonly List<GameObject> _itemObjects = new List<GameObject>();
    private readonly List<Toggle> _itemToggles = new List<Toggle>();

    private ChatRoomManager _boundManager;
    private bool _isBound;
    private Coroutine _pollRoutine;
    private Coroutine _pendingDetailRoutine;
    private int _selectedIndex = -1;
    private string _lastDetailRequestShareId = string.Empty;

    public string SelectedShareId { get; private set; }
    public ChatRoomBlockShareInfo SelectedDetailInfo { get; private set; }

    public bool TryGetSelectedListItemInfo(out string message, out int userLevelSeq)
    {
        message = string.Empty;
        userLevelSeq = 0;

        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
            return false;

        ChatRoomBlockShareInfo selected = _items[_selectedIndex];
        if (selected == null)
            return false;

        message = string.IsNullOrWhiteSpace(selected.Message) ? string.Empty : selected.Message.Trim();
        userLevelSeq = selected.UserLevelSeq;
        return true;
    }

    public bool TryGetListItemInfoByShareId(string shareIdRaw, out string message, out int userLevelSeq)
    {
        message = string.Empty;
        userLevelSeq = 0;

        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
            return false;

        for (int i = 0; i < _items.Count; i++)
        {
            ChatRoomBlockShareInfo item = _items[i];
            if (item == null)
                continue;

            string itemShareId = string.IsNullOrWhiteSpace(item.BlockShareId) ? string.Empty : item.BlockShareId.Trim();
            if (!string.Equals(shareId, itemShareId, StringComparison.Ordinal))
                continue;

            message = string.IsNullOrWhiteSpace(item.Message) ? string.Empty : item.Message.Trim();
            userLevelSeq = item.UserLevelSeq;
            return true;
        }

        return false;
    }

    public bool TryGetCheckedShareIds(out List<string> shareIds)
    {
        shareIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int count = Mathf.Min(_items.Count, _itemToggles.Count);

        for (int i = 0; i < count; i++)
        {
            Toggle toggle = _itemToggles[i];
            if (toggle == null || !toggle.isOn)
                continue;

            ChatRoomBlockShareInfo item = _items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.BlockShareId))
                continue;

            string shareId = item.BlockShareId.Trim();
            if (seen.Add(shareId))
                shareIds.Add(shareId);
        }

        return shareIds.Count > 0;
    }

    private void Start()
    {
        if (_itemTemplate != null)
            _itemTemplate.gameObject.SetActive(false);

        BindButtons();
        UpdateSelectionText();
        UpdateDetailText(null);
    }

    private void OnEnable()
    {
        BindButtons();
        TryBindManagerEvents();
        StartPollingIfNeeded();
    }

    private void OnDisable()
    {
        StopPolling();
        StopPendingDetailRoutine();
        UnbindManagerEvents();
        UnbindButtons();
    }

    public void OpenPanel()
    {
        SetPanelVisible(true);
        RequestListNow();
        StartPollingIfNeeded();
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

    public void RequestListNow()
    {
        TryBindManagerEvents();
        if (_boundManager == null)
        {
            SetStatus("ChatRoomManager is null.");
            return;
        }

        if (_hostOnly && !IsHost())
        {
            SetStatus("Current user is not host.");
            return;
        }

        if (_boundManager.IsBusy)
        {
            SetStatus("ChatRoomManager is busy. list refresh skipped.");
            return;
        }

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("RoomId is empty.");
            return;
        }

        _boundManager.FetchBlockShares(
            roomId,
            Mathf.Max(1, _defaultPage),
            Mathf.Max(1, _defaultSize),
            ResolveTokenOverride());
    }

    public void RequestSelectedDetailNow()
    {
        TryBindManagerEvents();
        if (_boundManager == null)
        {
            SetStatus("ChatRoomManager is null.");
            return;
        }

        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            SetStatus("Select one share first.");
            return;
        }

        if (_boundManager.IsBusy)
        {
            SetStatus("ChatRoomManager is busy. detail refresh queued.");
            EnqueueDetailRequest();
            return;
        }

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("RoomId is empty.");
            return;
        }

        ChatRoomBlockShareInfo selected = _items[_selectedIndex];
        string shareId = selected != null ? selected.BlockShareId : string.Empty;
        if (string.IsNullOrWhiteSpace(shareId))
        {
            SetStatus("Selected item has no shareId.");
            return;
        }

        _lastDetailRequestShareId = shareId.Trim();
        _boundManager.FetchBlockShareDetail(roomId, _lastDetailRequestShareId, ResolveTokenOverride());
    }

    private void StartPollingIfNeeded()
    {
        if (!_pollOnEnable)
            return;

        if (_pollRoutine != null)
            return;

        _pollRoutine = StartCoroutine(PollRoutine());
    }

    private void StopPolling()
    {
        if (_pollRoutine == null)
            return;

        StopCoroutine(_pollRoutine);
        _pollRoutine = null;
    }

    private void StopPendingDetailRoutine()
    {
        if (_pendingDetailRoutine == null)
            return;

        StopCoroutine(_pendingDetailRoutine);
        _pendingDetailRoutine = null;
    }

    private IEnumerator PollRoutine()
    {
        float interval = Mathf.Max(1f, _pollIntervalSeconds);
        var wait = new WaitForSeconds(interval);

        while (enabled)
        {
            if (IsPanelVisible())
                RequestListNow();

            yield return wait;
        }

        _pollRoutine = null;
    }

    private void EnqueueDetailRequest()
    {
        if (_pendingDetailRoutine != null)
            return;

        _pendingDetailRoutine = StartCoroutine(WaitAndRequestDetailWhenIdle());
    }

    private IEnumerator WaitAndRequestDetailWhenIdle()
    {
        while (enabled)
        {
            if (_boundManager == null)
                break;

            if (!_boundManager.IsBusy)
            {
                _pendingDetailRoutine = null;
                RequestSelectedDetailNow();
                yield break;
            }

            yield return null;
        }

        _pendingDetailRoutine = null;
    }

    private void TryBindManagerEvents()
    {
        ChatRoomManager manager = ChatRoomManager.Instance;
        if (manager == null)
            return;

        if (_boundManager == manager)
            return;

        UnbindManagerEvents();
        _boundManager = manager;

        _boundManager.OnBlockShareListFetchStarted += HandleListFetchStarted;
        _boundManager.OnBlockShareListFetchSucceeded += HandleListFetchSucceeded;
        _boundManager.OnBlockShareListFetchFailed += HandleListFetchFailed;
        _boundManager.OnBlockShareListFetchCanceled += HandleListFetchCanceled;

        _boundManager.OnBlockShareDetailFetchStarted += HandleDetailFetchStarted;
        _boundManager.OnBlockShareDetailFetchSucceeded += HandleDetailFetchSucceeded;
        _boundManager.OnBlockShareDetailFetchFailed += HandleDetailFetchFailed;
        _boundManager.OnBlockShareDetailFetchCanceled += HandleDetailFetchCanceled;

        _isBound = true;
    }

    private void UnbindManagerEvents()
    {
        if (!_isBound || _boundManager == null)
            return;

        _boundManager.OnBlockShareListFetchStarted -= HandleListFetchStarted;
        _boundManager.OnBlockShareListFetchSucceeded -= HandleListFetchSucceeded;
        _boundManager.OnBlockShareListFetchFailed -= HandleListFetchFailed;
        _boundManager.OnBlockShareListFetchCanceled -= HandleListFetchCanceled;

        _boundManager.OnBlockShareDetailFetchStarted -= HandleDetailFetchStarted;
        _boundManager.OnBlockShareDetailFetchSucceeded -= HandleDetailFetchSucceeded;
        _boundManager.OnBlockShareDetailFetchFailed -= HandleDetailFetchFailed;
        _boundManager.OnBlockShareDetailFetchCanceled -= HandleDetailFetchCanceled;

        _boundManager = null;
        _isBound = false;
    }

    private void HandleListFetchStarted(string roomId, int page, int size)
    {
        string targetRoomId = ResolveTargetRoomId();
        if (!IsSameRoom(targetRoomId, roomId))
            return;

        SetStatus($"Block list updating... roomId={roomId}, page={page}, size={size}");
    }

    private void HandleListFetchSucceeded(ChatRoomBlockShareListInfo info)
    {
        string roomId = info != null ? info.RoomId : string.Empty;
        string targetRoomId = ResolveTargetRoomId();
        if (!IsSameRoom(targetRoomId, roomId))
            return;

        string previousShareId = SelectedShareId;
        _items.Clear();

        ChatRoomBlockShareInfo[] incoming = info != null && info.Items != null
            ? info.Items
            : Array.Empty<ChatRoomBlockShareInfo>();

        for (int i = 0; i < incoming.Length; i++)
        {
            ChatRoomBlockShareInfo item = incoming[i];
            if (item == null)
                continue;

            _items.Add(item);
        }

        RebuildListUi();
        RestoreSelection(previousShareId);
        SetStatus($"Block list updated. count={_items.Count}");
    }

    private void HandleListFetchFailed(string roomId, int page, int size, string message)
    {
        string targetRoomId = ResolveTargetRoomId();
        if (!IsSameRoom(targetRoomId, roomId))
            return;

        SetStatus($"Block list update failed. message={message}");
    }

    private void HandleListFetchCanceled(string roomId, int page, int size)
    {
        string targetRoomId = ResolveTargetRoomId();
        if (!IsSameRoom(targetRoomId, roomId))
            return;

        SetStatus("Block list update canceled.");
    }

    private void HandleDetailFetchStarted(string roomId, string shareId)
    {
        if (!string.Equals(_lastDetailRequestShareId, shareId ?? string.Empty, StringComparison.Ordinal))
            return;

        SetStatus($"Detail updating... shareId={shareId}");
    }

    private void HandleDetailFetchSucceeded(ChatRoomBlockShareInfo info)
    {
        string shareId = info != null ? info.BlockShareId : string.Empty;
        if (!string.IsNullOrWhiteSpace(_lastDetailRequestShareId) &&
            !string.Equals(_lastDetailRequestShareId, shareId ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        SelectedDetailInfo = info;
        UpdateDetailText(info);
        SetStatus($"Detail updated. shareId={shareId}");
    }

    private void HandleDetailFetchFailed(string roomId, string shareId, string message)
    {
        if (!string.IsNullOrWhiteSpace(_lastDetailRequestShareId) &&
            !string.Equals(_lastDetailRequestShareId, shareId ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        SetStatus($"Detail update failed. shareId={shareId}, message={message}");
    }

    private void HandleDetailFetchCanceled(string roomId, string shareId)
    {
        if (!string.IsNullOrWhiteSpace(_lastDetailRequestShareId) &&
            !string.Equals(_lastDetailRequestShareId, shareId ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        SetStatus($"Detail update canceled. shareId={shareId}");
    }

    private void RebuildListUi()
    {
        ClearListUi();

        for (int i = 0; i < _items.Count; i++)
            CreateListItem(i, _items[i]);

        if (_emptyStateObject != null)
            _emptyStateObject.SetActive(_items.Count == 0);
    }

    private void CreateListItem(int index, ChatRoomBlockShareInfo item)
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

        string label = BuildListLabel(item);
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
            if (isOn)
                SelectIndex(capturedIndex, syncToggle: false);
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
                ChatRoomBlockShareInfo item = _items[i];
                string shareId = item != null ? item.BlockShareId : string.Empty;
                if (string.Equals(previousShareId, shareId ?? string.Empty, StringComparison.Ordinal))
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
        ChatRoomBlockShareInfo item = _items[index];
        SelectedShareId = item != null && !string.IsNullOrWhiteSpace(item.BlockShareId)
            ? item.BlockShareId.Trim()
            : string.Empty;

        if (syncToggle)
        {
            for (int i = 0; i < _itemToggles.Count; i++)
            {
                if (_itemToggles[i] != null)
                    _itemToggles[i].isOn = i == index;
            }
        }

        UpdateSelectionText();

        if (_refreshDetailAfterListUpdate || _fetchDetailWhenSelected)
            RequestSelectedDetailNow();
    }

    private void ClearSelection()
    {
        _selectedIndex = -1;
        SelectedShareId = string.Empty;
        SelectedDetailInfo = null;
        _lastDetailRequestShareId = string.Empty;
        UpdateSelectionText();
        UpdateDetailText(null);
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

    private void UpdateSelectionText()
    {
        if (_selectedText == null)
            return;

        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            _selectedText.text = "Selected: -";
            return;
        }

        ChatRoomBlockShareInfo selected = _items[_selectedIndex];
        string shareId = selected != null ? selected.BlockShareId : string.Empty;
        int seq = selected != null ? selected.UserLevelSeq : 0;
        _selectedText.text = $"Selected: shareId={shareId}, seq={seq}";
    }

    private void UpdateDetailText(ChatRoomBlockShareInfo detail)
    {
        if (_detailText == null)
            return;

        if (detail == null)
        {
            _detailText.text = "Detail: -";
            return;
        }

        string shareId = string.IsNullOrWhiteSpace(detail.BlockShareId) ? "-" : detail.BlockShareId;
        string roomId = string.IsNullOrWhiteSpace(detail.RoomId) ? "-" : detail.RoomId;
        string userId = string.IsNullOrWhiteSpace(detail.UserId) ? "-" : detail.UserId;
        string message = string.IsNullOrWhiteSpace(detail.Message) ? "(empty)" : detail.Message;
        string createdAt = string.IsNullOrWhiteSpace(detail.CreatedAtUtc) ? "-" : detail.CreatedAtUtc;

        _detailText.text =
            $"Detail\nshareId: {shareId}\nroomId: {roomId}\nuserId: {userId}\nseq: {detail.UserLevelSeq}\ncreatedAt: {createdAt}\nmessage: {message}";
    }

    private void BindButtons()
    {
        if (_openButton != null)
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
            _refreshNowButton.onClick.RemoveListener(RequestListNow);
            _refreshNowButton.onClick.AddListener(RequestListNow);
        }
    }

    private void UnbindButtons()
    {
        if (_openButton != null)
            _openButton.onClick.RemoveListener(OpenPanel);

        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(ClosePanel);

        if (_refreshNowButton != null)
            _refreshNowButton.onClick.RemoveListener(RequestListNow);
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

    private string ResolveTargetRoomId()
    {
        if (_autoRoomFromSession)
        {
            RoomInfo sessionRoom = RoomSessionContext.CurrentRoom;
            if (sessionRoom != null && !string.IsNullOrWhiteSpace(sessionRoom.RoomId))
                return sessionRoom.RoomId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_roomIdOverride))
            return _roomIdOverride.Trim();

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom != null && !string.IsNullOrWhiteSpace(currentRoom.RoomId))
            return currentRoom.RoomId.Trim();

        return string.Empty;
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_tokenOverride) ? null : _tokenOverride.Trim();
    }

    private bool IsHost()
    {
        RoomInfo room = RoomSessionContext.CurrentRoom;
        if (room == null || string.IsNullOrWhiteSpace(room.HostUserId))
            return false;

        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
            return false;

        string hostUserId = room.HostUserId.Trim();
        string currentUserId = AuthManager.Instance.CurrentUser.userId ?? string.Empty;
        currentUserId = currentUserId.Trim();
        return string.Equals(hostUserId, currentUserId, StringComparison.Ordinal);
    }

    private static bool IsSameRoom(string expectedRoomId, string incomingRoomId)
    {
        string left = string.IsNullOrWhiteSpace(expectedRoomId) ? string.Empty : expectedRoomId.Trim();
        string right = string.IsNullOrWhiteSpace(incomingRoomId) ? string.Empty : incomingRoomId.Trim();
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static string BuildListLabel(ChatRoomBlockShareInfo item)
    {
        if (item == null)
            return "- / -";

        string userId = string.IsNullOrWhiteSpace(item.UserId) ? "-" : item.UserId.Trim();
        string xmlFileName = string.IsNullOrWhiteSpace(item.Message)
            ? (item.UserLevelSeq > 0 ? item.UserLevelSeq.ToString() : "-")
            : item.Message.Trim();

        return $"{userId} / {xmlFileName}";
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[HostBlockShareAutoRefreshPanel] {text}");
    }
}
