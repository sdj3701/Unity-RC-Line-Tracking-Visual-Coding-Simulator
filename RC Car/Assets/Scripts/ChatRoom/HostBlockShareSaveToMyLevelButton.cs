using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth;
using MG_BlocksEngine2.Storage;
using RC.Network.Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class HostBlockShareSaveToMyLevelButton : MonoBehaviour
{
    private const string BlueLogColor = "#33A6FF";

    [Header("Target")]
    [SerializeField] private HostBlockShareAutoRefreshPanel _sourcePanel;
    [SerializeField] private Button _saveButton;
    [SerializeField] private Button _refreshVerifyButton;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private TMP_Text _resultBoolText;

    [Header("Option")]
    [SerializeField] private string _tokenOverride = string.Empty;
    [SerializeField] private bool _disableButtonWhileSaving = true;
    [SerializeField] private bool _refreshListAfterSave = true;
    [SerializeField] private bool _debugLog = true;

    private ChatRoomManager _boundManager;
    private bool _isSaving;
    private bool _isRefreshingVerify;
    private string _activeShareId = string.Empty;
    private string _activeVerifyShareId = string.Empty;
    private string _activeFileNameHint = string.Empty;
    private int _activeUserLevelSeqHint;
    private TaskCompletionSource<SaveAwaitResult> _saveAwaitTcs;
    private TaskCompletionSource<DetailAwaitResult> _detailAwaitTcs;
    private int _batchTotal;
    private int _batchCurrent;

    public bool HasSaveResult { get; private set; }
    public bool LastSaveResult { get; private set; }
    public bool HasVerifyResult { get; private set; }
    public bool LastVerifyResult { get; private set; }

    private const float SaveAwaitTimeoutSeconds = 20f;
    private const float DetailAwaitTimeoutSeconds = 15f;
    private const float BusyWaitTimeoutSeconds = 10f;

    private sealed class SaveAwaitResult
    {
        public bool Success;
        public string Message;
        public ChatRoomBlockShareSaveInfo Info;
    }

    private sealed class DetailAwaitResult
    {
        public bool Success;
        public string Message;
        public ChatRoomBlockShareInfo Info;
    }

    private void OnEnable()
    {
        if (_saveButton == null)
            _saveButton = GetComponent<Button>();

        if (_saveButton != null)
        {
            _saveButton.onClick.RemoveListener(OnClickSaveToMyLevel);
            _saveButton.onClick.AddListener(OnClickSaveToMyLevel);
        }

        if (_refreshVerifyButton != null)
        {
            _refreshVerifyButton.onClick.RemoveListener(OnClickRefreshVerify);
            _refreshVerifyButton.onClick.AddListener(OnClickRefreshVerify);
        }

        TryBindManagerEvents();
        UpdateResultBoolText();
        UpdateButtonInteractable();
    }

    private void OnDisable()
    {
        if (_saveButton != null)
            _saveButton.onClick.RemoveListener(OnClickSaveToMyLevel);

        if (_refreshVerifyButton != null)
            _refreshVerifyButton.onClick.RemoveListener(OnClickRefreshVerify);

        UnbindManagerEvents();
        _isSaving = false;
        _isRefreshingVerify = false;
        _activeShareId = string.Empty;
        _activeVerifyShareId = string.Empty;
        _activeFileNameHint = string.Empty;
        _activeUserLevelSeqHint = 0;
        _saveAwaitTcs = null;
        _detailAwaitTcs = null;
        _batchTotal = 0;
        _batchCurrent = 0;
        HasSaveResult = false;
        LastSaveResult = false;
        HasVerifyResult = false;
        LastVerifyResult = false;
        UpdateResultBoolText();
        UpdateButtonInteractable();
    }

    public void OnClickSaveToMyLevel()
    {
        if (_isRefreshingVerify)
        {
            SetStatus("Refresh verify is already running.");
            return;
        }

        if (_isSaving)
        {
            SetStatus("Save is already running.");
            return;
        }

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

        List<string> shareIds = CollectTargetShareIds();
        if (shareIds.Count <= 0)
        {
            SetStatus("Select one or more block shares first.");
            return;
        }

        _ = SaveSelectedSharesAsync(shareIds);
    }

    public void OnClickRefreshVerify()
    {
        if (_isSaving)
        {
            SetStatus("Save is already running.");
            return;
        }

        if (_isRefreshingVerify)
        {
            SetStatus("Refresh verify is already running.");
            return;
        }

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

        string shareId = ResolveSelectedShareId();
        if (string.IsNullOrWhiteSpace(shareId))
        {
            SetStatus("Select one block share first.");
            return;
        }

        _ = RefreshSelectedShareVerificationAsync(shareId);
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
        _boundManager.OnBlockShareDetailFetchSucceeded += HandleDetailFetchSucceeded;
        _boundManager.OnBlockShareDetailFetchFailed += HandleDetailFetchFailed;
        _boundManager.OnBlockShareDetailFetchCanceled += HandleDetailFetchCanceled;
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnBlockShareSaveSucceeded -= HandleSaveSucceeded;
        _boundManager.OnBlockShareSaveFailed -= HandleSaveFailed;
        _boundManager.OnBlockShareSaveCanceled -= HandleSaveCanceled;
        _boundManager.OnBlockShareDetailFetchSucceeded -= HandleDetailFetchSucceeded;
        _boundManager.OnBlockShareDetailFetchFailed -= HandleDetailFetchFailed;
        _boundManager.OnBlockShareDetailFetchCanceled -= HandleDetailFetchCanceled;
        _boundManager = null;
    }

    private void HandleSaveSucceeded(ChatRoomBlockShareSaveInfo info)
    {
        if (!_isSaving)
            return;

        string shareId = info != null ? info.ShareId : string.Empty;
        if (!IsActiveShare(shareId))
            return;

        CompleteCurrentSave(new SaveAwaitResult
        {
            Success = true,
            Message = string.Empty,
            Info = info
        });
    }

    private void HandleSaveFailed(string shareId, string message)
    {
        if (!_isSaving)
            return;

        if (!IsActiveShare(shareId))
            return;

        CompleteCurrentSave(new SaveAwaitResult
        {
            Success = false,
            Message = string.IsNullOrWhiteSpace(message) ? "save failed" : message,
            Info = null
        });
    }

    private void HandleSaveCanceled(string shareId)
    {
        if (!_isSaving)
            return;

        if (!IsActiveShare(shareId))
            return;

        CompleteCurrentSave(new SaveAwaitResult
        {
            Success = false,
            Message = "save canceled",
            Info = null
        });
    }

    private void HandleDetailFetchSucceeded(ChatRoomBlockShareInfo info)
    {
        if (!_isRefreshingVerify)
            return;

        string shareId = info != null ? info.BlockShareId : string.Empty;
        if (!IsActiveVerifyShare(shareId))
            return;

        CompleteCurrentDetail(new DetailAwaitResult
        {
            Success = true,
            Message = string.Empty,
            Info = info
        });
    }

    private void HandleDetailFetchFailed(string roomId, string shareId, string message)
    {
        if (!_isRefreshingVerify)
            return;

        if (!IsActiveVerifyShare(shareId))
            return;

        CompleteCurrentDetail(new DetailAwaitResult
        {
            Success = false,
            Message = string.IsNullOrWhiteSpace(message) ? "detail fetch failed" : message,
            Info = null
        });
    }

    private void HandleDetailFetchCanceled(string roomId, string shareId)
    {
        if (!_isRefreshingVerify)
            return;

        if (!IsActiveVerifyShare(shareId))
            return;

        CompleteCurrentDetail(new DetailAwaitResult
        {
            Success = false,
            Message = "detail fetch canceled",
            Info = null
        });
    }

    private bool IsActiveShare(string shareId)
    {
        string current = string.IsNullOrWhiteSpace(_activeShareId) ? string.Empty : _activeShareId.Trim();
        string incoming = string.IsNullOrWhiteSpace(shareId) ? string.Empty : shareId.Trim();
        return string.Equals(current, incoming, StringComparison.Ordinal);
    }

    private bool IsActiveVerifyShare(string shareId)
    {
        string current = string.IsNullOrWhiteSpace(_activeVerifyShareId) ? string.Empty : _activeVerifyShareId.Trim();
        string incoming = string.IsNullOrWhiteSpace(shareId) ? string.Empty : shareId.Trim();
        return string.Equals(current, incoming, StringComparison.Ordinal);
    }

    private List<string> CollectTargetShareIds()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (_sourcePanel != null && _sourcePanel.TryGetCheckedShareIds(out List<string> checkedIds))
        {
            for (int i = 0; i < checkedIds.Count; i++)
            {
                string shareId = string.IsNullOrWhiteSpace(checkedIds[i]) ? string.Empty : checkedIds[i].Trim();
                if (string.IsNullOrWhiteSpace(shareId))
                    continue;

                if (seen.Add(shareId))
                    result.Add(shareId);
            }
        }

        if (result.Count <= 0)
        {
            string fallbackShareId = _sourcePanel != null ? _sourcePanel.SelectedShareId : string.Empty;
            fallbackShareId = string.IsNullOrWhiteSpace(fallbackShareId) ? string.Empty : fallbackShareId.Trim();
            if (!string.IsNullOrWhiteSpace(fallbackShareId) && seen.Add(fallbackShareId))
                result.Add(fallbackShareId);
        }

        return result;
    }

    private string ResolveSelectedShareId()
    {
        if (_sourcePanel == null)
            return string.Empty;

        string shareId = _sourcePanel.SelectedShareId;
        return string.IsNullOrWhiteSpace(shareId) ? string.Empty : shareId.Trim();
    }

    private async Task SaveSelectedSharesAsync(List<string> shareIds)
    {
        if (shareIds == null || shareIds.Count <= 0)
            return;

        _isSaving = true;
        _batchTotal = shareIds.Count;
        _batchCurrent = 0;
        UpdateButtonInteractable();

        int successCount = 0;
        int failCount = 0;

        try
        {
            for (int i = 0; i < shareIds.Count; i++)
            {
                string shareId = string.IsNullOrWhiteSpace(shareIds[i]) ? string.Empty : shareIds[i].Trim();
                if (string.IsNullOrWhiteSpace(shareId))
                {
                    failCount++;
                    continue;
                }

                _batchCurrent = i + 1;

                if (_boundManager == null)
                {
                    failCount++;
                    SetSaveResult(false);
                    SetStatus($"Save stopped(manager-null). ({_batchCurrent}/{_batchTotal}) shareId={shareId}");
                    break;
                }

                bool isIdle = await WaitUntilManagerIdleAsync(BusyWaitTimeoutSeconds);
                if (!isIdle)
                {
                    failCount++;
                    SetSaveResult(false);
                    SetStatus($"Save skipped(busy-timeout). ({_batchCurrent}/{_batchTotal}) shareId={shareId}");
                    continue;
                }

                _activeShareId = shareId;
                CaptureActiveSelectionHints(_activeShareId);
                _saveAwaitTcs = new TaskCompletionSource<SaveAwaitResult>();

                _boundManager.SaveBlockShareToMyLevel(_activeShareId, ResolveTokenOverride());
                SetStatus($"Save requested ({_batchCurrent}/{_batchTotal}). shareId={_activeShareId}");

                SaveAwaitResult result = await AwaitCurrentSaveAsync(SaveAwaitTimeoutSeconds);
                if (result != null && result.Success)
                {
                    successCount++;
                    SetSaveResult(true);

                    ChatRoomBlockShareSaveInfo info = result.Info;
                    SetStatus($"Save success ({_batchCurrent}/{_batchTotal}). shareId={_activeShareId}, savedSeq={info?.SavedUserLevelSeq}");
                    _ = DebugLogSavedBlockCodeDataAsync(info);
                }
                else
                {
                    failCount++;
                    SetSaveResult(false);
                    string message = result != null && !string.IsNullOrWhiteSpace(result.Message)
                        ? result.Message
                        : "save failed";
                    SetStatus($"Save failed ({_batchCurrent}/{_batchTotal}). shareId={_activeShareId}, message={message}");
                }

                if (_refreshListAfterSave && _sourcePanel != null)
                    _sourcePanel.RequestListNow();
            }
        }
        finally
        {
            _isSaving = false;
            _activeShareId = string.Empty;
            _saveAwaitTcs = null;
            UpdateButtonInteractable();

            int total = _batchTotal;
            _batchTotal = 0;
            _batchCurrent = 0;
            SetStatus($"Batch save finished. total={total}, success={successCount}, failed={failCount}");
        }
    }

    private async Task RefreshSelectedShareVerificationAsync(string shareId)
    {
        string normalizedShareId = string.IsNullOrWhiteSpace(shareId) ? string.Empty : shareId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedShareId))
            return;

        _isRefreshingVerify = true;
        _activeVerifyShareId = normalizedShareId;
        CaptureActiveSelectionHints(_activeVerifyShareId);
        UpdateButtonInteractable();

        try
        {
            if (_refreshListAfterSave && _sourcePanel != null)
                _sourcePanel.RequestListNow();

            bool isIdle = await WaitUntilManagerIdleAsync(BusyWaitTimeoutSeconds);
            if (!isIdle)
            {
                SetVerifyResult(false);
                SetStatus($"Verify skipped(busy-timeout). shareId={_activeVerifyShareId}");
                LogBlue($"Verify skipped: manager busy timeout. shareId={_activeVerifyShareId}");
                return;
            }

            string roomId = ResolveTargetRoomId(_activeVerifyShareId);
            if (string.IsNullOrWhiteSpace(roomId))
            {
                SetVerifyResult(false);
                SetStatus($"Verify failed(room-missing). shareId={_activeVerifyShareId}");
                LogBlue($"Verify failed: apiRoomId is empty. shareId={_activeVerifyShareId}");
                return;
            }

            _detailAwaitTcs = new TaskCompletionSource<DetailAwaitResult>();
            _boundManager.FetchBlockShareDetail(roomId, _activeVerifyShareId, ResolveTokenOverride());
            SetStatus($"Verify refresh requested. shareId={_activeVerifyShareId}");
            LogBlue($"Verify refresh requested. roomId={roomId}, shareId={_activeVerifyShareId}");

            DetailAwaitResult detailResult = await AwaitCurrentDetailAsync(DetailAwaitTimeoutSeconds);
            if (detailResult == null || !detailResult.Success || detailResult.Info == null)
            {
                string message = detailResult != null && !string.IsNullOrWhiteSpace(detailResult.Message)
                    ? detailResult.Message
                    : "detail fetch failed";
                SetVerifyResult(false);
                SetStatus($"Verify failed(detail). shareId={_activeVerifyShareId}, message={message}");
                LogBlue($"Verify detail failed. shareId={_activeVerifyShareId}, message={message}");
                return;
            }

            ChatRoomBlockShareInfo detail = detailResult.Info;
            CaptureActiveSelectionHints(_activeVerifyShareId);
            LogBlue(
                $"Verify detail refreshed. shareId={detail.BlockShareId}, roomId={detail.RoomId}, userId={detail.UserId}, userLevelSeq={detail.UserLevelSeq}, fileName={detail.Message}");

            if (detail.UserLevelSeq <= 0)
            {
                SetVerifyResult(false);
                SetStatus($"Verify result: no userLevelSeq. shareId={_activeVerifyShareId}");
                LogBlue($"Verify result: userLevelSeq is empty or invalid. shareId={_activeVerifyShareId}");
                return;
            }

            string accessToken = ResolveAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                SetVerifyResult(false);
                SetStatus($"Verify failed(token-missing). shareId={_activeVerifyShareId}, seq={detail.UserLevelSeq}");
                LogBlue($"Verify failed: accessToken is empty. shareId={_activeVerifyShareId}, seq={detail.UserLevelSeq}");
                return;
            }

            ChatUserLevelDebugResult verifyResult = await ChatUserLevelDebugApi.FetchBySeqAsync(detail.UserLevelSeq, accessToken);
            if (verifyResult == null || !verifyResult.IsSuccess)
            {
                string error = "verify fetch returned null";
                if (verifyResult != null)
                {
                    if (!string.IsNullOrWhiteSpace(verifyResult.ErrorMessage))
                        error = verifyResult.ErrorMessage.Trim();
                    else if (verifyResult.ResponseCode > 0)
                        error = $"HTTP {verifyResult.ResponseCode}";
                    else
                        error = "verify fetch failed";
                }
                SetVerifyResult(false);
                SetStatus($"Verify failed(db-fetch). shareId={_activeVerifyShareId}, seq={detail.UserLevelSeq}, message={error}");
                LogBlue(
                    $"Verify DB fetch failed. shareId={_activeVerifyShareId}, seq={detail.UserLevelSeq}, code={verifyResult?.ResponseCode}, error={error}, body={TruncateForDebug(verifyResult?.ResponseBody)}");
                return;
            }

            string xml = verifyResult.Xml ?? string.Empty;
            string json = verifyResult.Json ?? string.Empty;
            int xmlLen = xml.Length;
            int jsonLen = json.Length;
            bool hasData = xmlLen > 0 || jsonLen > 0;

            SetVerifyResult(hasData);
            SetStatus(
                $"Verify result: {(hasData ? "data-exists" : "data-empty")}. shareId={_activeVerifyShareId}, seq={detail.UserLevelSeq}, xmlLen={xmlLen}, jsonLen={jsonLen}");
            LogBlue(
                $"Verify payload checked. shareId={_activeVerifyShareId}, seq={detail.UserLevelSeq}, xmlLen={xmlLen}, jsonLen={jsonLen}, hasData={hasData}, code={verifyResult.ResponseCode}");
            LogBlue($"Verify XML:\n{TruncateForDebug(xml)}");
            LogBlue($"Verify JSON:\n{TruncateForDebug(json)}");
        }
        finally
        {
            _isRefreshingVerify = false;
            _activeVerifyShareId = string.Empty;
            _detailAwaitTcs = null;
            UpdateButtonInteractable();
        }
    }

    private async Task<bool> WaitUntilManagerIdleAsync(float timeoutSeconds)
    {
        if (_boundManager == null)
            return false;

        float timeout = Mathf.Max(0.5f, timeoutSeconds);
        float deadline = Time.realtimeSinceStartup + timeout;

        while (_boundManager.IsBusy)
        {
            if (Time.realtimeSinceStartup >= deadline)
                return false;

            await Task.Yield();
        }

        return true;
    }

    private async Task<SaveAwaitResult> AwaitCurrentSaveAsync(float timeoutSeconds)
    {
        if (_saveAwaitTcs == null)
        {
            return new SaveAwaitResult
            {
                Success = false,
                Message = "internal save waiter is null",
                Info = null
            };
        }

        float timeout = Mathf.Max(1f, timeoutSeconds);
        float deadline = Time.realtimeSinceStartup + timeout;
        Task<SaveAwaitResult> waitTask = _saveAwaitTcs.Task;

        while (!waitTask.IsCompleted)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                return new SaveAwaitResult
                {
                    Success = false,
                    Message = "timeout",
                    Info = null
                };
            }

            await Task.Yield();
        }

        return waitTask.Result ?? new SaveAwaitResult
        {
            Success = false,
            Message = "empty save result",
            Info = null
        };
    }

    private async Task<DetailAwaitResult> AwaitCurrentDetailAsync(float timeoutSeconds)
    {
        if (_detailAwaitTcs == null)
        {
            return new DetailAwaitResult
            {
                Success = false,
                Message = "internal detail waiter is null",
                Info = null
            };
        }

        float timeout = Mathf.Max(1f, timeoutSeconds);
        float deadline = Time.realtimeSinceStartup + timeout;
        Task<DetailAwaitResult> waitTask = _detailAwaitTcs.Task;

        while (!waitTask.IsCompleted)
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                return new DetailAwaitResult
                {
                    Success = false,
                    Message = "detail timeout",
                    Info = null
                };
            }

            await Task.Yield();
        }

        return waitTask.Result ?? new DetailAwaitResult
        {
            Success = false,
            Message = "empty detail result",
            Info = null
        };
    }

    private void CompleteCurrentSave(SaveAwaitResult result)
    {
        if (_saveAwaitTcs == null)
            return;

        if (_saveAwaitTcs.Task.IsCompleted)
            return;

        _saveAwaitTcs.TrySetResult(result);
    }

    private void CompleteCurrentDetail(DetailAwaitResult result)
    {
        if (_detailAwaitTcs == null)
            return;

        if (_detailAwaitTcs.Task.IsCompleted)
            return;

        _detailAwaitTcs.TrySetResult(result);
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
            LogMappingNeeded($"Selected detail is stale. selectedShareId={shareId}, detailShareId={detail.BlockShareId}");

        if (_sourcePanel.TryGetListItemInfoByShareId(shareId, out string messageByShareId, out int seqByShareId))
        {
            _activeUserLevelSeqHint = seqByShareId;
            if (!string.IsNullOrWhiteSpace(messageByShareId))
                _activeFileNameHint = messageByShareId.Trim();

            if (_debugLog)
            {
                Debug.Log(
                    $"[HostBlockShareSaveToMyLevelButton] Selection linked(shareId). shareId={shareId}, userLevelSeqHint={_activeUserLevelSeqHint}, fileNameHint={_activeFileNameHint}");
            }
            return;
        }

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

    private void LogBlue(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"<color={BlueLogColor}>[HostBlockShareSaveToMyLevelButton] {message}</color>");
    }

    private void LogMappingNeeded(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"<color=orange>[HostBlockShareSaveToMyLevelButton][MAPPING] {message}</color>");
    }

    private void UpdateButtonInteractable()
    {
        bool interactable = true;

        if (_disableButtonWhileSaving && (_isSaving || _isRefreshingVerify))
            interactable = false;

        if (_saveButton != null)
            _saveButton.interactable = interactable;

        if (_refreshVerifyButton != null)
            _refreshVerifyButton.interactable = interactable;
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_tokenOverride) ? null : _tokenOverride.Trim();
    }

    private string ResolveAccessToken()
    {
        string overrideToken = ResolveTokenOverride();
        if (!string.IsNullOrWhiteSpace(overrideToken))
            return overrideToken;

        return AuthManager.Instance != null ? AuthManager.Instance.GetAccessToken() : string.Empty;
    }

    private string ResolveTargetRoomId(string shareId)
    {
        ChatRoomBlockShareInfo selectedDetail = _sourcePanel != null ? _sourcePanel.SelectedDetailInfo : null;
        if (selectedDetail != null &&
            string.Equals(shareId, selectedDetail.BlockShareId ?? string.Empty, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(selectedDetail.RoomId))
        {
            return selectedDetail.RoomId.Trim();
        }

        FusionRoomSessionInfo fusionContext = FusionRoomSessionContext.Current;
        if (fusionContext != null && !string.IsNullOrWhiteSpace(fusionContext.ApiRoomId))
            return fusionContext.ApiRoomId.Trim();

        return NetworkRoomIdentity.ResolveApiRoomId();
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

    private void SetVerifyResult(bool success)
    {
        HasVerifyResult = true;
        LastVerifyResult = success;
        UpdateResultBoolText();
    }

    private void UpdateResultBoolText()
    {
        if (_resultBoolText == null)
            return;

        string saveValue = HasSaveResult
            ? (LastSaveResult ? "True" : "False")
            : "-";
        string verifyValue = HasVerifyResult
            ? (LastVerifyResult ? "True" : "False")
            : "-";

        _resultBoolText.text = $"Save Result: {saveValue}\nVerify Result: {verifyValue}";
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
