using System;
using System.Threading.Tasks;
using MG_BlocksEngine2.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HostBlockShareSaveToMyLevelButton : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private HostBlockShareAutoRefreshPanel _sourcePanel;
    [SerializeField] private Button _saveButton;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private TMP_Text _resultBoolText;

    [Header("Option")]
    [SerializeField] private string _tokenOverride = string.Empty;
    [SerializeField] private bool _disableButtonWhileSaving = true;
    [SerializeField] private bool _disableButtonAfterSuccess = true;
    [SerializeField] private bool _refreshListAfterSave = true;
    [SerializeField] private bool _debugLog = true;

    private ChatRoomManager _boundManager;
    private bool _isSaving;
    private string _activeShareId = string.Empty;
    private string _activeFileNameHint = string.Empty;
    private int _activeUserLevelSeqHint;

    public bool HasSaveResult { get; private set; }
    public bool LastSaveResult { get; private set; }

    private void OnEnable()
    {
        if (_saveButton == null)
            _saveButton = GetComponent<Button>();

        if (_saveButton != null)
        {
            _saveButton.onClick.RemoveListener(OnClickSaveToMyLevel);
            _saveButton.onClick.AddListener(OnClickSaveToMyLevel);
        }

        TryBindManagerEvents();
        UpdateResultBoolText();
        UpdateButtonInteractable();
    }

    private void OnDisable()
    {
        if (_saveButton != null)
            _saveButton.onClick.RemoveListener(OnClickSaveToMyLevel);

        UnbindManagerEvents();
        _isSaving = false;
        _activeShareId = string.Empty;
        _activeFileNameHint = string.Empty;
        _activeUserLevelSeqHint = 0;
        HasSaveResult = false;
        LastSaveResult = false;
        UpdateResultBoolText();
        UpdateButtonInteractable();
    }

    public void OnClickSaveToMyLevel()
    {
        TryBindManagerEvents();
        if (_boundManager == null)
        {
            SetStatus("ChatRoomManager is null.");
            return;
        }

        if (_sourcePanel == null)
        {
            SetStatus("Source panel is not assigned.");
            return;
        }

        string shareId = _sourcePanel.SelectedShareId;
        if (string.IsNullOrWhiteSpace(shareId))
        {
            SetStatus("Select one block share first.");
            return;
        }

        if (_boundManager.IsBusy)
        {
            SetStatus("ChatRoomManager is busy.");
            return;
        }

        _activeShareId = shareId.Trim();
        CaptureActiveSelectionHints(_activeShareId);
        _isSaving = true;
        UpdateButtonInteractable();

        _boundManager.SaveBlockShareToMyLevel(_activeShareId, ResolveTokenOverride());
        SetStatus($"Save requested. shareId={_activeShareId}");
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
        _boundManager.OnBlockShareSaveSucceeded += HandleSaveSucceeded;
        _boundManager.OnBlockShareSaveFailed += HandleSaveFailed;
        _boundManager.OnBlockShareSaveCanceled += HandleSaveCanceled;
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnBlockShareSaveSucceeded -= HandleSaveSucceeded;
        _boundManager.OnBlockShareSaveFailed -= HandleSaveFailed;
        _boundManager.OnBlockShareSaveCanceled -= HandleSaveCanceled;
        _boundManager = null;
    }

    private void HandleSaveSucceeded(ChatRoomBlockShareSaveInfo info)
    {
        if (!_isSaving)
            return;

        string shareId = info != null ? info.ShareId : string.Empty;
        if (!IsActiveShare(shareId))
            return;

        _isSaving = false;
        SetSaveResult(true);
        UpdateButtonInteractable();

        SetStatus($"Save success. shareId={shareId}, savedSeq={info?.SavedUserLevelSeq}");
        _ = DebugLogSavedBlockCodeDataAsync(info);
        if (_refreshListAfterSave && _sourcePanel != null)
            _sourcePanel.RequestListNow();
    }

    private void HandleSaveFailed(string shareId, string message)
    {
        if (!_isSaving)
            return;

        if (!IsActiveShare(shareId))
            return;

        _isSaving = false;
        SetSaveResult(false);
        UpdateButtonInteractable();
        SetStatus($"Save failed. shareId={shareId}, message={message}");
    }

    private void HandleSaveCanceled(string shareId)
    {
        if (!_isSaving)
            return;

        if (!IsActiveShare(shareId))
            return;

        _isSaving = false;
        SetSaveResult(false);
        UpdateButtonInteractable();
        SetStatus($"Save canceled. shareId={shareId}");
    }

    private bool IsActiveShare(string shareId)
    {
        return string.Equals(_activeShareId, shareId ?? string.Empty, StringComparison.Ordinal);
    }

    private void CaptureActiveSelectionHints(string shareId)
    {
        _activeFileNameHint = string.Empty;
        _activeUserLevelSeqHint = 0;

        if (_sourcePanel == null)
            return;

        ChatRoomBlockShareInfo detail = _sourcePanel.SelectedDetailInfo;
        if (detail != null && string.Equals(shareId, detail.BlockShareId ?? string.Empty, StringComparison.Ordinal))
        {
            _activeUserLevelSeqHint = detail.UserLevelSeq;
            if (!string.IsNullOrWhiteSpace(detail.Message))
                _activeFileNameHint = detail.Message.Trim();

            if (_debugLog)
                Debug.Log($"[HostBlockShareSaveToMyLevelButton] Selection linked(detail). shareId={shareId}, userLevelSeqHint={_activeUserLevelSeqHint}, fileNameHint={_activeFileNameHint}");
            return;
        }

        if (detail != null && _debugLog)
            Debug.LogWarning($"[HostBlockShareSaveToMyLevelButton] Selected detail is stale. selectedShareId={shareId}, detailShareId={detail.BlockShareId}");

        if (_sourcePanel.TryGetSelectedListItemInfo(out string listMessage, out int listUserLevelSeq))
        {
            _activeUserLevelSeqHint = listUserLevelSeq;
            if (!string.IsNullOrWhiteSpace(listMessage))
                _activeFileNameHint = listMessage.Trim();

            if (_debugLog)
                Debug.Log($"[HostBlockShareSaveToMyLevelButton] Selection linked(list). shareId={shareId}, userLevelSeqHint={_activeUserLevelSeqHint}, fileNameHint={_activeFileNameHint}");
        }
    }

    private async Task DebugLogSavedBlockCodeDataAsync(ChatRoomBlockShareSaveInfo info)
    {
        if (!_debugLog)
            return;

        BE2_CodeStorageManager storage = BE2_CodeStorageManager.Instance;
        if (storage == null)
        {
            Debug.LogWarning("[HostBlockShareSaveToMyLevelButton] BE2_CodeStorageManager is null. skip XML/JSON debug load.");
            return;
        }

        string fileName = ResolveSavedFileName(info);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            Debug.LogWarning("[HostBlockShareSaveToMyLevelButton] Saved file name hint is empty. skip XML/JSON debug load.");
            return;
        }

        try
        {
            string xml = await storage.LoadXmlAsync(fileName);
            string json = await storage.LoadJsonAsync(fileName);

            LogGreen(
                $"Save payload debug loaded. shareId={info?.ShareId}, savedSeq={info?.SavedUserLevelSeq}, fileName={fileName}, xmlLen={(xml ?? string.Empty).Length}, jsonLen={(json ?? string.Empty).Length}");
            LogGreen($"Saved XML:\n{TruncateForDebug(xml)}");
            LogGreen($"Saved JSON:\n{TruncateForDebug(json)}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[HostBlockShareSaveToMyLevelButton] Save payload debug load failed. fileName={fileName}, error={ex.Message}");
        }
    }

    private string ResolveSavedFileName(ChatRoomBlockShareSaveInfo info)
    {
        if (!string.IsNullOrWhiteSpace(_activeFileNameHint))
            return _activeFileNameHint.Trim();

        if (info != null && info.SavedUserLevelSeq > 0)
            return info.SavedUserLevelSeq.ToString();

        if (_activeUserLevelSeqHint > 0)
            return _activeUserLevelSeqHint.ToString();

        ChatRoomBlockShareInfo selectedDetail = _sourcePanel != null ? _sourcePanel.SelectedDetailInfo : null;
        if (selectedDetail != null)
        {
            if (!string.IsNullOrWhiteSpace(selectedDetail.Message))
                return selectedDetail.Message.Trim();

            if (selectedDetail.UserLevelSeq > 0)
                return selectedDetail.UserLevelSeq.ToString();
        }

        return null;
    }

    private static string TruncateForDebug(string value, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty)";

        string text = value.Trim();
        if (text.Length <= maxLength)
            return text;

        return $"{text.Substring(0, maxLength)}...(truncated, len={text.Length})";
    }

    private static void LogGreen(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        Debug.Log($"<color=#00FF66>[HostBlockShareSaveToMyLevelButton] {text}</color>");
    }

    private void UpdateButtonInteractable()
    {
        if (_saveButton == null)
            return;

        bool interactable = true;

        if (_disableButtonWhileSaving && _isSaving)
            interactable = false;

        if (_disableButtonAfterSuccess && HasSaveResult && LastSaveResult)
            interactable = false;

        _saveButton.interactable = interactable;
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_tokenOverride) ? null : _tokenOverride.Trim();
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;

        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[HostBlockShareSaveToMyLevelButton] {text}");
    }

    private void SetSaveResult(bool success)
    {
        HasSaveResult = true;
        LastSaveResult = success;
        UpdateResultBoolText();
    }

    private void UpdateResultBoolText()
    {
        if (_resultBoolText == null)
            return;

        string value = HasSaveResult
            ? (LastSaveResult ? "True" : "False")
            : "-";

        _resultBoolText.text = $"Save Result: {value}";
    }
}
