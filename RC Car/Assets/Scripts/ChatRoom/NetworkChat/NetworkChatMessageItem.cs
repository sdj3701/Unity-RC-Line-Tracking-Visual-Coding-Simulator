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
        if (_messageText != null)
        {
            _messageText.text = text;
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

        bool shouldShowSenderName = !isMine && showSenderName && !string.IsNullOrWhiteSpace(message.SenderName);
        if (_senderNameText != null)
        {
            _senderNameText.gameObject.SetActive(shouldShowSenderName);
            _senderNameText.text = shouldShowSenderName ? message.SenderName.Trim() : string.Empty;
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
        ApplyBubbleWidth(text);
    }

    private void AutoBindReferences()
    {
        if (_rowRoot == null)
            _rowRoot = transform as RectTransform;

        if (_rowLayoutGroup == null && _rowRoot != null)
            _rowLayoutGroup = _rowRoot.GetComponent<HorizontalLayoutGroup>();

        if (_rowLayoutGroup == null)
            _rowLayoutGroup = GetComponentInChildren<HorizontalLayoutGroup>(true);

        if (_rowLayoutGroup != null)
            _rowRoot = _rowLayoutGroup.transform as RectTransform;

        if (_rowLayoutGroup == null && _rowRoot != null)
        {
            _rowLayoutGroup = _rowRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            _rowLayoutGroup.spacing = 4f;
            _rowLayoutGroup.childControlWidth = true;
            _rowLayoutGroup.childControlHeight = true;
            _rowLayoutGroup.childForceExpandWidth = false;
            _rowLayoutGroup.childForceExpandHeight = false;
            LogDebug("HorizontalLayoutGroup was missing and has been added at runtime.");
        }

        if (_messageText == null)
            _messageText = GetComponentInChildren<TMP_Text>(true);

        if (_bubbleRoot == null && _messageText != null)
            _bubbleRoot = _messageText.transform.parent as RectTransform;

        if (_bubbleImage == null && _bubbleRoot != null)
            _bubbleImage = _bubbleRoot.GetComponent<Image>();

        if (_bubbleImage == null && _messageText != null)
            _bubbleImage = FindClosestImage(_messageText.transform);

        if (_bubbleImage == null)
            _bubbleImage = GetComponentInChildren<Image>(true);

        if (_bubbleImage != null &&
            (_bubbleRoot == null || _bubbleRoot.GetComponent<Image>() == null))
        {
            _bubbleRoot = _bubbleImage.transform as RectTransform;
        }

        if (_bubbleImage == null && _bubbleRoot != null)
        {
            _bubbleImage = _bubbleRoot.gameObject.AddComponent<Image>();
            _bubbleImage.raycastTarget = false;
            LogDebug("Bubble Image was missing and has been added at runtime.");
        }

        if (_bubbleLayoutElement == null && _bubbleRoot != null)
            _bubbleLayoutElement = _bubbleRoot.GetComponent<LayoutElement>();

        if (_bubbleLayoutElement == null && _bubbleRoot != null)
            _bubbleLayoutElement = _bubbleRoot.gameObject.AddComponent<LayoutElement>();

        if (_timeText == null)
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i] != _messageText)
                {
                    _timeText = texts[i];
                    break;
                }
            }
        }
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
