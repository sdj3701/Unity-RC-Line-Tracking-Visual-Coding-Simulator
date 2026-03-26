using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth;
using MG_BlocksEngine2.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
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

        int savedSeq = info != null ? info.SavedUserLevelSeq : 0;
        string accessToken = ResolveTokenOverride();
        if (string.IsNullOrWhiteSpace(accessToken) && AuthManager.Instance != null)
            accessToken = AuthManager.Instance.GetAccessToken();

        if (savedSeq > 0 && !string.IsNullOrWhiteSpace(accessToken))
        {
            ChatUserLevelDebugResult verifyResult = await ChatUserLevelDebugApi.FetchBySeqAsync(savedSeq, accessToken);
            if (verifyResult != null && verifyResult.IsSuccess)
            {
                LogGreen(
                    $"DB verify by seq success. shareId={info?.ShareId}, savedSeq={savedSeq}, level={verifyResult.Level}, xmlLen={(verifyResult.Xml ?? string.Empty).Length}, jsonLen={(verifyResult.Json ?? string.Empty).Length}, code={verifyResult.ResponseCode}");
                LogGreen($"DB XML:\n{TruncateForDebug(verifyResult.Xml)}");
                LogGreen($"DB JSON:\n{TruncateForDebug(verifyResult.Json)}");
                return;
            }

            if (verifyResult != null)
            {
                LogGreen(
                    $"DB verify by seq failed. shareId={info?.ShareId}, savedSeq={savedSeq}, code={verifyResult.ResponseCode}, error={verifyResult.ErrorMessage}, body={TruncateForDebug(verifyResult.ResponseBody)}");
            }
        }

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

public static class ChatUserLevelDebugApi
{
    private const string UserLevelDetailEndpointTemplate = "http://ioteacher.com/api/user-level/{seq}";
    private static readonly string[] WrapperObjectKeys = { "data", "item", "result", "userLevel" };
    private static readonly string[] XmlKeys = { "xml", "xmlLongText", "xmlData", "xml_data" };
    private static readonly string[] JsonKeys = { "json", "jsonLongText", "jsonData", "json_data" };

    public static async Task<ChatUserLevelDebugResult> FetchBySeqAsync(
        int seq,
        string accessToken,
        int timeoutSeconds = 15)
    {
        var result = new ChatUserLevelDebugResult
        {
            Seq = seq
        };

        if (seq <= 0)
        {
            result.ErrorMessage = "seq must be >= 1.";
            return result;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            result.ErrorMessage = "accessToken is empty.";
            return result;
        }

        string endpoint = UserLevelDetailEndpointTemplate.Replace(
            "{seq}",
            UnityWebRequest.EscapeURL(seq.ToString()));
        result.RequestUrl = endpoint;

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, timeoutSeconds);
            request.SetRequestHeader("Authorization", $"Bearer {accessToken.Trim()}");
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequestAsyncOperation op = request.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            result.ResponseCode = request.responseCode;
            result.ResponseBody = body;

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                result.ErrorMessage = string.IsNullOrWhiteSpace(request.error)
                    ? "Network/DataProcessing error."
                    : request.error;
                return result;
            }

            bool httpSuccess = request.responseCode >= 200 && request.responseCode < 300;
            if (!httpSuccess)
            {
                result.ErrorMessage = $"HTTP {request.responseCode}";
                return result;
            }

            string trimmed = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
            string payload = ExtractPrimaryObjectPayload(trimmed);
            if (string.IsNullOrWhiteSpace(payload))
                payload = trimmed;

            result.Seq = FirstPositive(
                ParseJsonInt(payload, "seq"),
                ParseJsonInt(payload, "id"),
                ParseJsonInt(payload, "userLevelSeq"),
                seq);

            result.Level = FirstNonEmpty(
                ExtractJsonScalarAsString(payload, "level"),
                ExtractJsonScalarAsString(trimmed, "level"),
                string.Empty);

            result.Xml = FirstNonEmpty(
                ExtractFirstStringValueByKeys(payload, XmlKeys),
                ExtractFirstStringValueByKeys(trimmed, XmlKeys),
                string.Empty);

            result.Json = FirstNonEmpty(
                ExtractJsonPayloadByKeys(payload, JsonKeys),
                ExtractJsonPayloadByKeys(trimmed, JsonKeys),
                string.Empty);

            result.IsSuccess = true;
            return result;
        }
    }

    private static string ExtractPrimaryObjectPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        string trimmed = json.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            return trimmed;

        for (int i = 0; i < WrapperObjectKeys.Length; i++)
        {
            if (TryExtractRawJsonField(trimmed, WrapperObjectKeys[i], out string rawObject))
                return rawObject;
        }

        return trimmed;
    }

    private static string ExtractFirstStringValueByKeys(string json, string[] keys)
    {
        if (string.IsNullOrWhiteSpace(json) || keys == null || keys.Length == 0)
            return null;

        for (int i = 0; i < keys.Length; i++)
        {
            string value = ExtractJsonScalarAsString(json, keys[i]);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string ExtractJsonPayloadByKeys(string json, string[] keys)
    {
        if (string.IsNullOrWhiteSpace(json) || keys == null || keys.Length == 0)
            return null;

        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (TryExtractRawJsonField(json, key, out string rawJson))
                return rawJson.Trim();

            string scalar = ExtractJsonScalarAsString(json, key);
            if (string.IsNullOrWhiteSpace(scalar))
                continue;

            string scalarTrimmed = scalar.Trim();
            if (LooksLikeJson(scalarTrimmed))
                return scalarTrimmed;

            return scalarTrimmed;
        }

        return null;
    }

    private static bool TryExtractRawJsonField(string json, string fieldName, out string rawJson)
    {
        rawJson = null;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
            return false;

        Match match = Regex.Match(json, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*");
        if (!match.Success)
            return false;

        int start = match.Index + match.Length;
        while (start < json.Length && char.IsWhiteSpace(json[start]))
            start++;

        if (start >= json.Length)
            return false;

        char opener = json[start];
        if (opener != '{' && opener != '[')
            return false;

        char closer = opener == '{' ? '}' : ']';
        int end = FindMatchingBracket(json, start, opener, closer);
        if (end < start)
            return false;

        rawJson = json.Substring(start, end - start + 1);
        return true;
    }

    private static int FindMatchingBracket(string text, int startIndex, char openChar, char closeChar)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startIndex; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == openChar)
            {
                depth++;
                continue;
            }

            if (c != closeChar)
                continue;

            depth--;
            if (depth == 0)
                return i;
        }

        return -1;
    }

    private static bool LooksLikeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();
        return (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
               (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal));
    }

    private static int ParseJsonInt(string json, string key)
    {
        string raw = ExtractJsonScalarAsString(json, key);
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        if (int.TryParse(raw.Trim(), out int value))
            return value;

        return 0;
    }

    private static string ExtractJsonScalarAsString(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            return null;

        string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(\"(?<text>(?:\\\\.|[^\"\\\\])*)\"|(?<number>-?\\d+(?:\\.\\d+)?)|(?<bool>true|false)|null)";
        Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        if (match.Groups["text"].Success)
            return Regex.Unescape(match.Groups["text"].Value);
        if (match.Groups["number"].Success)
            return match.Groups["number"].Value;
        if (match.Groups["bool"].Success)
            return match.Groups["bool"].Value;

        return null;
    }

    private static int FirstPositive(params int[] values)
    {
        if (values == null)
            return 0;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] > 0)
                return values[i];
        }

        return 0;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
            return null;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        }

        return null;
    }
}

public sealed class ChatUserLevelDebugResult
{
    public bool IsSuccess;
    public int Seq;
    public string Level;
    public string Xml;
    public string Json;
    public long ResponseCode;
    public string ResponseBody;
    public string RequestUrl;
    public string ErrorMessage;
}
