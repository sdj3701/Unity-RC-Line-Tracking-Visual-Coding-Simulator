using System;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HostJoinRequestItemUI : MonoBehaviour
{
    [Header("Text")]
    [SerializeField] private TMP_Text _userIdText;
    [SerializeField] private string _unknownUserLabel = "Unknown User";

    [Header("Buttons")]
    [SerializeField] private Button _acceptButton;
    [SerializeField] private Button _rejectButton;

    private ChatRoomJoinRequestInfo _legacyRequest;
    private FusionPendingJoinRequestInfo _photonRequest;
    private Action<ChatRoomJoinRequestInfo> _onLegacyAccept;
    private Action<ChatRoomJoinRequestInfo> _onLegacyReject;
    private Action<string> _onPhotonAccept;
    private Action<string> _onPhotonReject;

    private void Awake()
    {
        ResolveReferencesIfMissing();
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    public void Configure(
        ChatRoomJoinRequestInfo request,
        Action<ChatRoomJoinRequestInfo> onAccept,
        Action<ChatRoomJoinRequestInfo> onReject)
    {
        ResolveReferencesIfMissing();
        UnbindButtons();

        _legacyRequest = request;
        _photonRequest = null;
        _onLegacyAccept = onAccept;
        _onLegacyReject = onReject;
        _onPhotonAccept = null;
        _onPhotonReject = null;

        UpdateUserIdText();
        BindButtons();
        SetInteractable(true);
    }

    public void ConfigurePhoton(
        FusionPendingJoinRequestInfo request,
        Action<string> onAccept,
        Action<string> onReject)
    {
        ResolveReferencesIfMissing();
        UnbindButtons();

        _legacyRequest = null;
        _photonRequest = request;
        _onLegacyAccept = null;
        _onLegacyReject = null;
        _onPhotonAccept = onAccept;
        _onPhotonReject = onReject;

        UpdateUserIdText();
        BindButtons();
        SetInteractable(true);
    }

    public void SetInteractable(bool interactable)
    {
        bool hasRequestId =
            (_legacyRequest != null && !string.IsNullOrWhiteSpace(_legacyRequest.RequestId)) ||
            (_photonRequest != null && !string.IsNullOrWhiteSpace(_photonRequest.RequestId));

        bool enabled = interactable && hasRequestId;

        if (_acceptButton != null)
            _acceptButton.interactable = enabled;

        if (_rejectButton != null)
            _rejectButton.interactable = enabled;
    }

    private void ResolveReferencesIfMissing()
    {
        if (_userIdText == null)
            _userIdText = GetComponentInChildren<TMP_Text>(true);

        if (_acceptButton != null && _rejectButton != null)
            return;

        Button[] buttons = GetComponentsInChildren<Button>(true);
        if (_acceptButton == null && buttons.Length > 0)
            _acceptButton = buttons[0];

        if (_rejectButton == null && buttons.Length > 1)
            _rejectButton = buttons[1];
    }

    private void BindButtons()
    {
        if (_acceptButton != null)
        {
            _acceptButton.onClick.RemoveListener(HandleAcceptClicked);
            _acceptButton.onClick.AddListener(HandleAcceptClicked);
        }

        if (_rejectButton != null)
        {
            _rejectButton.onClick.RemoveListener(HandleRejectClicked);
            _rejectButton.onClick.AddListener(HandleRejectClicked);
        }
    }

    private void UnbindButtons()
    {
        if (_acceptButton != null)
            _acceptButton.onClick.RemoveListener(HandleAcceptClicked);

        if (_rejectButton != null)
            _rejectButton.onClick.RemoveListener(HandleRejectClicked);
    }

    private void UpdateUserIdText()
    {
        if (_userIdText == null)
            return;

        string label = _unknownUserLabel;

        if (_photonRequest != null)
        {
            string userId = string.IsNullOrWhiteSpace(_photonRequest.UserId)
                ? _unknownUserLabel
                : _photonRequest.UserId.Trim();
            string displayName = string.IsNullOrWhiteSpace(_photonRequest.DisplayName)
                ? string.Empty
                : _photonRequest.DisplayName.Trim();

            label = string.IsNullOrWhiteSpace(displayName) || string.Equals(displayName, userId, StringComparison.Ordinal)
                ? userId
                : $"{userId} ({displayName})";
        }
        else if (_legacyRequest != null)
        {
            label = !string.IsNullOrWhiteSpace(_legacyRequest.RequestUserId)
                ? _legacyRequest.RequestUserId.Trim()
                : _unknownUserLabel;
        }

        _userIdText.text = label;
    }

    private void HandleAcceptClicked()
    {
        if (_photonRequest != null)
        {
            _onPhotonAccept?.Invoke(_photonRequest.RequestId);
            return;
        }

        if (_legacyRequest != null)
            _onLegacyAccept?.Invoke(_legacyRequest);
    }

    private void HandleRejectClicked()
    {
        if (_photonRequest != null)
        {
            _onPhotonReject?.Invoke(_photonRequest.RequestId);
            return;
        }

        if (_legacyRequest != null)
            _onLegacyReject?.Invoke(_legacyRequest);
    }
}
