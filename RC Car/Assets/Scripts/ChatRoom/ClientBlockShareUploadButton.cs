using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClientBlockShareUploadButton : MonoBehaviour
{
    [SerializeField] private Button _uploadButton;
    [SerializeField] private TMP_InputField _userLevelSeqInput;
    [SerializeField] private TMP_InputField _messageInput;
    [SerializeField] private TMP_InputField _tokenOverrideInput;
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private int _defaultUserLevelSeq = 1;
    [SerializeField] private string _defaultMessage = "이 블록코드로 진행해봐";
    [SerializeField] private bool _bindOnEnable = true;
    [SerializeField] private bool _disableButtonWhileUploading = true;
    [SerializeField] private bool _debugLog = true;

    private ChatRoomManager _boundManager;
    private bool _isUploading;
    private string _activeRoomId = string.Empty;
    private int _activeUserLevelSeq;

    private void OnEnable()
    {
        if (_uploadButton == null)
            _uploadButton = GetComponent<Button>();

        if (_bindOnEnable && _uploadButton != null)
        {
            _uploadButton.onClick.RemoveListener(OnClickUploadBlockShare);
            _uploadButton.onClick.AddListener(OnClickUploadBlockShare);
        }

        TryBindManagerEvents();
        UpdateButtonInteractable();
    }

    private void OnDisable()
    {
        if (_bindOnEnable && _uploadButton != null)
            _uploadButton.onClick.RemoveListener(OnClickUploadBlockShare);

        UnbindManagerEvents();
        _isUploading = false;
        UpdateButtonInteractable();
    }

    public void OnClickUploadBlockShare()
    {
        TryBindManagerEvents();
        if (_boundManager == null)
        {
            LogWarning("ChatRoomManager.Instance is null.");
            return;
        }

        if (_boundManager.IsBusy)
        {
            LogWarning("ChatRoomManager is busy.");
            return;
        }

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            LogWarning("RoomId is empty. Set RoomSessionContext or room override.");
            return;
        }

        if (!TryResolveUserLevelSeq(out int userLevelSeq))
        {
            LogWarning("userLevelSeq must be an integer >= 1.");
            return;
        }

        string message = ResolveMessage();
        string tokenOverride = ResolveTokenOverride();

        _activeRoomId = roomId;
        _activeUserLevelSeq = userLevelSeq;
        _isUploading = true;
        UpdateButtonInteractable();

        _boundManager.UploadBlockShare(roomId, userLevelSeq, message, tokenOverride);

        if (_debugLog)
            Debug.Log($"[ClientBlockShareUploadButton] Upload requested. roomId={roomId}, userLevelSeq={userLevelSeq}, messageLength={message.Length}");
    }

    public void ApplyDraft(int userLevelSeq, string message, string roomId = null)
    {
        if (userLevelSeq > 0)
        {
            _defaultUserLevelSeq = userLevelSeq;
            if (_userLevelSeqInput != null)
                _userLevelSeqInput.text = userLevelSeq.ToString();
        }

        if (message != null)
        {
            _defaultMessage = message;
            if (_messageInput != null)
                _messageInput.text = message;
        }

        if (!string.IsNullOrWhiteSpace(roomId))
            _roomIdOverride = roomId.Trim();

        if (_debugLog)
            Debug.Log($"[ClientBlockShareUploadButton] Draft applied. roomId={roomId}, userLevelSeq={userLevelSeq}, messageLength={(message ?? string.Empty).Length}");
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
        _boundManager.OnBlockShareUploadSucceeded += HandleBlockShareUploadSucceeded;
        _boundManager.OnBlockShareUploadFailed += HandleBlockShareUploadFailed;
        _boundManager.OnBlockShareUploadCanceled += HandleBlockShareUploadCanceled;
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnBlockShareUploadSucceeded -= HandleBlockShareUploadSucceeded;
        _boundManager.OnBlockShareUploadFailed -= HandleBlockShareUploadFailed;
        _boundManager.OnBlockShareUploadCanceled -= HandleBlockShareUploadCanceled;
        _boundManager = null;
    }

    private void HandleBlockShareUploadSucceeded(ChatRoomBlockShareUploadInfo info)
    {
        if (!_isUploading)
            return;

        string roomId = info != null ? info.RoomId : string.Empty;
        int userLevelSeq = info != null ? info.UserLevelSeq : 0;
        if (!IsActiveUploadTarget(roomId, userLevelSeq))
            return;

        _isUploading = false;
        UpdateButtonInteractable();

        if (_debugLog)
            Debug.Log($"[ClientBlockShareUploadButton] Upload success. roomId={roomId}, userLevelSeq={userLevelSeq}, blockShareId={info?.BlockShareId}, code={info?.ResponseCode}");
    }

    private void HandleBlockShareUploadFailed(string roomId, int userLevelSeq, string message)
    {
        if (!_isUploading)
            return;

        if (!IsActiveUploadTarget(roomId, userLevelSeq))
            return;

        _isUploading = false;
        UpdateButtonInteractable();
        LogWarning($"Upload failed. roomId={roomId}, userLevelSeq={userLevelSeq}, message={message}");
    }

    private void HandleBlockShareUploadCanceled(string roomId, int userLevelSeq)
    {
        if (!_isUploading)
            return;

        if (!IsActiveUploadTarget(roomId, userLevelSeq))
            return;

        _isUploading = false;
        UpdateButtonInteractable();
        LogWarning($"Upload canceled. roomId={roomId}, userLevelSeq={userLevelSeq}");
    }

    private bool IsActiveUploadTarget(string roomId, int userLevelSeq)
    {
        return string.Equals(_activeRoomId, roomId ?? string.Empty, StringComparison.Ordinal) &&
               _activeUserLevelSeq == userLevelSeq;
    }

    private void UpdateButtonInteractable()
    {
        if (_uploadButton == null)
            return;

        if (_disableButtonWhileUploading)
            _uploadButton.interactable = !_isUploading;
    }

    private string ResolveTargetRoomId()
    {
        if (!string.IsNullOrWhiteSpace(_roomIdOverride))
            return _roomIdOverride.Trim();

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom != null && !string.IsNullOrWhiteSpace(currentRoom.RoomId))
            return currentRoom.RoomId.Trim();

        return string.Empty;
    }

    private bool TryResolveUserLevelSeq(out int userLevelSeq)
    {
        userLevelSeq = _defaultUserLevelSeq;

        string raw = _userLevelSeqInput != null ? _userLevelSeqInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return userLevelSeq >= 1;

        if (!int.TryParse(raw.Trim(), out userLevelSeq))
            return false;

        return userLevelSeq >= 1;
    }

    private string ResolveMessage()
    {
        string inputMessage = _messageInput != null ? _messageInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(inputMessage))
            return string.IsNullOrWhiteSpace(_defaultMessage) ? string.Empty : _defaultMessage.Trim();

        return inputMessage.Trim();
    }

    private string ResolveTokenOverride()
    {
        if (_tokenOverrideInput == null)
            return null;

        string token = _tokenOverrideInput.text;
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private void LogWarning(string message)
    {
        if (!_debugLog)
            return;

        Debug.LogWarning($"[ClientBlockShareUploadButton] {message}");
    }
}
