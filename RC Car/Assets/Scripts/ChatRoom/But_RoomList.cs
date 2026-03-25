using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class But_RoomList : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private Button _but_Cancel;
    [SerializeField] private Button _but_Confirm;
    [SerializeField] private GameObject _roomListPanel;
    [SerializeField] private Transform _roomListContentRoot;
    [SerializeField] private Toggle _roomTogglePrefab;
    [SerializeField] private ToggleGroup _toggleGroup;
    [SerializeField] private TMP_InputField _requestTokenInput;
    [SerializeField] private bool _useJoinRequestOnConfirm = true;
    [SerializeField] private bool _closePanelOnJoinRequestSubmit = true;
    [SerializeField] private bool _moveToSceneOnConfirm = true;
    [SerializeField] private string _targetSceneName = "03_NetworkCarTest";
    [SerializeField] private bool _waitForJoinApproval = true;
    [SerializeField] private float _joinApprovalPollIntervalSeconds = 2f;
    [SerializeField] private bool _stopPollingWhenRejected = true;
    [SerializeField] private bool _bindOnEnable = true;
    [SerializeField] private bool _clearPreviousItemsOnRefresh = true;
    [SerializeField] private bool _debugLog = true;

    private const float MinJoinApprovalPollIntervalSeconds = 0.5f;
    private readonly List<Toggle> _spawnedToggles = new List<Toggle>();
    private readonly Dictionary<string, ChatRoomSummaryInfo> _roomMap = new Dictionary<string, ChatRoomSummaryInfo>();
    private ChatRoomManager _boundManager;
    private string _pendingJoinRequestId = string.Empty;
    private string _pendingJoinRoomId = string.Empty;
    private bool _isWaitingJoinApproval;
    private float _nextJoinApprovalPollTime;

    public string SelectedRoomId { get; private set; }

    private void OnEnable()
    {
        if (_button == null)
            _button = GetComponent<Button>();

        if (_bindOnEnable && _button != null)
        {
            _button.onClick.RemoveListener(OnClickFetchRoomList);
            _button.onClick.AddListener(OnClickFetchRoomList);
        }

        if (_but_Cancel != null)
        {
            _but_Cancel.onClick.RemoveListener(OnClickCancel);
            _but_Cancel.onClick.AddListener(OnClickCancel);
        }

        if (_but_Confirm != null)
        {
            _but_Confirm.onClick.RemoveListener(OnClickConfirm);
            _but_Confirm.onClick.AddListener(OnClickConfirm);
        }

        TryBindManagerEvents();
        TryResolveContentRoot();
    }

    private void OnDisable()
    {
        if (_bindOnEnable && _button != null)
            _button.onClick.RemoveListener(OnClickFetchRoomList);

        if (_but_Cancel != null)
            _but_Cancel.onClick.RemoveListener(OnClickCancel);

        if (_but_Confirm != null)
            _but_Confirm.onClick.RemoveListener(OnClickConfirm);
        UnbindManagerEvents();
        StopJoinApprovalPolling();
    }

    private void Update()
    {
        TryPollJoinApprovalStatus();
    }

    public void OnClickFetchRoomList()
    {
        TryBindManagerEvents();

        if (_boundManager == null)
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] ChatRoomManager.Instance is null.");
            return;
        }

        _boundManager.FetchRoomList();
    }

    public void OnClickCancel()
    {
        if (_roomListPanel != null)
            _roomListPanel.SetActive(false);
    }

    public void OnClickConfirm()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoomId))
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] No room is selected.");
            return;
        }

        if (_useJoinRequestOnConfirm)
        {
            TryBindManagerEvents();
            if (_boundManager == null)
            {
                if (_debugLog)
                    Debug.LogWarning("[But_RoomList] ChatRoomManager.Instance is null.");
                return;
            }

            StopJoinApprovalPolling();
            _boundManager.RequestJoinRequest(SelectedRoomId, ResolveTokenOverride());

            if (_closePanelOnJoinRequestSubmit && _roomListPanel != null)
                _roomListPanel.SetActive(false);

            return;
        }

        ChatRoomSummaryInfo selectedRoom;
        if (!_roomMap.TryGetValue(SelectedRoomId, out selectedRoom))
            selectedRoom = new ChatRoomSummaryInfo { RoomId = SelectedRoomId, Title = string.Empty };

        RoomSessionContext.Set(new RoomInfo
        {
            RoomId = selectedRoom.RoomId,
            RoomName = selectedRoom.Title,
            HostUserId = selectedRoom.OwnerUserId,
            CreatedAtUtc = selectedRoom.CreatedAtUtc
        });

        if (_roomListPanel != null)
            _roomListPanel.SetActive(false);

        if (_moveToSceneOnConfirm && !string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
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
        _boundManager.OnListSucceeded += HandleRoomListSucceeded;
        _boundManager.OnJoinRequestSucceeded += HandleJoinRequestSucceeded;
        _boundManager.OnJoinRequestFailed += HandleJoinRequestFailed;
        _boundManager.OnMyJoinRequestStatusFetchSucceeded += HandleMyJoinRequestStatusFetchSucceeded;
        _boundManager.OnMyJoinRequestStatusFetchFailed += HandleMyJoinRequestStatusFetchFailed;
        _boundManager.OnMyJoinRequestStatusFetchCanceled += HandleMyJoinRequestStatusFetchCanceled;
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnListSucceeded -= HandleRoomListSucceeded;
        _boundManager.OnJoinRequestSucceeded -= HandleJoinRequestSucceeded;
        _boundManager.OnJoinRequestFailed -= HandleJoinRequestFailed;
        _boundManager.OnMyJoinRequestStatusFetchSucceeded -= HandleMyJoinRequestStatusFetchSucceeded;
        _boundManager.OnMyJoinRequestStatusFetchFailed -= HandleMyJoinRequestStatusFetchFailed;
        _boundManager.OnMyJoinRequestStatusFetchCanceled -= HandleMyJoinRequestStatusFetchCanceled;
        _boundManager = null;
    }

    private void TryPollJoinApprovalStatus()
    {
        if (!_useJoinRequestOnConfirm || !_waitForJoinApproval || !_isWaitingJoinApproval)
            return;

        if (Time.unscaledTime < _nextJoinApprovalPollTime)
            return;

        TryBindManagerEvents();
        if (_boundManager == null)
        {
            _nextJoinApprovalPollTime = Time.unscaledTime + Mathf.Max(MinJoinApprovalPollIntervalSeconds, _joinApprovalPollIntervalSeconds);
            return;
        }

        if (_boundManager.IsBusy)
        {
            _nextJoinApprovalPollTime = Time.unscaledTime + MinJoinApprovalPollIntervalSeconds;
            return;
        }

        if (string.IsNullOrWhiteSpace(_pendingJoinRequestId))
        {
            StopJoinApprovalPolling();
            return;
        }

        _boundManager.FetchMyJoinRequestStatus(_pendingJoinRequestId, ResolveTokenOverride());
        _nextJoinApprovalPollTime = Time.unscaledTime + Mathf.Max(MinJoinApprovalPollIntervalSeconds, _joinApprovalPollIntervalSeconds);
    }

    private void TryResolveContentRoot()
    {
        if (_roomListContentRoot != null)
            return;

        if (_roomListPanel == null)
            return;

        ScrollRect scrollRect = _roomListPanel.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect != null && scrollRect.content != null)
            _roomListContentRoot = scrollRect.content;
    }

    private void HandleRoomListSucceeded(ChatRoomSummaryInfo[] rooms)
    {
        TryResolveContentRoot();

        if (_roomListPanel != null && !_roomListPanel.activeSelf)
            _roomListPanel.SetActive(true);

        if (_roomTogglePrefab == null || _roomListContentRoot == null)
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] Toggle prefab/content root is not assigned.");
            return;
        }

        if (_clearPreviousItemsOnRefresh)
            ClearSpawnedToggles();
        else
            _roomMap.Clear();

        if (rooms == null || rooms.Length == 0)
            return;

        for (int i = 0; i < rooms.Length; i++)
        {
            ChatRoomSummaryInfo room = rooms[i];
            if (room == null || string.IsNullOrWhiteSpace(room.RoomId))
                continue;

            _roomMap[room.RoomId] = room;

            Toggle toggle = Instantiate(_roomTogglePrefab, _roomListContentRoot);
            toggle.isOn = false;

            if (_toggleGroup != null)
                toggle.group = _toggleGroup;

            string roomId = room.RoomId;
            string roomTitle = string.IsNullOrWhiteSpace(room.Title)
                ? $"Room {roomId}"
                : room.Title;

            SetToggleLabel(toggle, roomTitle);
            toggle.onValueChanged.AddListener(isOn => OnRoomToggleValueChanged(roomId, isOn));

            _spawnedToggles.Add(toggle);
        }

        if (_spawnedToggles.Count > 0)
            _spawnedToggles[0].isOn = true;
    }

    private void OnRoomToggleValueChanged(string roomId, bool isOn)
    {
        if (isOn)
        {
            SelectedRoomId = roomId;
            return;
        }

        if (SelectedRoomId == roomId)
            SelectedRoomId = null;
    }

    private void ClearSpawnedToggles()
    {
        for (int i = 0; i < _spawnedToggles.Count; i++)
        {
            if (_spawnedToggles[i] != null)
                Destroy(_spawnedToggles[i].gameObject);
        }

        _spawnedToggles.Clear();
        _roomMap.Clear();
        SelectedRoomId = null;
    }

    private string ResolveTokenOverride()
    {
        if (_requestTokenInput == null)
            return null;

        string token = _requestTokenInput.text;
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private void HandleJoinRequestSucceeded(ChatRoomJoinRequestInfo info)
    {
        StartJoinApprovalPolling(info);

        if (!_debugLog)
            return;

        string roomId = info != null ? info.RoomId : string.Empty;
        string requestId = info != null ? info.RequestId : string.Empty;
        string status = info != null ? info.Status : string.Empty;
        Debug.Log($"[But_RoomList] Join request sent. roomId={roomId}, requestId={requestId}, status={status}");
    }

    private void HandleJoinRequestFailed(string message)
    {
        StopJoinApprovalPolling();

        if (!_debugLog)
            return;

        Debug.LogWarning($"[But_RoomList] Join request failed: {message}");
    }

    private void StartJoinApprovalPolling(ChatRoomJoinRequestInfo info)
    {
        if (!_useJoinRequestOnConfirm || !_waitForJoinApproval)
            return;

        string requestId = info != null && !string.IsNullOrWhiteSpace(info.RequestId)
            ? info.RequestId.Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(requestId))
        {
            if (_debugLog)
                Debug.LogWarning("[But_RoomList] Join request id is empty. Approval polling skipped.");
            return;
        }

        _pendingJoinRequestId = requestId;
        _pendingJoinRoomId = info != null && !string.IsNullOrWhiteSpace(info.RoomId)
            ? info.RoomId.Trim()
            : (SelectedRoomId ?? string.Empty);
        _isWaitingJoinApproval = true;
        _nextJoinApprovalPollTime = Time.unscaledTime + Mathf.Max(MinJoinApprovalPollIntervalSeconds, _joinApprovalPollIntervalSeconds);
    }

    private void StopJoinApprovalPolling()
    {
        _isWaitingJoinApproval = false;
        _pendingJoinRequestId = string.Empty;
        _pendingJoinRoomId = string.Empty;
        _nextJoinApprovalPollTime = 0f;
    }

    private void HandleMyJoinRequestStatusFetchSucceeded(ChatRoomJoinRequestInfo info)
    {
        if (!_isWaitingJoinApproval)
            return;

        string status = NormalizeStatus(info != null ? info.Status : string.Empty);
        string requestId = info != null ? info.RequestId : string.Empty;
        string roomId = info != null ? info.RoomId : string.Empty;

        if (_debugLog)
            Debug.Log($"[But_RoomList] Join request status fetched. requestId={requestId}, roomId={roomId}, status={status}");

        if (IsApprovedStatus(status))
        {
            string targetRoomId = !string.IsNullOrWhiteSpace(roomId) ? roomId : _pendingJoinRoomId;
            StopJoinApprovalPolling();
            NavigateToApprovedRoom(targetRoomId);
            return;
        }

        if (_stopPollingWhenRejected && IsRejectedStatus(status))
        {
            StopJoinApprovalPolling();

            if (_debugLog)
                Debug.LogWarning($"[But_RoomList] Join request rejected. requestId={requestId}, status={status}");
        }
    }

    private void HandleMyJoinRequestStatusFetchFailed(string requestId, string message)
    {
        if (!_isWaitingJoinApproval)
            return;

        if (_debugLog)
            Debug.LogWarning($"[But_RoomList] Join request status fetch failed. requestId={requestId}, message={message}");

        if (ContainsAuthErrorCode(message))
            StopJoinApprovalPolling();
    }

    private void HandleMyJoinRequestStatusFetchCanceled(string requestId)
    {
        if (!_isWaitingJoinApproval)
            return;

        if (_debugLog)
            Debug.LogWarning($"[But_RoomList] Join request status fetch canceled. requestId={requestId}");
    }

    private void NavigateToApprovedRoom(string roomId)
    {
        string resolvedRoomId = !string.IsNullOrWhiteSpace(roomId)
            ? roomId.Trim()
            : (!string.IsNullOrWhiteSpace(SelectedRoomId) ? SelectedRoomId.Trim() : string.Empty);

        ChatRoomSummaryInfo selectedRoom = ResolveRoomForNavigation(resolvedRoomId);

        RoomSessionContext.Set(new RoomInfo
        {
            RoomId = selectedRoom.RoomId,
            RoomName = selectedRoom.Title,
            HostUserId = selectedRoom.OwnerUserId,
            CreatedAtUtc = selectedRoom.CreatedAtUtc
        });

        if (_moveToSceneOnConfirm && !string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    private ChatRoomSummaryInfo ResolveRoomForNavigation(string roomId)
    {
        if (!string.IsNullOrWhiteSpace(roomId) && _roomMap.TryGetValue(roomId, out ChatRoomSummaryInfo room))
            return room;

        if (!string.IsNullOrWhiteSpace(SelectedRoomId) && _roomMap.TryGetValue(SelectedRoomId, out ChatRoomSummaryInfo selected))
            return selected;

        return new ChatRoomSummaryInfo
        {
            RoomId = roomId ?? string.Empty,
            Title = string.Empty,
            OwnerUserId = string.Empty,
            CreatedAtUtc = string.Empty
        };
    }

    private static string NormalizeStatus(string status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim().ToUpperInvariant();
    }

    private static bool IsApprovedStatus(string status)
    {
        string normalized = NormalizeStatus(status);
        return normalized == "APPROVED" || normalized == "ACCEPTED";
    }

    private static bool IsRejectedStatus(string status)
    {
        string normalized = NormalizeStatus(status);
        return normalized == "REJECTED" || normalized == "DENIED" || normalized == "CANCELED" || normalized == "CANCELLED";
    }

    private static bool ContainsAuthErrorCode(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.IndexOf("401", StringComparison.Ordinal) >= 0 ||
               message.IndexOf("403", StringComparison.Ordinal) >= 0;
    }

    private static void SetToggleLabel(Toggle toggle, string label)
    {
        TMP_Text tmpText = toggle.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null)
        {
            tmpText.text = label;
            return;
        }

        Text legacyText = toggle.GetComponentInChildren<Text>(true);
        if (legacyText != null)
            legacyText.text = label;
    }
}
