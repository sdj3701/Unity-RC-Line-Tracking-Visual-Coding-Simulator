using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RC.Network.Fusion;
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
    [SerializeField] private bool _usePhotonRooms = true;
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
    private FusionLobbyService _boundPhotonLobbyService;
    private string _pendingJoinRequestId = string.Empty;
    private string _pendingJoinRoomId = string.Empty;
    private bool _isWaitingJoinApproval;
    private float _nextJoinApprovalPollTime;
    private TaskCompletionSource<PhotonApiJoinRequestResult> _photonApiJoinRequestTcs;
    private string _pendingPhotonApiRoomId = string.Empty;

    public string SelectedRoomId { get; private set; }

    private sealed class PhotonApiJoinRequestResult
    {
        public bool Success;
        public string Message;
        public ChatRoomJoinRequestInfo Info;
    }

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

        if (_usePhotonRooms)
            BindPhotonLobbyService();

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

        if (_usePhotonRooms)
            UnbindPhotonLobbyService();

        UnbindManagerEvents();
        StopJoinApprovalPolling();
        CompletePendingPhotonApiJoinRequest(false, "canceled");
    }

    private void Update()
    {
        if (_usePhotonRooms)
            return;

        TryPollJoinApprovalStatus();
    }

    public void OnClickFetchRoomList()
    {
        if (_usePhotonRooms)
        {
            _ = FetchPhotonRoomListAsync();
            return;
        }

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

        if (_usePhotonRooms)
        {
            _ = JoinSelectedPhotonRoomAsync();
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

        NetworkRoomIdentity.ApplyRoomContext(
            apiRoomId: selectedRoom.RoomId,
            photonSessionName: null,
            roomName: selectedRoom.Title,
            hostUserId: selectedRoom.OwnerUserId,
            createdAtUtc: selectedRoom.CreatedAtUtc);

        if (_roomListPanel != null)
            _roomListPanel.SetActive(false);

        if (_moveToSceneOnConfirm && !string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    private async Task FetchPhotonRoomListAsync()
    {
        TryResolveContentRoot();

        FusionConnectionManager connection = FusionConnectionManager.GetOrCreate();
        if (!connection.IsInSessionLobby)
        {
            FusionDebugLog.Info(FusionDebugFlow.Lobby, "Room list requested before Photon lobby was ready. Connecting lobby first.");
            bool connected = await connection.ConnectToPhotonLobbyAsync();
            if (!connected)
            {
                FusionDebugLog.Error(FusionDebugFlow.Lobby, $"Photon lobby connection failed. {connection.LastErrorMessage}");
                return;
            }
        }

        FusionLobbyService lobbyService = FusionLobbyService.GetOrCreate();
        lobbyService.RefreshFromConnectionManager();
        HandlePhotonRoomListSucceeded(lobbyService.Rooms);
    }

    private async Task JoinSelectedPhotonRoomAsync()
    {
        string sessionName = SelectedRoomId != null ? SelectedRoomId.Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            FusionDebugLog.Warning(FusionDebugFlow.Room, "Photon room join skipped because no room is selected.");
            return;
        }

        FusionLobbyService lobbyService = FusionLobbyService.GetOrCreate();
        lobbyService.RefreshFromConnectionManager();
        lobbyService.TryGetRoom(sessionName, out FusionRoomInfo roomInfo);

        string apiRoomId = roomInfo != null ? roomInfo.ApiRoomId : string.Empty;
        if (_useJoinRequestOnConfirm)
        {
            PhotonApiJoinRequestResult apiJoinRequest = await RequestApiJoinForPhotonRoomAsync(apiRoomId);
            if (apiJoinRequest == null || !apiJoinRequest.Success)
            {
                string message = apiJoinRequest != null ? apiJoinRequest.Message : "api join request failed";
                FusionDebugLog.Error(FusionDebugFlow.Room, $"Photon room join blocked because API join request failed. session={sessionName}, apiRoomId={apiRoomId}, message={message}");
                return;
            }
        }

        if (_roomListPanel != null)
            _roomListPanel.SetActive(false);

        FusionDebugLog.Info(FusionDebugFlow.Room, $"Photon room join requested from room list. session={sessionName}");
        FusionRoomService roomService = FusionRoomService.GetOrCreate();
        bool success = await roomService.JoinRoomAsync(
            sessionName,
            roomInfo,
            _targetSceneName,
            loadSceneOnSuccess: false);

        if (!success)
        {
            FusionDebugLog.Error(FusionDebugFlow.Room, $"Photon room join failed. {roomService.LastErrorMessage}");
            return;
        }

        NetworkRoomIdentity.ApplyRoomContext(
            apiRoomId,
            sessionName,
            roomInfo != null ? roomInfo.RoomName : sessionName,
            roomInfo != null ? roomInfo.HostUserId : string.Empty,
            roomInfo != null ? roomInfo.CreatedAtUtc : string.Empty);

        if (_moveToSceneOnConfirm && !string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    private void HandlePhotonRoomListSucceeded(IReadOnlyList<FusionRoomInfo> rooms)
    {
        if (rooms == null || rooms.Count == 0)
        {
            HandleRoomListSucceeded(Array.Empty<ChatRoomSummaryInfo>());
            FusionDebugLog.Info(FusionDebugFlow.Lobby, "Photon room list is empty.");
            return;
        }

        var converted = new ChatRoomSummaryInfo[rooms.Count];
        for (int i = 0; i < rooms.Count; i++)
        {
            FusionRoomInfo room = rooms[i];
            converted[i] = new ChatRoomSummaryInfo
            {
                RoomId = room.SessionName,
                Title = BuildPhotonRoomLabel(room),
                OwnerUserId = room.HostUserId,
                CreatedAtUtc = room.CreatedAtUtc
            };
        }

        HandleRoomListSucceeded(converted);
        FusionDebugLog.Info(FusionDebugFlow.Lobby, $"Photon room list rendered. count={converted.Length}");
    }

    private static string BuildPhotonRoomLabel(FusionRoomInfo room)
    {
        if (room == null)
            return "Photon Room";

        string title = string.IsNullOrWhiteSpace(room.DisplayName)
            ? room.SessionName
            : room.DisplayName;

        string state = room.IsOpen ? "Open" : "Closed";
        return $"{title} ({room.PlayerCount}/{room.MaxPlayers}) {state}";
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
        _boundManager.OnJoinRequestCanceled += HandleJoinRequestCanceled;
        _boundManager.OnMyJoinRequestStatusFetchSucceeded += HandleMyJoinRequestStatusFetchSucceeded;
        _boundManager.OnMyJoinRequestStatusFetchFailed += HandleMyJoinRequestStatusFetchFailed;
        _boundManager.OnMyJoinRequestStatusFetchCanceled += HandleMyJoinRequestStatusFetchCanceled;
    }

    private void BindPhotonLobbyService()
    {
        FusionLobbyService lobbyService = FusionLobbyService.GetOrCreate();
        if (_boundPhotonLobbyService == lobbyService)
            return;

        UnbindPhotonLobbyService();
        _boundPhotonLobbyService = lobbyService;
        _boundPhotonLobbyService.OnRoomsUpdated += HandlePhotonRoomsUpdated;
    }

    private void UnbindPhotonLobbyService()
    {
        if (_boundPhotonLobbyService == null)
            return;

        _boundPhotonLobbyService.OnRoomsUpdated -= HandlePhotonRoomsUpdated;
        _boundPhotonLobbyService = null;
    }

    private void HandlePhotonRoomsUpdated(IReadOnlyList<FusionRoomInfo> rooms)
    {
        if (!_usePhotonRooms)
            return;

        if (_roomListPanel != null && !_roomListPanel.activeInHierarchy)
            return;

        HandlePhotonRoomListSucceeded(rooms);
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnListSucceeded -= HandleRoomListSucceeded;
        _boundManager.OnJoinRequestSucceeded -= HandleJoinRequestSucceeded;
        _boundManager.OnJoinRequestFailed -= HandleJoinRequestFailed;
        _boundManager.OnJoinRequestCanceled -= HandleJoinRequestCanceled;
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
        if (_photonApiJoinRequestTcs != null)
        {
            string pendingRoomId = info != null ? info.RoomId : string.Empty;
            if (string.IsNullOrWhiteSpace(_pendingPhotonApiRoomId) ||
                string.Equals(_pendingPhotonApiRoomId, pendingRoomId ?? string.Empty, StringComparison.Ordinal))
            {
                CompletePendingPhotonApiJoinRequest(true, string.Empty, info);
            }
        }

        if (!_usePhotonRooms)
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
        CompletePendingPhotonApiJoinRequest(false, message);
        StopJoinApprovalPolling();

        if (!_debugLog)
            return;

        Debug.LogWarning($"[But_RoomList] Join request failed: {message}");
    }

    private void HandleJoinRequestCanceled()
    {
        CompletePendingPhotonApiJoinRequest(false, "canceled");
        StopJoinApprovalPolling();
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

    private async Task<PhotonApiJoinRequestResult> RequestApiJoinForPhotonRoomAsync(string apiRoomId)
    {
        if (!_useJoinRequestOnConfirm)
        {
            return new PhotonApiJoinRequestResult
            {
                Success = true,
                Message = string.Empty,
                Info = null
            };
        }

        string resolvedApiRoomId = string.IsNullOrWhiteSpace(apiRoomId) ? string.Empty : apiRoomId.Trim();
        if (string.IsNullOrWhiteSpace(resolvedApiRoomId))
        {
            return new PhotonApiJoinRequestResult
            {
                Success = false,
                Message = "api roomId is empty",
                Info = null
            };
        }

        TryBindManagerEvents();
        if (_boundManager == null)
        {
            return new PhotonApiJoinRequestResult
            {
                Success = false,
                Message = "ChatRoomManager.Instance is null",
                Info = null
            };
        }

        if (_boundManager.IsBusy)
        {
            return new PhotonApiJoinRequestResult
            {
                Success = false,
                Message = "ChatRoomManager is busy",
                Info = null
            };
        }

        _pendingPhotonApiRoomId = resolvedApiRoomId;
        _photonApiJoinRequestTcs = new TaskCompletionSource<PhotonApiJoinRequestResult>();
        _boundManager.RequestJoinRequest(resolvedApiRoomId, ResolveTokenOverride());
        return await _photonApiJoinRequestTcs.Task;
    }

    private void CompletePendingPhotonApiJoinRequest(bool success, string message, ChatRoomJoinRequestInfo info = null)
    {
        if (_photonApiJoinRequestTcs == null)
            return;

        _photonApiJoinRequestTcs.TrySetResult(new PhotonApiJoinRequestResult
        {
            Success = success,
            Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim(),
            Info = info
        });
        _photonApiJoinRequestTcs = null;
        _pendingPhotonApiRoomId = string.Empty;
    }

    private void NavigateToApprovedRoom(string roomId)
    {
        string resolvedRoomId = !string.IsNullOrWhiteSpace(roomId)
            ? roomId.Trim()
            : (!string.IsNullOrWhiteSpace(SelectedRoomId) ? SelectedRoomId.Trim() : string.Empty);

        ChatRoomSummaryInfo selectedRoom = ResolveRoomForNavigation(resolvedRoomId);

        NetworkRoomIdentity.ApplyRoomContext(
            apiRoomId: selectedRoom.RoomId,
            photonSessionName: null,
            roomName: selectedRoom.Title,
            hostUserId: selectedRoom.OwnerUserId,
            createdAtUtc: selectedRoom.CreatedAtUtc);

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
