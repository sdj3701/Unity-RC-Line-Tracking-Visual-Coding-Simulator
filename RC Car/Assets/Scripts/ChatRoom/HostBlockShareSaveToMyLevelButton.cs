using System;
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
