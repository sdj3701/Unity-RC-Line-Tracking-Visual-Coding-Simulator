using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum NetworkChatBubbleSide
{
    Left,
    Right
}

[DisallowMultipleComponent]
public sealed class NetworkChatMessageItem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform _rowRoot;
    [SerializeField] private RectTransform _bubbleRoot;
    [SerializeField] private Image _bubbleImage;
    [SerializeField] private TMP_Text _messageText;
    [SerializeField] private TMP_Text _timeText;
    [SerializeField] private TMP_Text _senderNameText;
    [SerializeField] private HorizontalLayoutGroup _rowLayoutGroup;
    [SerializeField] private LayoutElement _bubbleLayoutElement;
    [SerializeField] private LayoutElement _senderLayoutElement;

    [Header("Style")]
    [SerializeField] private Color _mineBubbleColor = new Color(1f, 0.86f, 0.04f, 1f);
    [SerializeField] private Color _otherBubbleColor = Color.white;
    [SerializeField] private Color _messageTextColor = new Color(0.02f, 0.02f, 0.02f, 1f);
    [SerializeField] private Color _timeTextColor = new Color(0.16f, 0.22f, 0.28f, 1f);
    [SerializeField] private Color _senderNameTextColor = new Color(0.18f, 0.22f, 0.28f, 1f);
    [SerializeField] private bool _prefixSenderIdToMessage = true;

    [Header("Layout")]
    [SerializeField, Min(40f)] private float _minBubbleWidth = 48f;
    [SerializeField, Min(80f)] private float _maxBubbleWidth = 420f;
    [SerializeField, Range(0.2f, 1f)] private float _maxBubbleWidthRatio = 0.72f;
    [SerializeField, Min(0f)] private float _bubbleHorizontalPadding = 24f;
    [SerializeField] private bool _swapTimeSideByMessageOwner = true;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private void Reset()
    {
        AutoBindReferences();
    }

    private void Awake()
    {
        AutoBindReferences();
    }

    public void BindReferences(
        RectTransform rowRoot,
        RectTransform bubbleRoot,
        Image bubbleImage,
        TMP_Text messageText,
        TMP_Text timeText,
        TMP_Text senderNameText = null,
        HorizontalLayoutGroup rowLayoutGroup = null,
        LayoutElement bubbleLayoutElement = null,
        LayoutElement senderLayoutElement = null)
    {
        _rowRoot = rowRoot;
        _bubbleRoot = bubbleRoot;
        _bubbleImage = bubbleImage;
        _messageText = messageText;
        _timeText = timeText;
        _senderNameText = senderNameText;
        _rowLayoutGroup = rowLayoutGroup;
        _bubbleLayoutElement = bubbleLayoutElement;
        _senderLayoutElement = senderLayoutElement;
        AutoBindReferences();
    }

    public void SetData(
        NetworkChatMessage message,
        bool isMine,
        bool showSenderName,
        NetworkChatBubbleSide mineSide = NetworkChatBubbleSide.Right,
        NetworkChatBubbleSide otherSide = NetworkChatBubbleSide.Left)
    {
        AutoBindReferences();

        string text = string.IsNullOrWhiteSpace(message.Message) ? string.Empty : message.Message.Trim();
        string senderLabel = ResolveSenderLabel(message);
        string displayText = _prefixSenderIdToMessage && !string.IsNullOrWhiteSpace(senderLabel)
            ? $"[{senderLabel}] {text}"
            : text;

        if (_messageText != null)
        {
            _messageText.text = displayText;
            _messageText.alignment = TextAlignmentOptions.Left;
            _messageText.enableWordWrapping = true;
            _messageText.color = _messageTextColor;
        }

        if (_timeText != null)
        {
            _timeText.text = FormatKoreanAmPm(message.LocalTime);
            _timeText.color = _timeTextColor;
            _timeText.alignment = TextAlignmentOptions.Bottom;
        }

        bool shouldShowSenderName =
            showSenderName &&
            !_prefixSenderIdToMessage &&
            !string.IsNullOrWhiteSpace(senderLabel);
        if (_senderNameText != null)
        {
            _senderNameText.gameObject.SetActive(shouldShowSenderName);
            _senderNameText.text = shouldShowSenderName ? senderLabel : string.Empty;
            _senderNameText.color = _senderNameTextColor;
            _senderNameText.alignment = TextAlignmentOptions.Left;
        }

        if (_senderLayoutElement != null)
            _senderLayoutElement.ignoreLayout = !shouldShowSenderName;

        if (_bubbleImage != null)
            _bubbleImage.color = isMine ? _mineBubbleColor : _otherBubbleColor;
        else
            LogDebug("Bubble Image is missing. Bubble color cannot be applied.", warning: true);

        NetworkChatBubbleSide side = isMine ? mineSide : otherSide;
        LogDebug(
            $"SetData. isMine={isMine}, side={side}, " +
            $"rowLayout={_rowLayoutGroup != null}, bubbleImage={_bubbleImage != null}, " +
            $"bubbleLayout={_bubbleLayoutElement != null}, messageText={_messageText != null}");
        ApplySide(side);
        ApplyBubbleWidth(displayText);
    }

    private void AutoBindReferences()
    {
        RectTransform itemRect = transform as RectTransform;
        if (!HasUsableBubbleStructure(itemRect))
            BuildRuntimeMessageStructure(itemRect);

        BindExistingReferences();
    }

    private bool HasUsableBubbleStructure(RectTransform itemRect)
    {
        if (itemRect == null)
            return false;

        TMP_Text messageText = IsUsableMessageText(_messageText, itemRect)
            ? _messageText
            : FindTextByName("MessageText");
        RectTransform bubbleRoot = _bubbleRoot != null && _bubbleRoot != itemRect
            ? _bubbleRoot
            : (messageText != null ? messageText.transform.parent as RectTransform : null);
        Image bubbleImage = _bubbleImage != null && _bubbleImage.transform != transform
            ? _bubbleImage
            : (bubbleRoot != null ? bubbleRoot.GetComponent<Image>() : null);
        HorizontalLayoutGroup rowLayout = _rowLayoutGroup != null
            ? _rowLayoutGroup
            : GetComponentInChildren<HorizontalLayoutGroup>(true);

        return messageText != null &&
               bubbleRoot != null &&
               bubbleRoot != itemRect &&
               bubbleImage != null &&
               rowLayout != null;
    }

    private void BuildRuntimeMessageStructure(RectTransform itemRect)
    {
        if (itemRect == null)
            return;

        TMP_Text sourceText = _messageText != null ? _messageText : GetComponent<TMP_Text>();
        if (sourceText == null)
            sourceText = GetComponentInChildren<TMP_Text>(true);

        TMP_FontAsset sourceFont = sourceText != null ? sourceText.font : null;
        string templateText = sourceText != null && !string.IsNullOrEmpty(sourceText.text)
            ? sourceText.text
            : "Message";

        if (sourceText != null && sourceText.transform == transform)
            sourceText.enabled = false;

        LayoutElement itemLayout = GetOrAdd<LayoutElement>(gameObject);
        itemLayout.flexibleWidth = 1f;
        itemLayout.minHeight = 28f;

        VerticalLayoutGroup itemLayoutGroup = GetOrAdd<VerticalLayoutGroup>(gameObject);
        itemLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
        itemLayoutGroup.spacing = 0f;
        itemLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
        itemLayoutGroup.childControlWidth = true;
        itemLayoutGroup.childControlHeight = true;
        itemLayoutGroup.childForceExpandWidth = true;
        itemLayoutGroup.childForceExpandHeight = false;

        ContentSizeFitter itemFitter = GetOrAdd<ContentSizeFitter>(gameObject);
        itemFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        itemFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform rowRect = FindDirectChild(itemRect, "RowLayout");
        if (rowRect == null)
            rowRect = CreateRectChild(itemRect, "RowLayout");

        LayoutElement rowLayout = GetOrAdd<LayoutElement>(rowRect.gameObject);
        rowLayout.flexibleWidth = 1f;
        rowLayout.minHeight = 28f;

        HorizontalLayoutGroup rowLayoutGroup = GetOrAdd<HorizontalLayoutGroup>(rowRect.gameObject);
        rowLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
        rowLayoutGroup.spacing = 4f;
        rowLayoutGroup.childAlignment = TextAnchor.MiddleRight;
        rowLayoutGroup.childControlWidth = true;
        rowLayoutGroup.childControlHeight = true;
        rowLayoutGroup.childForceExpandWidth = false;
        rowLayoutGroup.childForceExpandHeight = false;

        ContentSizeFitter rowFitter = GetOrAdd<ContentSizeFitter>(rowRect.gameObject);
        rowFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform bubbleRect = FindDirectChild(rowRect, "Bubble");
        if (bubbleRect == null)
            bubbleRect = CreateRectChild(rowRect, "Bubble");

        Image bubbleImage = GetOrAdd<Image>(bubbleRect.gameObject);
        bubbleImage.raycastTarget = false;

        LayoutElement bubbleLayout = GetOrAdd<LayoutElement>(bubbleRect.gameObject);
        bubbleLayout.minWidth = _minBubbleWidth;
        bubbleLayout.preferredWidth = 120f;
        bubbleLayout.flexibleWidth = 0f;

        VerticalLayoutGroup bubbleLayoutGroup = GetOrAdd<VerticalLayoutGroup>(bubbleRect.gameObject);
        bubbleLayoutGroup.padding = new RectOffset(10, 10, 6, 6);
        bubbleLayoutGroup.spacing = 2f;
        bubbleLayoutGroup.childAlignment = TextAnchor.MiddleLeft;
        bubbleLayoutGroup.childControlWidth = true;
        bubbleLayoutGroup.childControlHeight = true;
        bubbleLayoutGroup.childForceExpandWidth = false;
        bubbleLayoutGroup.childForceExpandHeight = false;

        ContentSizeFitter bubbleFitter = GetOrAdd<ContentSizeFitter>(bubbleRect.gameObject);
        bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI senderText = FindTextByName("SenderNameText") as TextMeshProUGUI;
        if (senderText == null)
            senderText = CreateTextChild(bubbleRect, "SenderNameText", sourceFont, 12f, _senderNameTextColor);

        LayoutElement senderLayout = GetOrAdd<LayoutElement>(senderText.gameObject);
        senderLayout.ignoreLayout = true;
        senderText.gameObject.SetActive(false);

        TextMeshProUGUI messageText = FindTextByName("MessageText") as TextMeshProUGUI;
        if (messageText == null || messageText.transform == transform)
            messageText = CreateTextChild(bubbleRect, "MessageText", sourceFont, 18f, _messageTextColor);

        messageText.text = templateText;
        messageText.enableWordWrapping = true;
        messageText.raycastTarget = false;

        TextMeshProUGUI timeText = FindTextByName("TimeText") as TextMeshProUGUI;
        if (timeText == null)
            timeText = CreateTextChild(rowRect, "TimeText", sourceFont, 12f, _timeTextColor);

        LayoutElement timeLayout = GetOrAdd<LayoutElement>(timeText.gameObject);
        timeLayout.minWidth = 58f;
        timeLayout.flexibleWidth = 0f;
        timeText.raycastTarget = false;

        _rowRoot = rowRect;
        _bubbleRoot = bubbleRect;
        _bubbleImage = bubbleImage;
        _messageText = messageText;
        _timeText = timeText;
        _senderNameText = senderText;
        _rowLayoutGroup = rowLayoutGroup;
        _bubbleLayoutElement = bubbleLayout;
        _senderLayoutElement = senderLayout;

        LogDebug("Runtime chat message layout was created from a simple prefab.");
    }

    private void BindExistingReferences()
    {
        RectTransform itemRect = transform as RectTransform;

        if (_rowLayoutGroup == null)
            _rowLayoutGroup = GetComponentInChildren<HorizontalLayoutGroup>(true);

        if (_rowLayoutGroup != null)
            _rowRoot = _rowLayoutGroup.transform as RectTransform;

        if (!IsUsableMessageText(_messageText, itemRect))
            _messageText = FindTextByName("MessageText") ?? FindFirstUsableText(itemRect);

        if (_senderNameText == null)
            _senderNameText = FindTextByName("SenderNameText");

        if (_timeText == null)
            _timeText = FindTextByName("TimeText") ?? FindFirstTextExcept(_messageText, _senderNameText);

        if (_bubbleRoot == null || _bubbleRoot == itemRect)
            _bubbleRoot = _messageText != null ? _messageText.transform.parent as RectTransform : null;

        if (_bubbleRoot == itemRect)
            _bubbleRoot = null;

        if ((_bubbleImage == null || _bubbleImage.transform == transform) && _bubbleRoot != null)
            _bubbleImage = _bubbleRoot.GetComponent<Image>();

        if (_bubbleImage == null)
            _bubbleImage = FindFirstChildImage();

        if (_bubbleImage != null && (_bubbleRoot == null || _bubbleRoot.GetComponent<Image>() == null))
            _bubbleRoot = _bubbleImage.transform as RectTransform;

        if (_bubbleImage == null && _bubbleRoot != null)
        {
            _bubbleImage = GetOrAdd<Image>(_bubbleRoot.gameObject);
            _bubbleImage.raycastTarget = false;
            LogDebug("Bubble Image was missing and has been added to Bubble at runtime.");
        }

        if (_bubbleLayoutElement == null && _bubbleRoot != null)
            _bubbleLayoutElement = GetOrAdd<LayoutElement>(_bubbleRoot.gameObject);

        if (_senderLayoutElement == null && _senderNameText != null)
            _senderLayoutElement = GetOrAdd<LayoutElement>(_senderNameText.gameObject);
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static RectTransform FindDirectChild(RectTransform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == childName)
                return child as RectTransform;
        }

        return null;
    }

    private static RectTransform CreateRectChild(RectTransform parent, string childName)
    {
        GameObject childObject = new GameObject(childName, typeof(RectTransform));
        RectTransform rect = childObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0f, rect.sizeDelta.y);
        return rect;
    }

    private static TextMeshProUGUI CreateTextChild(
        RectTransform parent,
        string childName,
        TMP_FontAsset font,
        float fontSize,
        Color color)
    {
        GameObject textObject = new GameObject(childName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;

        text.text = string.Empty;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private TMP_Text FindTextByName(string textName)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == textName)
                return texts[i];
        }

        return null;
    }

    private TMP_Text FindFirstUsableText(RectTransform itemRect)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (IsUsableMessageText(texts[i], itemRect))
                return texts[i];
        }

        return null;
    }

    private TMP_Text FindFirstTextExcept(params TMP_Text[] excluded)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text candidate = texts[i];
            if (candidate == null)
                continue;

            bool isExcluded = false;
            if (excluded != null)
            {
                for (int j = 0; j < excluded.Length; j++)
                {
                    if (candidate == excluded[j])
                    {
                        isExcluded = true;
                        break;
                    }
                }
            }

            if (!isExcluded)
                return candidate;
        }

        return null;
    }

    private Image FindFirstChildImage()
    {
        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].transform != transform)
                return images[i];
        }

        return null;
    }

    private bool IsUsableMessageText(TMP_Text text, RectTransform itemRect)
    {
        return text != null &&
               text.transform != transform &&
               (itemRect == null || text.transform.parent != itemRect);
    }

    private static string ResolveSenderLabel(NetworkChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.SenderUserId))
            return message.SenderUserId.Trim();

        if (!string.IsNullOrWhiteSpace(message.SenderName))
            return message.SenderName.Trim();

        return message.Sender.ToString();
    }

    private void ApplySide(NetworkChatBubbleSide side)
    {
        if (_rowLayoutGroup != null)
        {
            _rowLayoutGroup.childAlignment = side == NetworkChatBubbleSide.Right
                ? TextAnchor.MiddleRight
                : TextAnchor.MiddleLeft;
        }

        RectTransform row = _rowRoot != null ? _rowRoot : transform as RectTransform;
        if (row != null)
        {
            row.anchorMin = new Vector2(0f, row.anchorMin.y);
            row.anchorMax = new Vector2(1f, row.anchorMax.y);
            row.pivot = new Vector2(0.5f, row.pivot.y);

            LayoutElement rowLayoutElement = row.GetComponent<LayoutElement>();
            if (rowLayoutElement == null)
                rowLayoutElement = row.gameObject.AddComponent<LayoutElement>();

            rowLayoutElement.flexibleWidth = 1f;
        }

        if (_swapTimeSideByMessageOwner && _bubbleRoot != null && _timeText != null)
        {
            int bubbleIndex = side == NetworkChatBubbleSide.Right ? 1 : 0;
            int timeIndex = side == NetworkChatBubbleSide.Right ? 0 : 1;
            _timeText.transform.SetSiblingIndex(timeIndex);
            _bubbleRoot.SetSiblingIndex(bubbleIndex);
        }
    }

    private void ApplyBubbleWidth(string message)
    {
        if (_bubbleLayoutElement == null || _messageText == null)
            return;

        float maxWidth = ResolveMaxBubbleWidth();
        float availableTextWidth = Mathf.Max(20f, maxWidth - Mathf.Max(0f, _bubbleHorizontalPadding));
        Vector2 preferred = _messageText.GetPreferredValues(message, availableTextWidth, 0f);
        float preferredWidth = preferred.x + Mathf.Max(0f, _bubbleHorizontalPadding);

        _bubbleLayoutElement.preferredWidth = Mathf.Clamp(
            preferredWidth,
            Mathf.Min(_minBubbleWidth, maxWidth),
            maxWidth);
        _bubbleLayoutElement.flexibleWidth = 0f;
    }

    private float ResolveMaxBubbleWidth()
    {
        float maxWidth = Mathf.Max(_minBubbleWidth, _maxBubbleWidth);
        Transform parent = _rowRoot != null ? _rowRoot.parent : transform.parent;
        RectTransform parentRect = parent as RectTransform;
        if (parentRect == null || parentRect.rect.width <= 0f)
            return maxWidth;

        float ratioWidth = parentRect.rect.width * Mathf.Clamp01(_maxBubbleWidthRatio);
        return Mathf.Clamp(ratioWidth, _minBubbleWidth, maxWidth);
    }

    private static string FormatKoreanAmPm(System.DateTime time)
    {
        string ampm = time.Hour < 12 ? "오전" : "오후";
        int hour = time.Hour % 12;
        if (hour == 0)
            hour = 12;

        return $"{ampm} {hour}:{time.Minute:00}";
    }

    private Image FindClosestImage(Transform start)
    {
        Transform current = start;
        while (current != null && current != transform.parent)
        {
            Image image = current.GetComponent<Image>();
            if (image != null)
                return image;

            if (current == transform)
                break;

            current = current.parent;
        }

        return null;
    }

    private void LogDebug(string message, bool warning = false)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        if (warning)
            Debug.LogWarning($"[NetworkChatMessageItem] {message}", this);
        else
            Debug.Log($"[NetworkChatMessageItem] {message}", this);
    }
}
