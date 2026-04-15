using System;
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

    private ChatRoomJoinRequestInfo _request;
    private Action<ChatRoomJoinRequestInfo> _onAccept;
    private Action<ChatRoomJoinRequestInfo> _onReject;

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

        _request = request;
        _onAccept = onAccept;
        _onReject = onReject;

        UpdateUserIdText();
        BindButtons();
        SetInteractable(true);
    }

    public void SetInteractable(bool interactable)
    {
        bool hasRequestId = _request != null && !string.IsNullOrWhiteSpace(_request.RequestId);
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

        string userId = _request != null && !string.IsNullOrWhiteSpace(_request.RequestUserId)
            ? _request.RequestUserId.Trim()
            : _unknownUserLabel;

        _userIdText.text = userId;
    }

    private void HandleAcceptClicked()
    {
        if (_request == null)
            return;

        _onAccept?.Invoke(_request);
    }

    private void HandleRejectClicked()
    {
        if (_request == null)
            return;

        _onReject?.Invoke(_request);
    }
}
