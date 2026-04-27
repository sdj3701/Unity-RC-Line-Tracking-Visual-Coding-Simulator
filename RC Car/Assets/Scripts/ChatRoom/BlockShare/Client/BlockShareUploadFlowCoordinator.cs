using System;
using System.Threading.Tasks;
using UnityEngine;

public class BlockShareUploadFlowCoordinator : MonoBehaviour
{
    [SerializeField] private LocalBlockCodeListPanel _localListPanel;
    [SerializeField] private LocalBlockCodeListController _localListController;
    [SerializeField] private BlockShareUploadButtonView _uploadButton;
    [SerializeField] private BlockShareRemoteListController _remoteListController;
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private bool _autoRoomFromSession = true;
    [SerializeField] private bool _closePanelOnUploadSuccess = true;
    [SerializeField] private bool _debugLog = true;

    private IRoomIdProvider _roomIdProvider;
    private IBlockShareUploadService _uploadService;
    private bool _isUploading;

    private void Awake()
    {
        if (_localListPanel == null)
            _localListPanel = GetComponent<LocalBlockCodeListPanel>();

        if (_localListController == null)
            _localListController = GetComponent<LocalBlockCodeListController>();

        _roomIdProvider = new FusionRoomIdProvider(_roomIdOverride, _autoRoomFromSession);
        _uploadService = new ChatRoomBlockShareUploadService();
    }

    private void OnEnable()
    {
        if (_uploadButton != null)
            _uploadButton.UploadClicked += HandleUploadClicked;

        if (_localListPanel != null)
            _localListPanel.SelectionChanged += UpdateUploadButtonState;

        UpdateUploadButtonState();
    }

    private void OnDisable()
    {
        if (_uploadButton != null)
            _uploadButton.UploadClicked -= HandleUploadClicked;

        if (_localListPanel != null)
            _localListPanel.SelectionChanged -= UpdateUploadButtonState;
    }

    private void HandleUploadClicked()
    {
        if (_isUploading)
            return;

        if (_localListPanel == null || !_localListPanel.TryGetSelectedEntry(out LocalBlockCodeEntry entry))
        {
            if (_localListPanel != null)
                _localListPanel.SetStatus("Select one item first.");
            return;
        }

        if (entry == null || entry.UserLevelSeq <= 0)
        {
            _localListPanel.SetStatus("Invalid userLevelSeq.");
            return;
        }

        string roomId = _roomIdProvider != null ? _roomIdProvider.GetRoomId() : string.Empty;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            _localListPanel.SetStatus("API roomId is empty.");
            return;
        }

        _ = UploadSelectedEntryAsync(entry, roomId);
    }

    private async Task UploadSelectedEntryAsync(LocalBlockCodeEntry entry, string roomId)
    {
        _isUploading = true;
        ApplyBusyState();

        try
        {
            BlockShareUploadResult result = await _uploadService.UploadAsync(new BlockShareUploadRequest
            {
                RoomId = roomId,
                UserLevelSeq = entry.UserLevelSeq,
                Message = entry.FileName
            });

            if (_localListPanel != null)
            {
                _localListPanel.SetStatus(
                    $"Upload completed: [{result.UserLevelSeq}] {result.Message}");
            }

            if (_closePanelOnUploadSuccess && _localListPanel != null)
                _localListPanel.ClosePanel();

            if (_remoteListController != null)
                _remoteListController.RequestRefresh();
        }
        catch (OperationCanceledException)
        {
            if (_localListPanel != null)
                _localListPanel.SetStatus("Upload canceled.");
        }
        catch (Exception e)
        {
            if (_localListPanel != null)
                _localListPanel.SetStatus($"Upload failed. ({e.Message})");
        }
        finally
        {
            _isUploading = false;
            ApplyBusyState();
        }
    }

    private void UpdateUploadButtonState()
    {
        if (_uploadButton == null)
            return;

        bool canUpload = _localListController != null &&
                         _localListController.TryGetSelectedEntry(out LocalBlockCodeEntry entry) &&
                         entry != null &&
                         entry.UserLevelSeq > 0;

        _uploadButton.SetInteractable(canUpload);
        _uploadButton.SetBusy(_isUploading);
    }

    private void ApplyBusyState()
    {
        UpdateUploadButtonState();

        if (_localListPanel != null)
            _localListPanel.SetBusy(_isUploading);

        if (_debugLog)
            Debug.Log($"[BlockShareUploadFlowCoordinator] Upload busy={_isUploading}");
    }
}
