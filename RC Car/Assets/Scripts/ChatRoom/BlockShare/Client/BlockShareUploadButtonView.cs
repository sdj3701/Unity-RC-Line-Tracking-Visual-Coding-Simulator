using System;
using UnityEngine;
using UnityEngine.UI;

public class BlockShareUploadButtonView : MonoBehaviour
{
    [SerializeField] private Button _uploadButton;
    [SerializeField] private bool _bindOnEnable = true;
    [SerializeField] private bool _disableButtonWhileBusy = true;
    [SerializeField] private bool _debugLog = true;

    private bool _isBusy;
    private bool _isInteractable = true;

    public event Action UploadClicked;

    private void Awake()
    {
        if (_uploadButton == null && _bindOnEnable)
            _uploadButton = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (_bindOnEnable && _uploadButton == null)
            _uploadButton = GetComponent<Button>();

        if (_bindOnEnable && _uploadButton != null)
        {
            _uploadButton.onClick.RemoveListener(HandleUploadClicked);
            _uploadButton.onClick.AddListener(HandleUploadClicked);
        }

        ApplyInteractable();
    }

    private void OnDisable()
    {
        if (_bindOnEnable && _uploadButton != null)
            _uploadButton.onClick.RemoveListener(HandleUploadClicked);

        _isBusy = false;
        ApplyInteractable();
    }

    public bool IsOwnedUploadButton(Button button)
    {
        return button != null && button == _uploadButton;
    }

    public void SetInteractable(bool interactable)
    {
        _isInteractable = interactable;
        ApplyInteractable();
    }

    public void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        ApplyInteractable();
    }

    private void HandleUploadClicked()
    {
        if (_debugLog)
            Debug.Log("[BlockShareUploadButtonView] Upload button clicked.");

        UploadClicked?.Invoke();
    }

    private void ApplyInteractable()
    {
        if (_uploadButton == null)
            return;

        bool interactable = _isInteractable;
        if (_disableButtonWhileBusy)
            interactable &= !_isBusy;

        _uploadButton.interactable = interactable;
    }
}
