using System;
using System.Threading.Tasks;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyUIController : MonoBehaviour
{
    [Tooltip("Room Info UI")]
    [SerializeField] private GameObject _roomInfoUI;
    [SerializeField] private TMP_InputField _roomNameInputField;
    [SerializeField] private TMP_InputField _maxUserCountInputField;
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _closeRoomInfoButton;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private LobbyRoomFlow _roomFlow;
    [SerializeField] private ChatRoomManager _chatRoomManager;

    [Header("Photon Room Path")]
    [SerializeField] private bool _usePhotonRoomCreation = true;

    [Header("Scene Transition (ChatRoom Path)")]
    [SerializeField] private bool _moveToNetworkSceneOnChatRoomReady = true;
    [SerializeField] private string _targetSceneName = "03_NetworkCarTest";
    [SerializeField] private bool _storeRoomContext = true;

    private bool _isHybridCreateInProgress;
    private TaskCompletionSource<ChatRoomCreateInfo> _hybridApiCreateTcs;

    private void Awake()
    {
        SetRoomInfoVisible(false);
        SetStatusText(string.Empty);
    }

    private void OnEnable()
    {
        if (_createRoomButton != null)
        {
            _createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);
            _createRoomButton.onClick.AddListener(OnCreateRoomButtonClicked);
        }

        if (_closeRoomInfoButton != null)
        {
            _closeRoomInfoButton.onClick.RemoveListener(HideRoomInfoUI);
            _closeRoomInfoButton.onClick.AddListener(HideRoomInfoUI);
        }

        if (_roomFlow != null)
        {
            _roomFlow.OnRoomCreateStarted += HandleRoomCreateStarted;
            _roomFlow.OnRoomCreateProgress += HandleRoomCreateProgress;
            _roomFlow.OnRoomReady += HandleRoomReady;
            _roomFlow.OnRoomCreateFailed += HandleRoomCreateFailed;
            _roomFlow.OnRoomCreateCanceled += HandleRoomCreateCanceled;
        }

        if (_chatRoomManager != null)
        {
            _chatRoomManager.OnCreateStarted += HandleChatRoomCreateStarted;
            _chatRoomManager.OnCreateSucceeded += HandleChatRoomCreateSucceeded;
            _chatRoomManager.OnCreateFailed += HandleChatRoomCreateFailed;
            _chatRoomManager.OnCreateCanceled += HandleChatRoomCreateCanceled;
        }
    }

    private void OnDisable()
    {
        if (_createRoomButton != null)
            _createRoomButton.onClick.RemoveListener(OnCreateRoomButtonClicked);

        if (_closeRoomInfoButton != null)
            _closeRoomInfoButton.onClick.RemoveListener(HideRoomInfoUI);

        if (_roomFlow != null)
        {
            _roomFlow.OnRoomCreateStarted -= HandleRoomCreateStarted;
            _roomFlow.OnRoomCreateProgress -= HandleRoomCreateProgress;
            _roomFlow.OnRoomReady -= HandleRoomReady;
            _roomFlow.OnRoomCreateFailed -= HandleRoomCreateFailed;
            _roomFlow.OnRoomCreateCanceled -= HandleRoomCreateCanceled;
        }

        if (_chatRoomManager != null)
        {
            _chatRoomManager.OnCreateStarted -= HandleChatRoomCreateStarted;
            _chatRoomManager.OnCreateSucceeded -= HandleChatRoomCreateSucceeded;
            _chatRoomManager.OnCreateFailed -= HandleChatRoomCreateFailed;
            _chatRoomManager.OnCreateCanceled -= HandleChatRoomCreateCanceled;
        }
    }

    public void ShowRoomInfoUI()
    {
        SetRoomInfoVisible(true);
        SetStatusText(string.Empty);
        SetCreateButtonInteractable(true);
    }

    public void HideRoomInfoUI()
    {
        if (_roomFlow != null && _roomFlow.IsBusy)
            _roomFlow.CancelCurrentRequest();

        if (_chatRoomManager != null && _chatRoomManager.IsBusy)
            _chatRoomManager.CancelCurrentRequest();

        SetRoomInfoVisible(false);
        SetStatusText(string.Empty);
        SetCreateButtonInteractable(true);
    }

    public void OnCreateRoomButtonClicked()
    {
        if (!TryReadCreateInputs(out string roomName, out string maxUserCountRaw))
            return;

        if (_usePhotonRoomCreation)
        {
            _ = CreateHybridPhotonRoomFromInputsAsync(roomName, maxUserCountRaw);
            return;
        }

        if (_chatRoomManager != null)
        {
            _chatRoomManager.CreateRoom(roomName, maxUserCountRaw);
            return;
        }

        if (_roomFlow == null)
        {
            SetStatusText("ChatRoomManager or LobbyRoomFlow reference is missing.");
            return;
        }

        _roomFlow.CreateRoom(roomName);
    }

    private async Task CreateHybridPhotonRoomFromInputsAsync(string roomName, string maxUserCountRaw)
    {
        if (!int.TryParse(maxUserCountRaw, out int maxPlayers) || maxPlayers <= 0)
        {
            SetStatusText("Maximum players must be a number >= 1.");
            return;
        }

        SetCreateButtonInteractable(false);
        SetStatusText($"\"{roomName}\" Photon/API room creating...");
        FusionDebugLog.Info(FusionDebugFlow.Room, $"Lobby create button requested hybrid room. room={roomName}, maxPlayers={maxPlayers}");

        if (_chatRoomManager == null)
        {
            SetCreateButtonInteractable(true);
            SetStatusText("ChatRoomManager reference is missing.");
            return;
        }

        ChatRoomCreateInfo apiRoomInfo;
        try
        {
            apiRoomInfo = await CreateApiRoomForPhotonAsync(roomName, maxPlayers);
        }
        catch (Exception e)
        {
            SetCreateButtonInteractable(true);
            SetStatusText(string.IsNullOrWhiteSpace(e.Message)
                ? "API room creation failed."
                : e.Message);
            return;
        }

        if (apiRoomInfo == null || string.IsNullOrWhiteSpace(apiRoomInfo.RoomId))
        {
            SetCreateButtonInteractable(true);
            SetStatusText("API roomId is empty.");
            return;
        }

        FusionRoomService roomService = FusionRoomService.GetOrCreate();
        bool success = await roomService.CreateRoomAsync(
            roomName,
            maxPlayers,
            apiRoomInfo.RoomId,
            _targetSceneName,
            loadSceneOnSuccess: false);

        if (!success)
        {
            SetCreateButtonInteractable(true);
            SetStatusText(string.IsNullOrWhiteSpace(roomService.LastErrorMessage)
                ? "Photon room creation failed."
                : roomService.LastErrorMessage);
            return;
        }

        FusionRoomSessionInfo fusionContext = FusionRoomSessionContext.Current;
        string sessionName = fusionContext != null ? fusionContext.SessionName : string.Empty;

        if (_storeRoomContext)
        {
            NetworkRoomIdentity.ApplyRoomContext(
                apiRoomInfo.RoomId,
                sessionName,
                string.IsNullOrWhiteSpace(apiRoomInfo.Title) ? roomName : apiRoomInfo.Title,
                apiRoomInfo.OwnerUserId,
                apiRoomInfo.CreatedAtUtc);
        }

        SetStatusText("Photon/API room ready. Moving to network scene...");
        SetCreateButtonInteractable(!_moveToNetworkSceneOnChatRoomReady);

        if (_moveToNetworkSceneOnChatRoomReady && !string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    private async Task<ChatRoomCreateInfo> CreateApiRoomForPhotonAsync(string roomName, int maxPlayers)
    {
        if (_chatRoomManager == null)
            throw new InvalidOperationException("ChatRoomManager is null.");

        if (_chatRoomManager.IsBusy)
            throw new InvalidOperationException("ChatRoomManager is busy.");

        _isHybridCreateInProgress = true;
        _hybridApiCreateTcs = new TaskCompletionSource<ChatRoomCreateInfo>();

        try
        {
            _chatRoomManager.CreateRoom(roomName, maxPlayers.ToString());
            return await _hybridApiCreateTcs.Task;
        }
        finally
        {
            _hybridApiCreateTcs = null;
            _isHybridCreateInProgress = false;
        }
    }

    private void HandleRoomCreateStarted(string roomName)
    {
        SetCreateButtonInteractable(false);
        SetStatusText($"\"{roomName}\" room creating...");
    }

    private void HandleRoomCreateProgress(RoomProvisioningProgress progress)
    {
        if (progress == null)
            return;

        SetStatusText($"Server provisioning... ({progress.PollCount})");
    }

    private void HandleRoomReady(RoomInfo roomInfo)
    {
        SetCreateButtonInteractable(false);
        SetStatusText("Room ready. Moving to next scene...");
    }

    private void HandleRoomCreateFailed(RoomCreateError error)
    {
        SetCreateButtonInteractable(true);
        SetStatusText(error != null && !string.IsNullOrWhiteSpace(error.UserMessage)
            ? error.UserMessage
            : "Room creation failed.");
    }

    private void HandleRoomCreateCanceled()
    {
        SetCreateButtonInteractable(true);
        SetStatusText("Room creation canceled.");
    }

    private void HandleChatRoomCreateStarted(string roomTitle, int maxUserCount)
    {
        SetCreateButtonInteractable(false);

        if (_isHybridCreateInProgress)
        {
            SetStatusText($"\"{roomTitle}\" API room creating... (max {maxUserCount})");
            return;
        }

        SetStatusText($"\"{roomTitle}\" chat room creating... (max {maxUserCount})");
    }

    private void HandleChatRoomCreateSucceeded(ChatRoomCreateInfo roomInfo)
    {
        if (_isHybridCreateInProgress)
        {
            _hybridApiCreateTcs?.TrySetResult(roomInfo);
            return;
        }

        SetCreateButtonInteractable(!_moveToNetworkSceneOnChatRoomReady);

        string roomId = roomInfo != null ? roomInfo.RoomId : string.Empty;
        string title = roomInfo != null ? roomInfo.Title : string.Empty;
        string ownerUserId = roomInfo != null ? roomInfo.OwnerUserId : string.Empty;
        string createdAtUtc = roomInfo != null ? roomInfo.CreatedAtUtc : string.Empty;

        if (string.IsNullOrWhiteSpace(title))
            SetStatusText($"Chat room ready. id={roomId}");
        else
            SetStatusText($"\"{title}\" chat room ready. id={roomId}");

        if (!_moveToNetworkSceneOnChatRoomReady)
            return;

        if (_storeRoomContext)
        {
            NetworkRoomIdentity.ApplyRoomContext(
                roomId,
                photonSessionName: null,
                roomName: title,
                hostUserId: ownerUserId,
                createdAtUtc: createdAtUtc);
        }

        if (!string.IsNullOrWhiteSpace(_targetSceneName))
            SceneManager.LoadScene(_targetSceneName);
    }

    private void HandleChatRoomCreateFailed(string userMessage)
    {
        if (_isHybridCreateInProgress)
        {
            _hybridApiCreateTcs?.TrySetException(new InvalidOperationException(
                string.IsNullOrWhiteSpace(userMessage)
                    ? "API room creation failed."
                    : userMessage));
            return;
        }

        SetCreateButtonInteractable(true);
        SetStatusText(string.IsNullOrWhiteSpace(userMessage)
            ? "Chat room creation failed."
            : userMessage);
    }

    private void HandleChatRoomCreateCanceled()
    {
        if (_isHybridCreateInProgress)
        {
            _hybridApiCreateTcs?.TrySetCanceled();
            return;
        }

        SetCreateButtonInteractable(true);
        SetStatusText("Chat room creation canceled.");
    }

    private void SetCreateButtonInteractable(bool interactable)
    {
        if (_createRoomButton != null)
            _createRoomButton.interactable = interactable;
    }

    private bool TryReadCreateInputs(out string roomName, out string maxUserCountRaw)
    {
        roomName = _roomNameInputField != null ? _roomNameInputField.text : string.Empty;
        maxUserCountRaw = _maxUserCountInputField != null ? _maxUserCountInputField.text : string.Empty;

        roomName = string.IsNullOrWhiteSpace(roomName) ? string.Empty : roomName.Trim();
        maxUserCountRaw = string.IsNullOrWhiteSpace(maxUserCountRaw) ? string.Empty : maxUserCountRaw.Trim();

        if (string.IsNullOrWhiteSpace(roomName))
        {
            SetStatusText("Enter a room name.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(maxUserCountRaw))
        {
            SetStatusText("Enter a max player count.");
            return false;
        }

        return true;
    }

    private void SetStatusText(string message)
    {
        if (_statusText != null)
            _statusText.text = message ?? string.Empty;
    }

    private void SetRoomInfoVisible(bool isVisible)
    {
        if (_roomInfoUI != null)
            _roomInfoUI.SetActive(isVisible);
    }
}
