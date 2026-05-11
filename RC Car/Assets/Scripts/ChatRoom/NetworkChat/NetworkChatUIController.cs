using System.Collections;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class NetworkChatUIController : MonoBehaviour
{
    [Header("Network")]
    [SerializeField] private NetworkChatManager _chatManager;
    [SerializeField] private bool _autoResolveChatManager = true;

    [Header("UI References")]
    [SerializeField] private ScrollRect _scrollRect;
    [SerializeField] private RectTransform _contentRoot;
    [SerializeField] private NetworkChatMessageItem _messageItemPrefab;
    [SerializeField] private TMP_InputField _messageInput;
    [SerializeField] private Button _sendButton;
    [SerializeField] private TMP_Text _statusText;

    [Header("Message Layout")]
    [SerializeField] private NetworkChatBubbleSide _mineBubbleSide = NetworkChatBubbleSide.Right;
    [SerializeField] private NetworkChatBubbleSide _otherBubbleSide = NetworkChatBubbleSide.Left;
    [SerializeField] private bool _showSenderNameForOthers;
    [SerializeField, Min(0)] private int _maxVisibleMessages = 100;

    [Header("Input")]
    [SerializeField] private bool _sendOnSubmit = true;
    [SerializeField] private bool _clearInputAfterSend = true;
    [SerializeField] private bool _refocusInputAfterSend = true;
    [SerializeField] private bool _trimInput = true;

    [Header("Scroll")]
    [SerializeField] private bool _autoScrollToBottom = true;
    [SerializeField] private bool _forceScrollOnlyWhenAlreadyNearBottom = false;
    [SerializeField, Range(0f, 0.2f)] private float _nearBottomThreshold = 0.04f;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private bool _isBound;
    private Coroutine _scrollCoroutine;

    private void Reset()
    {
        AutoBindSceneReferences();
    }

    private void Awake()
    {
        AutoBindSceneReferences();
    }

    private void OnEnable()
    {
        AutoBindSceneReferences();
        BindInputEvents();
        BindChatManager();
        ReplayExistingHistory();
        RefreshSendInteractable();
    }

    private void OnDisable()
    {
        UnbindChatManager();
        UnbindInputEvents();
    }

    private void Update()
    {
        if (!_autoResolveChatManager)
        {
            RefreshSendInteractable();
            return;
        }

        if (_chatManager == null)
            BindChatManager();

        RefreshSendInteractable();
    }

    public void OnSubmitMessage()
    {
        string raw = _messageInput != null ? _messageInput.text : string.Empty;
        string text = _trimInput && raw != null ? raw.Trim() : raw;
        LogDebug(
            $"Send button submit. rawLength={(raw != null ? raw.Length : 0)}, " +
            $"trimmedLength={(text != null ? text.Length : 0)}, " +
            $"hasInput={_messageInput != null}, hasButton={_sendButton != null}, " +
            $"hasManager={_chatManager != null}, managerReady={(_chatManager != null && _chatManager.IsReady)}");

        if (string.IsNullOrWhiteSpace(text))
        {
            LogDebug("Send ignored. Message is empty or whitespace.", warning: true);
            SetStatus(string.Empty);
            return;
        }

        if (!ResolveChatManager())
        {
            LogDebug("Send failed. NetworkChatManager could not be resolved.", warning: true);
            SetStatus("Network chat manager is not ready.");
            return;
        }

        bool sent = _chatManager.TrySendMessage(text, out string errorMessage);
        if (!sent)
        {
            LogDebug($"Send failed. reason={errorMessage}", warning: true);
            SetStatus(errorMessage);
            return;
        }

        LogDebug($"Send succeeded. textLength={text.Length}");
        SetStatus(string.Empty);

        if (_clearInputAfterSend && _messageInput != null)
            _messageInput.text = string.Empty;

        if (_refocusInputAfterSend && _messageInput != null)
            _messageInput.ActivateInputField();
    }

    public void AddMessage(NetworkChatMessage message)
    {
        if (_contentRoot == null || _messageItemPrefab == null)
        {
            SetStatus("Chat message prefab or content root is missing.");
            return;
        }

        NetworkChatMessageItem item = Instantiate(_messageItemPrefab, _contentRoot);
        item.gameObject.SetActive(true);
        bool isMine = IsMine(message.Sender);
        LogDebug($"Add message. sender={message.Sender}, isMine={isMine}, textLength={(message.Message != null ? message.Message.Length : 0)}");
        item.SetData(message, isMine, _showSenderNameForOthers, _mineBubbleSide, _otherBubbleSide);
        TrimVisibleMessages();
        QueueScrollToBottomIfNeeded();
    }

    public void ClearMessages()
    {
        if (_contentRoot == null)
            return;

        for (int i = _contentRoot.childCount - 1; i >= 0; i--)
            Destroy(_contentRoot.GetChild(i).gameObject);
    }

    public void ScrollToBottom()
    {
        if (_scrollRect == null)
            return;

        if (_scrollCoroutine != null)
            StopCoroutine(_scrollCoroutine);

        _scrollCoroutine = StartCoroutine(ScrollToBottomNextFrame());
    }

    private void AutoBindSceneReferences()
    {
        if (_scrollRect == null)
            _scrollRect = GetComponentInChildren<ScrollRect>(true);

        if (_contentRoot == null && _scrollRect != null)
            _contentRoot = _scrollRect.content;

        if (_messageInput == null)
            _messageInput = GetComponentInChildren<TMP_InputField>(true);

        if (_sendButton == null)
            _sendButton = GetComponentInChildren<Button>(true);

        if (_messageItemPrefab == null)
            _messageItemPrefab = GetComponentInChildren<NetworkChatMessageItem>(true);

        if (_messageItemPrefab == null && _contentRoot != null)
            _messageItemPrefab = CreateDefaultMessageItemTemplate(_contentRoot);

        if (_messageItemPrefab != null &&
            _contentRoot != null &&
            _messageItemPrefab.transform.IsChildOf(_contentRoot))
        {
            _messageItemPrefab.gameObject.SetActive(false);
        }
    }

    private void BindInputEvents()
    {
        if (_sendButton != null)
        {
            _sendButton.onClick.RemoveListener(OnSubmitMessage);
            _sendButton.onClick.AddListener(OnSubmitMessage);
            LogDebug($"Send button bound. button={_sendButton.name}, interactable={_sendButton.interactable}");
        }
        else
        {
            LogDebug("Send button bind skipped. _sendButton is null.", warning: true);
        }

        if (_messageInput != null && _sendOnSubmit)
        {
            _messageInput.onSubmit.RemoveListener(HandleInputSubmit);
            _messageInput.onSubmit.AddListener(HandleInputSubmit);
            LogDebug($"Input submit bound. input={_messageInput.name}");
        }
    }

    private void UnbindInputEvents()
    {
        if (_sendButton != null)
            _sendButton.onClick.RemoveListener(OnSubmitMessage);

        if (_messageInput != null)
            _messageInput.onSubmit.RemoveListener(HandleInputSubmit);
    }

    private void BindChatManager()
    {
        if (!ResolveChatManager() || _isBound)
            return;

        _chatManager.OnMessageReceived += HandleMessageReceived;
        _chatManager.OnStatusChanged += HandleStatusChanged;
        _isBound = true;
    }

    private void UnbindChatManager()
    {
        if (!_isBound || _chatManager == null)
            return;

        _chatManager.OnMessageReceived -= HandleMessageReceived;
        _chatManager.OnStatusChanged -= HandleStatusChanged;
        _isBound = false;
    }

    private bool ResolveChatManager()
    {
        if (_chatManager != null)
            return true;

        if (!_autoResolveChatManager)
            return false;

        _chatManager = NetworkChatManager.Instance;
        if (_chatManager == null)
            _chatManager = FindObjectOfType<NetworkChatManager>(true);

        return _chatManager != null;
    }

    private void ReplayExistingHistory()
    {
        if (!ResolveChatManager() || _chatManager.LocalHistory == null || _chatManager.LocalHistory.Count == 0)
            return;

        if (_contentRoot != null && _contentRoot.childCount > 0)
            return;

        for (int i = 0; i < _chatManager.LocalHistory.Count; i++)
            AddMessage(_chatManager.LocalHistory[i]);
    }

    private void HandleMessageReceived(NetworkChatMessage message)
    {
        AddMessage(message);
    }

    private void HandleStatusChanged(string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
            SetStatus(status);
    }

    private void HandleInputSubmit(string _)
    {
        if (_sendOnSubmit)
            OnSubmitMessage();
    }

    private bool IsMine(PlayerRef sender)
    {
        NetworkRunner runner = _chatManager != null ? _chatManager.ActiveRunner : null;
        return runner != null && sender == runner.LocalPlayer;
    }

    private void TrimVisibleMessages()
    {
        if (_contentRoot == null || _maxVisibleMessages <= 0)
            return;

        while (_contentRoot.childCount > _maxVisibleMessages)
            Destroy(_contentRoot.GetChild(0).gameObject);
    }

    private void QueueScrollToBottomIfNeeded()
    {
        if (!_autoScrollToBottom || _scrollRect == null)
            return;

        if (_forceScrollOnlyWhenAlreadyNearBottom &&
            _scrollRect.verticalNormalizedPosition > _nearBottomThreshold)
        {
            return;
        }

        ScrollToBottom();
    }

    private IEnumerator ScrollToBottomNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (_scrollRect != null)
            _scrollRect.verticalNormalizedPosition = 0f;

        _scrollCoroutine = null;
    }

    private void RefreshSendInteractable()
    {
        if (_sendButton == null)
            return;

        bool nextInteractable = _chatManager == null || _chatManager.IsReady;
        if (_sendButton.interactable != nextInteractable)
            LogDebug($"Send button interactable changed. value={nextInteractable}, hasManager={_chatManager != null}, managerReady={(_chatManager != null && _chatManager.IsReady)}");

        _sendButton.interactable = nextInteractable;
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        if (_statusText != null)
            _statusText.text = text;

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[NetworkChatUIController] {text}");
    }

    private NetworkChatMessageItem CreateDefaultMessageItemTemplate(RectTransform parent)
    {
        GameObject itemObject = new GameObject(
            "ChatMessageItemTemplate",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(NetworkChatMessageItem));
        RectTransform itemRect = itemObject.GetComponent<RectTransform>();
        itemRect.SetParent(parent, false);
        itemRect.anchorMin = new Vector2(0f, 1f);
        itemRect.anchorMax = new Vector2(1f, 1f);
        itemRect.pivot = new Vector2(0.5f, 1f);

        LayoutElement itemLayout = itemObject.GetComponent<LayoutElement>();
        itemLayout.flexibleWidth = 1f;
        itemLayout.minHeight = 28f;

        VerticalLayoutGroup itemLayoutGroup = itemObject.GetComponent<VerticalLayoutGroup>();
        itemLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
        itemLayoutGroup.spacing = 0f;
        itemLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
        itemLayoutGroup.childControlWidth = true;
        itemLayoutGroup.childControlHeight = true;
        itemLayoutGroup.childForceExpandWidth = true;
        itemLayoutGroup.childForceExpandHeight = false;

        ContentSizeFitter itemFitter = itemObject.GetComponent<ContentSizeFitter>();
        itemFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        itemFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject rowObject = new GameObject(
            "RowLayout",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter));
        RectTransform rowRect = rowObject.GetComponent<RectTransform>();
        rowRect.SetParent(itemRect, false);
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);

        LayoutElement rowLayout = rowObject.GetComponent<LayoutElement>();
        rowLayout.flexibleWidth = 1f;
        rowLayout.minHeight = 28f;

        HorizontalLayoutGroup rowLayoutGroup = rowObject.GetComponent<HorizontalLayoutGroup>();
        rowLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
        rowLayoutGroup.spacing = 4f;
        rowLayoutGroup.childAlignment = TextAnchor.MiddleRight;
        rowLayoutGroup.childControlWidth = true;
        rowLayoutGroup.childControlHeight = true;
        rowLayoutGroup.childForceExpandWidth = false;
        rowLayoutGroup.childForceExpandHeight = false;

        ContentSizeFitter rowFitter = rowObject.GetComponent<ContentSizeFitter>();
        rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject bubbleObject = new GameObject(
            "Bubble",
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        RectTransform bubbleRect = bubbleObject.GetComponent<RectTransform>();
        bubbleRect.SetParent(rowRect, false);

        Image bubbleImage = bubbleObject.GetComponent<Image>();
        bubbleImage.color = new Color(1f, 0.86f, 0.04f, 1f);
        bubbleImage.raycastTarget = false;

        LayoutElement bubbleLayout = bubbleObject.GetComponent<LayoutElement>();
        bubbleLayout.minWidth = 48f;
        bubbleLayout.preferredWidth = 120f;
        bubbleLayout.flexibleWidth = 0f;

        VerticalLayoutGroup bubbleLayoutGroup = bubbleObject.GetComponent<VerticalLayoutGroup>();
        bubbleLayoutGroup.padding = new RectOffset(10, 10, 6, 6);
        bubbleLayoutGroup.spacing = 0f;
        bubbleLayoutGroup.childAlignment = TextAnchor.MiddleLeft;
        bubbleLayoutGroup.childControlWidth = true;
        bubbleLayoutGroup.childControlHeight = true;
        bubbleLayoutGroup.childForceExpandWidth = false;
        bubbleLayoutGroup.childForceExpandHeight = false;

        ContentSizeFitter bubbleFitter = bubbleObject.GetComponent<ContentSizeFitter>();
        bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject messageObject = new GameObject("MessageText", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform messageRect = messageObject.GetComponent<RectTransform>();
        messageRect.SetParent(bubbleRect, false);
        TextMeshProUGUI messageText = messageObject.GetComponent<TextMeshProUGUI>();
        messageText.text = "Message";
        messageText.fontSize = 18f;
        messageText.color = new Color(0.02f, 0.02f, 0.02f, 1f);
        messageText.alignment = TextAlignmentOptions.Left;
        messageText.enableWordWrapping = true;
        messageText.raycastTarget = false;

        GameObject timeObject = new GameObject("TimeText", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        RectTransform timeRect = timeObject.GetComponent<RectTransform>();
        timeRect.SetParent(rowRect, false);
        LayoutElement timeLayout = timeObject.GetComponent<LayoutElement>();
        timeLayout.minWidth = 58f;
        timeLayout.flexibleWidth = 0f;

        TextMeshProUGUI timeText = timeObject.GetComponent<TextMeshProUGUI>();
        timeText.text = "오전 10:45";
        timeText.fontSize = 12f;
        timeText.color = new Color(0.16f, 0.22f, 0.28f, 1f);
        timeText.alignment = TextAlignmentOptions.BottomLeft;
        timeText.raycastTarget = false;

        NetworkChatMessageItem item = itemObject.GetComponent<NetworkChatMessageItem>();
        item.BindReferences(rowRect, bubbleRect, bubbleImage, messageText, timeText, null, rowLayoutGroup, bubbleLayout);
        itemObject.SetActive(false);

        LogDebug("Default ChatMessageItem template was created under Content Root.");
        return item;
    }

    private void LogDebug(string message, bool warning = false)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        if (warning)
            Debug.LogWarning($"[NetworkChatUIController] {message}");
        else
            Debug.Log($"[NetworkChatUIController] {message}");
    }
}
