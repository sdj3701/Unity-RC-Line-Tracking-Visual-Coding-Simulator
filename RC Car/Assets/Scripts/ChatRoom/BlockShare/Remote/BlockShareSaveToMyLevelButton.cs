using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth;
using MG_BlocksEngine2.Storage;
using RC.Network.Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

public class BlockShareSaveToMyLevelButton : MonoBehaviour
{
    private const string BlueLogColor = "#33A6FF";
    private const string DiagnosticLogColor = "#FF4D4D";
    private const string BlockShareDetailEndpointRoute = "GET /api/chat/rooms/{roomId}/block-shares/{shareId}";
    private const string OverlayCanvasName = "Canvas Car Renders";
    private const string OverlayRenderName = "RCCarRender";
    private const string UpdateButtonName = "But_Update";

    [Header("Target")]
    [SerializeField] private BlockShareRemoteListPanel _sourcePanel;
    [SerializeField] private Button _saveButton;

    [Header("Option")]
    [SerializeField] private string _tokenOverride = string.Empty;
    [SerializeField] private bool _disableButtonWhileSaving = true;
    [SerializeField] private bool _refreshListAfterSave = true;
    [SerializeField] private bool _debugLog = true;

    [Header("Ownership Guard")]
    [SerializeField] private bool _enforceDedicatedButtonOwnership = true;

    private ChatRoomManager _boundManager;
    private HostNetworkCarCoordinator _hostCoordinator;
    private IBlockShareListService _listService;
    private bool _isSaving;
    private string _activeShareId = string.Empty;
    private string _activeFileNameHint = string.Empty;
    private int _activeUserLevelSeqHint;
    private TaskCompletionSource<DetailAwaitResult> _detailAwaitTcs;
    private bool _saveButtonBlockedByOwnership;
    private bool _lastInteractableState;
    private bool _hasLoggedInteractableState;
    private string _lastInteractableReason = string.Empty;
    private bool _overlayRaycastBlockerSanitized;
    private bool _buttonChildRaycastSanitized;
    private BlockShareSaveButtonClickProxy _saveButtonProxy;
    private string _lastRaycastProbeSummary = string.Empty;

    private const float DetailAwaitTimeoutSeconds = 20f;
    private const float BusyWaitTimeoutSeconds = 10f;
    private const int RoomSnapshotPage = 1;
    private const int RoomSnapshotSize = 200;

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

        TraceSaveEvent("OnEnable");
        TryDisableOverlayRaycastBlockers();
        TryDisableConflictingButtonChildRaycasts();
        ResolveButtonOwnership();
        BindButtons();
        TryBindManagerEvents();
        UpdateButtonInteractable();
    }

    private void Update()
    {
        if (!_overlayRaycastBlockerSanitized)
            TryDisableOverlayRaycastBlockers();
        if (!_buttonChildRaycastSanitized)
            TryDisableConflictingButtonChildRaycasts();

        TryBindManagerEvents();
        UpdateButtonInteractable();
    }

    private void OnDisable()
    {
        TraceSaveEvent("OnDisable");
        UnbindButtons();
        UnbindManagerEvents();
        _isSaving = false;
        _activeShareId = string.Empty;
        _activeFileNameHint = string.Empty;
        _activeUserLevelSeqHint = 0;
        _detailAwaitTcs = null;
        _saveButtonBlockedByOwnership = false;
        _hasLoggedInteractableState = false;
        _lastInteractableReason = string.Empty;
        _overlayRaycastBlockerSanitized = false;
        _buttonChildRaycastSanitized = false;
        _lastRaycastProbeSummary = string.Empty;
        UpdateButtonInteractable();
    }

    private void HandleSaveButtonClick()
    {
        TraceSaveEvent("HandleSaveButtonClick");

        if (_saveButtonBlockedByOwnership)
        {
            SetStatus("Apply is blocked: button ownership conflict.");
            return;
        }

        if (!IsCurrentUserHost())
        {
            SetStatus("Apply is host-only.");
            return;
        }

        if (_isSaving)
        {
            SetStatus("Apply is already running.");
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

        LogDiagnostic(
            $"Save batch request accepted. selectedObject={DescribeCurrentSelectedObject()}, saveButton={DescribeButton(_saveButton)}, sourcePanel={DescribeGameObject(_sourcePanel != null ? _sourcePanel.gameObject : null)}");
        _ = SaveLatestRoomSharesAndRunAsync();
    }

    private void BindButtons()
    {
        TraceSaveEvent("BindButtons");

        if (_saveButton == null)
        {
            LogDiagnostic("BindButtons skipped. saveButton=(null)");
            return;
        }

        _saveButton.onClick.RemoveListener(HandleSaveButtonClick);

        if (_saveButtonBlockedByOwnership)
        {
            _saveButton.interactable = false;
            LogDiagnostic(
                $"BindButtons skipped AddListener due to ownership conflict. saveButton={DescribeButton(_saveButton)}, persistentCount={_saveButton.onClick.GetPersistentEventCount()}");
            return;
        }

        EnsureSaveButtonProxy();
        _saveButton.onClick.AddListener(HandleSaveButtonClick);
        LogDiagnostic(
            $"BindButtons attached HandleSaveButtonClick. saveButton={DescribeButton(_saveButton)}, persistentCount={_saveButton.onClick.GetPersistentEventCount()}");
    }

    private void UnbindButtons()
    {
        TraceSaveEvent("UnbindButtons");

        if (_saveButton != null)
            _saveButton.onClick.RemoveListener(HandleSaveButtonClick);

        if (_saveButtonProxy != null)
            _saveButtonProxy.Configure(null);
    }

    private void ResolveButtonOwnership()
    {
        _saveButtonBlockedByOwnership = false;

        if (!_enforceDedicatedButtonOwnership)
            return;

        if (_saveButton != null && IsUsedByClientPanel(_saveButton, out string saveOwnerName))
        {
            _saveButtonBlockedByOwnership = true;
            Debug.LogWarning($"[BlockShareSaveToMyLevelButton] Save button ownership conflict. owner={saveOwnerName}, button={_saveButton.name}");
        }
    }

    private bool IsUsedByClientPanel(Button button, out string ownerName)
    {
        ownerName = string.Empty;
        if (button == null)
            return false;

        LocalBlockCodeListPanel[] panels = FindObjectsOfType<LocalBlockCodeListPanel>(true);
        if (panels == null || panels.Length == 0)
            return false;

        for (int i = 0; i < panels.Length; i++)
        {
            LocalBlockCodeListPanel panel = panels[i];
            if (panel == null)
                continue;

            if (!panel.IsOwnedActionButton(button))
                continue;

            ownerName = panel.gameObject.name;
            return true;
        }

        return false;
    }

    private bool IsCurrentUserHost()
    {
        FusionRoomSessionInfo fusionContext = FusionRoomSessionContext.Current;
        if (fusionContext != null)
        {
            if (fusionContext.IsHost || fusionContext.GameMode == Fusion.GameMode.Host)
                return true;

            if (!string.IsNullOrWhiteSpace(fusionContext.HostUserId) &&
                AuthManager.Instance != null &&
                AuthManager.Instance.CurrentUser != null)
            {
                string fusionHostUserId = fusionContext.HostUserId.Trim();
                string currentFusionUserId = AuthManager.Instance.CurrentUser.userId ?? string.Empty;
                currentFusionUserId = currentFusionUserId.Trim();
                return string.Equals(fusionHostUserId, currentFusionUserId, StringComparison.Ordinal);
            }
        }

        RoomInfo room = RoomSessionContext.CurrentRoom;
        if (room == null || string.IsNullOrWhiteSpace(room.HostUserId))
            return false;

        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
            return false;

        string hostUserId = room.HostUserId.Trim();
        string currentUserId = AuthManager.Instance.CurrentUser.userId ?? string.Empty;
        currentUserId = currentUserId.Trim();
        return string.Equals(hostUserId, currentUserId, StringComparison.Ordinal);
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
        _boundManager.OnBlockShareDetailFetchSucceeded += HandleDetailFetchSucceeded;
        _boundManager.OnBlockShareDetailFetchFailed += HandleDetailFetchFailed;
        _boundManager.OnBlockShareDetailFetchCanceled += HandleDetailFetchCanceled;
    }

    private void UnbindManagerEvents()
    {
        if (_boundManager == null)
            return;

        _boundManager.OnBlockShareDetailFetchSucceeded -= HandleDetailFetchSucceeded;
        _boundManager.OnBlockShareDetailFetchFailed -= HandleDetailFetchFailed;
        _boundManager.OnBlockShareDetailFetchCanceled -= HandleDetailFetchCanceled;
        _boundManager = null;
    }

    private void HandleDetailFetchSucceeded(ChatRoomBlockShareInfo info)
    {
        if (!_isSaving)
            return;

        string shareId = info != null ? info.BlockShareId : string.Empty;
        if (!IsActiveShare(shareId))
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
        if (!_isSaving)
            return;

        if (!IsActiveShare(shareId))
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
        if (!_isSaving)
            return;

        if (!IsActiveShare(shareId))
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

    private string ResolveSelectedShareId()
    {
        if (_sourcePanel == null)
            return string.Empty;

        string shareId = _sourcePanel.SelectedShareId;
        return string.IsNullOrWhiteSpace(shareId) ? string.Empty : shareId.Trim();
    }

    private async Task SaveLatestRoomSharesAndRunAsync()
    {
        _isSaving = true;
        UpdateButtonInteractable();

        try
        {
            if (_boundManager == null)
            {
                SetStatus("Save batch stopped(manager-null).");
                return;
            }

            string roomId = ResolveSelectedRoomId();
            if (string.IsNullOrWhiteSpace(roomId))
            {
                SetStatus("Save batch stopped(roomId-null).");
                return;
            }

            HostNetworkCarCoordinator hostCoordinator = ResolveHostCoordinator();
            if (hostCoordinator == null)
            {
                SetStatus("HostNetworkCarCoordinator is null.");
                return;
            }

            bool isIdle = await WaitUntilManagerIdleAsync(BusyWaitTimeoutSeconds);
            if (!isIdle)
            {
                SetStatus("Save batch skipped(busy-timeout).");
                return;
            }

            SetStatus($"Save batch requested. roomId={roomId}");
            IReadOnlyList<BlockShareListItemViewModel> snapshot = await FetchLatestRoomShareSnapshotAsync(roomId);
            List<BlockShareListItemViewModel> latestShares = SelectLatestSharePerUser(snapshot);
            if (latestShares.Count <= 0)
            {
                SetStatus("Save batch stopped(no remote shares).");
                return;
            }

            int appliedCount = 0;
            int failedCount = 0;
            string firstFailure = string.Empty;

            for (int i = 0; i < latestShares.Count; i++)
            {
                BlockShareListItemViewModel item = latestShares[i];
                if (item == null)
                    continue;

                string userId = Normalize(item.UserId);
                string shareId = Normalize(item.ShareId);
                SetStatus($"Save batch applying {i + 1}/{latestShares.Count}. user={userId}, shareId={shareId}");

                DetailAwaitResult detailResult = await FetchDetailAsync(roomId, shareId);
                if (detailResult == null || !detailResult.Success)
                {
                    failedCount++;
                    string detailMessage = detailResult != null && !string.IsNullOrWhiteSpace(detailResult.Message)
                        ? detailResult.Message
                        : "detail fetch failed";
                    if (string.IsNullOrWhiteSpace(firstFailure))
                        firstFailure = $"user={userId}, shareId={shareId}, message={detailMessage}";

                    continue;
                }

                if (detailResult.Info == null)
                {
                    failedCount++;
                    if (string.IsNullOrWhiteSpace(firstFailure))
                        firstFailure = $"user={userId}, shareId={shareId}, message=empty detail result";
                    continue;
                }

                BackfillDetailInfo(detailResult.Info, item);
                string applyError = await hostCoordinator.ApplyRemoteBlockShareToOwnerAsync(detailResult.Info);
                if (string.IsNullOrWhiteSpace(applyError))
                {
                    appliedCount++;
                    continue;
                }

                failedCount++;
                if (string.IsNullOrWhiteSpace(firstFailure))
                    firstFailure = $"user={userId}, shareId={shareId}, message={applyError}";
            }

            if (appliedCount > 0)
            {
                if (failedCount > 0 && !string.IsNullOrWhiteSpace(firstFailure))
                {
                    SetStatus(
                        $"Save batch complete. applied={appliedCount}, failed={failedCount}, run=manual, firstFailure={firstFailure}");
                }
                else
                {
                    SetStatus($"Save batch complete. applied={appliedCount}, failed={failedCount}, run=manual");
                }
            }
            else if (!string.IsNullOrWhiteSpace(firstFailure))
            {
                SetStatus($"Save batch failed. applied=0, failed={failedCount}, firstFailure={firstFailure}");
            }
            else
            {
                SetStatus("Save batch failed. applied=0");
            }

            if (_refreshListAfterSave && _sourcePanel != null)
                _sourcePanel.RequestRefresh();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Save batch canceled.");
        }
        catch (Exception e)
        {
            SetStatus($"Save batch failed. ({e.Message})");
        }
        finally
        {
            _isSaving = false;
            _activeShareId = string.Empty;
            _detailAwaitTcs = null;
            UpdateButtonInteractable();
        }
    }

    private async Task<IReadOnlyList<BlockShareListItemViewModel>> FetchLatestRoomShareSnapshotAsync(string roomId)
    {
        if (_listService == null)
            _listService = new ChatRoomBlockShareListService();

        string targetRoomId = Normalize(roomId);
        if (string.IsNullOrWhiteSpace(targetRoomId))
            return Array.Empty<BlockShareListItemViewModel>();

        IReadOnlyList<BlockShareListItemViewModel> items = await _listService.FetchListAsync(
            targetRoomId,
            RoomSnapshotPage,
            RoomSnapshotSize,
            ResolveTokenOverride());

        return items ?? Array.Empty<BlockShareListItemViewModel>();
    }

    private List<BlockShareListItemViewModel> SelectLatestSharePerUser(IReadOnlyList<BlockShareListItemViewModel> snapshot)
    {
        var bestByUser = new Dictionary<string, BlockShareListItemViewModel>(StringComparer.Ordinal);
        var indexByUser = new Dictionary<string, int>(StringComparer.Ordinal);

        if (snapshot != null)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                BlockShareListItemViewModel candidate = snapshot[i];
                if (candidate == null)
                    continue;

                string userId = Normalize(candidate.UserId);
                string shareId = Normalize(candidate.ShareId);
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(shareId))
                    continue;

                if (!bestByUser.TryGetValue(userId, out BlockShareListItemViewModel currentBest))
                {
                    bestByUser[userId] = candidate;
                    indexByUser[userId] = i;
                    continue;
                }

                int currentIndex = indexByUser.TryGetValue(userId, out int storedIndex) ? storedIndex : -1;
                if (!IsCandidateNewer(candidate, i, currentBest, currentIndex))
                    continue;

                bestByUser[userId] = candidate;
                indexByUser[userId] = i;
            }
        }

        var results = new List<BlockShareListItemViewModel>(bestByUser.Count);
        foreach (KeyValuePair<string, BlockShareListItemViewModel> pair in bestByUser)
        {
            if (pair.Value != null)
                results.Add(pair.Value);
        }

        results.Sort((left, right) =>
        {
            int leftSlot = ResolveUserSlotIndex(left != null ? left.UserId : null);
            int rightSlot = ResolveUserSlotIndex(right != null ? right.UserId : null);
            return leftSlot.CompareTo(rightSlot);
        });

        return results;
    }

    private bool IsCandidateNewer(
        BlockShareListItemViewModel candidate,
        int candidateIndex,
        BlockShareListItemViewModel currentBest,
        int currentIndex)
    {
        bool hasCandidateTime = TryParseCreatedAtUtc(candidate != null ? candidate.CreatedAtUtc : null, out DateTime candidateTime);
        bool hasCurrentTime = TryParseCreatedAtUtc(currentBest != null ? currentBest.CreatedAtUtc : null, out DateTime currentTime);

        if (hasCandidateTime && hasCurrentTime)
        {
            int compare = DateTime.Compare(candidateTime, currentTime);
            if (compare != 0)
                return compare > 0;
        }
        else if (hasCandidateTime)
        {
            return true;
        }
        else if (hasCurrentTime)
        {
            return false;
        }

        return candidateIndex >= currentIndex;
    }

    private bool TryParseCreatedAtUtc(string raw, out DateTime value)
    {
        value = default;
        string createdAt = Normalize(raw);
        if (string.IsNullOrWhiteSpace(createdAt))
            return false;

        if (DateTimeOffset.TryParse(createdAt, out DateTimeOffset parsedOffset))
        {
            value = parsedOffset.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(createdAt, out DateTime parsedDateTime))
        {
            value = parsedDateTime.ToUniversalTime();
            return true;
        }

        return false;
    }

    private int ResolveUserSlotIndex(string userIdRaw)
    {
        HostNetworkCarCoordinator hostCoordinator = ResolveHostCoordinator();
        if (hostCoordinator == null)
            return int.MaxValue;

        string userId = Normalize(userIdRaw);
        if (string.IsNullOrWhiteSpace(userId))
            return int.MaxValue;

        return hostCoordinator.TryGetSlotIndexForUser(userId, out int slotIndex)
            ? slotIndex
            : int.MaxValue;
    }

    private async Task<DetailAwaitResult> FetchDetailAsync(string roomIdRaw, string shareIdRaw)
    {
        string roomId = Normalize(roomIdRaw);
        string shareId = Normalize(shareIdRaw);
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(shareId))
        {
            return new DetailAwaitResult
            {
                Success = false,
                Message = "roomId/shareId is empty",
                Info = null
            };
        }

        if (_boundManager == null)
        {
            return new DetailAwaitResult
            {
                Success = false,
                Message = "manager is null",
                Info = null
            };
        }

        bool isIdle = await WaitUntilManagerIdleAsync(BusyWaitTimeoutSeconds);
        if (!isIdle)
        {
            return new DetailAwaitResult
            {
                Success = false,
                Message = "busy-timeout",
                Info = null
            };
        }

        _activeShareId = shareId;
        _detailAwaitTcs = new TaskCompletionSource<DetailAwaitResult>();

        try
        {
            LogBlue($"Endpoint intent=detail-fetch-batch, route={BlockShareDetailEndpointRoute}, roomId={roomId}, shareId={shareId}");
            _boundManager.FetchBlockShareDetail(roomId, shareId, ResolveTokenOverride());
            return await AwaitCurrentDetailAsync(DetailAwaitTimeoutSeconds);
        }
        finally
        {
            _activeShareId = string.Empty;
            _detailAwaitTcs = null;
        }
    }

    private void BackfillDetailInfo(ChatRoomBlockShareInfo detailInfo, BlockShareListItemViewModel item)
    {
        if (detailInfo == null || item == null)
            return;

        if (string.IsNullOrWhiteSpace(detailInfo.BlockShareId))
            detailInfo.BlockShareId = Normalize(item.ShareId);
        if (string.IsNullOrWhiteSpace(detailInfo.RoomId))
            detailInfo.RoomId = Normalize(item.RoomId);
        if (string.IsNullOrWhiteSpace(detailInfo.UserId))
            detailInfo.UserId = Normalize(item.UserId);
        if (detailInfo.UserLevelSeq <= 0)
            detailInfo.UserLevelSeq = item.UserLevelSeq;
        if (string.IsNullOrWhiteSpace(detailInfo.Message))
            detailInfo.Message = Normalize(item.FileName);
        if (string.IsNullOrWhiteSpace(detailInfo.CreatedAtUtc))
            detailInfo.CreatedAtUtc = Normalize(item.CreatedAtUtc);
    }

    private async Task FetchDetailAndApplySelectedShareAsync(string shareIdRaw)
    {
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
            return;

        _isSaving = true;
        UpdateButtonInteractable();

        try
        {
            if (_boundManager == null)
            {
                SetStatus("Apply stopped(manager-null).");
                return;
            }

            bool isIdle = await WaitUntilManagerIdleAsync(BusyWaitTimeoutSeconds);
            if (!isIdle)
            {
                SetStatus($"Apply skipped(busy-timeout). shareId={shareId}");
                return;
            }

            string roomId = ResolveSelectedRoomId();
            if (string.IsNullOrWhiteSpace(roomId))
            {
                SetStatus($"Apply stopped(roomId-null). shareId={shareId}");
                return;
            }

            HostNetworkCarCoordinator hostCoordinator = ResolveHostCoordinator();
            if (hostCoordinator == null)
            {
                SetStatus("HostNetworkCarCoordinator is null.");
                return;
            }

            _activeShareId = shareId;
            CaptureActiveSelectionHints(_activeShareId);
            _detailAwaitTcs = new TaskCompletionSource<DetailAwaitResult>();

            LogBlue($"Endpoint intent=detail-fetch, route={BlockShareDetailEndpointRoute}, roomId={roomId}, shareId={_activeShareId}");
            _boundManager.FetchBlockShareDetail(roomId, _activeShareId, ResolveTokenOverride());
            SetStatus($"Apply-to-host requested. roomId={roomId}, shareId={_activeShareId}");

            DetailAwaitResult result = await AwaitCurrentDetailAsync(DetailAwaitTimeoutSeconds);
            if (result == null || !result.Success)
            {
                string message = result != null && !string.IsNullOrWhiteSpace(result.Message)
                    ? result.Message
                    : "detail fetch failed";
                SetStatus($"Apply-to-host failed. shareId={_activeShareId}, message={message}");
            }
            else if (result.Info == null)
            {
                SetStatus($"Apply-to-host failed. shareId={_activeShareId}, message=empty detail result");
            }
            else
            {
                string applyError = await hostCoordinator.ApplyRemoteBlockShareToCurrentHostAsync(result.Info);
                if (string.IsNullOrWhiteSpace(applyError))
                {
                    SetStatus($"Apply-to-host success. shareId={_activeShareId}, userLevelSeq={result.Info.UserLevelSeq}");
                    if (_debugLog)
                    {
                        LogBlue(
                            $"Direct detail apply completed. shareId={_activeShareId}, jsonLen={(result.Info.Json ?? string.Empty).Length}, xmlLen={(result.Info.Xml ?? string.Empty).Length}");
                    }
                }
                else
                {
                    SetStatus($"Apply-to-host failed. shareId={_activeShareId}, message={applyError}");
                }
            }

            if (_refreshListAfterSave && _sourcePanel != null)
                _sourcePanel.RequestRefresh();
        }
        finally
        {
            _isSaving = false;
            _activeShareId = string.Empty;
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
                    Message = "timeout",
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

    private void CompleteCurrentDetail(DetailAwaitResult result)
    {
        if (_detailAwaitTcs == null)
            return;

        if (_detailAwaitTcs.Task.IsCompleted)
            return;

        _detailAwaitTcs.TrySetResult(result);
    }

    private string ResolveSelectedRoomId()
    {
        ChatRoomBlockShareInfo selectedDetail = _sourcePanel != null ? _sourcePanel.SelectedDetailInfo : null;
        if (selectedDetail != null && !string.IsNullOrWhiteSpace(selectedDetail.RoomId))
            return selectedDetail.RoomId.Trim();

        string roomId = NetworkRoomIdentity.ResolveApiRoomId();
        if (!string.IsNullOrWhiteSpace(roomId))
            return roomId.Trim();

        RoomInfo room = RoomSessionContext.CurrentRoom;
        if (room != null && !string.IsNullOrWhiteSpace(room.RoomId))
            return room.RoomId.Trim();

        return string.Empty;
    }

    private HostNetworkCarCoordinator ResolveHostCoordinator()
    {
        if (_hostCoordinator != null)
            return _hostCoordinator;

        _hostCoordinator = FindObjectOfType<HostNetworkCarCoordinator>(true);
        return _hostCoordinator;
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
                Debug.Log($"[BlockShareSaveToMyLevelButton] Selection linked(detail). shareId={shareId}, userLevelSeqHint={_activeUserLevelSeqHint}, fileNameHint={_activeFileNameHint}");
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
                    $"[BlockShareSaveToMyLevelButton] Selection linked(shareId). shareId={shareId}, userLevelSeqHint={_activeUserLevelSeqHint}, fileNameHint={_activeFileNameHint}");
            }
            return;
        }

        if (_sourcePanel.TryGetSelectedListItemInfo(out string listMessage, out int listUserLevelSeq))
        {
            _activeUserLevelSeqHint = listUserLevelSeq;
            if (!string.IsNullOrWhiteSpace(listMessage))
                _activeFileNameHint = listMessage.Trim();

            if (_debugLog)
                Debug.Log($"[BlockShareSaveToMyLevelButton] Selection linked(list). shareId={shareId}, userLevelSeqHint={_activeUserLevelSeqHint}, fileNameHint={_activeFileNameHint}");
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
            Debug.LogWarning("[BlockShareSaveToMyLevelButton] BE2_CodeStorageManager is null. skip XML/JSON debug load.");
            return;
        }

        string fileName = ResolveSavedFileName(info);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            Debug.LogWarning("[BlockShareSaveToMyLevelButton] Saved file name hint is empty. skip XML/JSON debug load.");
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
            Debug.LogWarning($"[BlockShareSaveToMyLevelButton] Save payload debug load failed. fileName={fileName}, error={ex.Message}");
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
        Debug.Log($"<color=#00FF66>[BlockShareSaveToMyLevelButton] {text}</color>");
    }

    private void LogBlue(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"<color={BlueLogColor}>[BlockShareSaveToMyLevelButton] {message}</color>");
    }

    private void TraceSaveEvent(string eventName)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(eventName))
            return;

        LogDiagnostic(
            $"{eventName}. selectedObject={DescribeCurrentSelectedObject()}, saveButton={DescribeButton(_saveButton)}, sourcePanel={DescribeGameObject(_sourcePanel != null ? _sourcePanel.gameObject : null)}, saveBlocked={_saveButtonBlockedByOwnership}, isSaving={_isSaving}\n{StackTraceUtility.ExtractStackTrace()}");
    }

    private void LogDiagnostic(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"<color={DiagnosticLogColor}>[BlockShareSaveToMyLevelButton]</color> {message}");
    }

    private static string DescribeCurrentSelectedObject()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return DescribeGameObject(selected);
    }

    private static string DescribeButton(Button button)
    {
        if (button == null)
            return "(null)";

        return $"{button.name}(activeSelf={button.gameObject.activeSelf}, activeInHierarchy={button.gameObject.activeInHierarchy})";
    }

    private static string DescribeGameObject(GameObject gameObject)
    {
        if (gameObject == null)
            return "(null)";

        return $"{gameObject.name}(activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy})";
    }

    private void LogMappingNeeded(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"<color=orange>[BlockShareSaveToMyLevelButton][MAPPING] {message}</color>");
    }

    private void EnsureSaveButtonProxy()
    {
        if (_saveButton == null)
            return;

        if (_saveButtonProxy == null)
            _saveButtonProxy = _saveButton.GetComponent<BlockShareSaveButtonClickProxy>();

        if (_saveButtonProxy == null)
            _saveButtonProxy = _saveButton.gameObject.AddComponent<BlockShareSaveButtonClickProxy>();

        _saveButtonProxy.Configure(this);
    }

    public void NotifyProxyPointerDown(PointerEventData eventData)
    {
        if (!_debugLog)
            return;

        LogDiagnostic(
            $"Save pointer down. selectedObject={DescribeCurrentSelectedObject()}, pointerPress={DescribeGameObject(eventData != null ? eventData.pointerPress : null)}, pointerEnter={DescribeGameObject(eventData != null ? eventData.pointerEnter : null)}");
    }

    public void NotifyProxyPointerUp(PointerEventData eventData)
    {
        if (!_debugLog)
            return;

        LogDiagnostic(
            $"Save pointer up. selectedObject={DescribeCurrentSelectedObject()}, pointerPress={DescribeGameObject(eventData != null ? eventData.pointerPress : null)}, pointerEnter={DescribeGameObject(eventData != null ? eventData.pointerEnter : null)}");
    }

    public void NotifyProxyPointerClick(PointerEventData eventData)
    {
        if (_debugLog)
        {
            LogDiagnostic(
                $"Save pointer click. selectedObject={DescribeCurrentSelectedObject()}, pointerPress={DescribeGameObject(eventData != null ? eventData.pointerPress : null)}, pointerClick={DescribeGameObject(eventData != null ? eventData.pointerClick : null)}, pointerEnter={DescribeGameObject(eventData != null ? eventData.pointerEnter : null)}");
        }

        _ = TryHandleProxyClickFallbackAsync("pointer-click");
    }

    public void NotifyProxySubmit(BaseEventData eventData)
    {
        if (_debugLog)
        {
            LogDiagnostic(
                $"Save submit. selectedObject={DescribeCurrentSelectedObject()}, currentInputModule={DescribeInputModule(eventData)}");
        }

        _ = TryHandleProxyClickFallbackAsync("submit");
    }

    private async Task TryHandleProxyClickFallbackAsync(string source)
    {
        await Task.Yield();

        if (_saveButton == null)
            return;

        if (!_saveButton.isActiveAndEnabled || !_saveButton.interactable)
            return;

        if (_saveButtonBlockedByOwnership || _isSaving)
            return;

        if (IsCurrentUserHost())
        {
            LogDiagnostic($"Save proxy fallback invoked. source={source}, selectedShareId={ResolveSelectedShareId()}");
            HandleSaveButtonClick();
        }
    }

    private void TryDisableOverlayRaycastBlockers()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        if (canvases == null || canvases.Length == 0)
            return;

        bool foundCandidate = false;
        bool changedAny = false;

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
                continue;

            if (!string.Equals(canvas.gameObject.name, OverlayCanvasName, StringComparison.Ordinal))
                continue;

            foundCandidate = true;

            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null && raycaster.enabled)
            {
                raycaster.enabled = false;
                changedAny = true;
            }

            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            for (int j = 0; j < graphics.Length; j++)
            {
                Graphic graphic = graphics[j];
                if (graphic == null || !graphic.raycastTarget)
                    continue;

                bool isRenderTarget = string.Equals(graphic.gameObject.name, OverlayRenderName, StringComparison.Ordinal);
                bool isCanvasChild = graphic.transform.IsChildOf(canvas.transform);
                if (!isRenderTarget && !isCanvasChild)
                    continue;

                graphic.raycastTarget = false;
                changedAny = true;
            }
        }

        if (!foundCandidate)
            return;

        _overlayRaycastBlockerSanitized = true;

        if (changedAny)
        {
            LogDiagnostic(
                $"Disabled overlay raycast blockers. canvas={OverlayCanvasName}, render={OverlayRenderName}");
        }
    }

    private void TryDisableConflictingButtonChildRaycasts()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        if (buttons == null || buttons.Length == 0)
            return;

        bool foundCandidate = false;
        bool changedAny = false;

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            if (!string.Equals(button.gameObject.name, UpdateButtonName, StringComparison.Ordinal))
                continue;

            foundCandidate = true;
            Graphic targetGraphic = button.targetGraphic;
            Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
            for (int j = 0; j < graphics.Length; j++)
            {
                Graphic graphic = graphics[j];
                if (graphic == null || !graphic.raycastTarget)
                    continue;

                if (ReferenceEquals(graphic, targetGraphic))
                    continue;

                graphic.raycastTarget = false;
                changedAny = true;
            }
        }

        if (!foundCandidate)
            return;

        _buttonChildRaycastSanitized = true;

        if (changedAny)
            LogDiagnostic($"Disabled conflicting child raycasts. button={UpdateButtonName}");
    }

    private void UpdateButtonInteractable()
    {
        bool interactable = ComputeButtonInteractable(out string reason);

        if (_saveButton != null)
            _saveButton.interactable = interactable;

        if (!_debugLog)
            return;

        string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "ready" : reason;
        if (_hasLoggedInteractableState &&
            _lastInteractableState == interactable &&
            string.Equals(_lastInteractableReason, normalizedReason, StringComparison.Ordinal))
        {
            return;
        }

        _hasLoggedInteractableState = true;
        _lastInteractableState = interactable;
        _lastInteractableReason = normalizedReason;
        LogDiagnostic(
            $"Save interactable={interactable}, reason={normalizedReason}, selectedShareId={ResolveSelectedShareId()}, isHost={IsCurrentUserHost()}, managerBound={_boundManager != null}");

        if (interactable)
            ProbeUiRaycastPath();
        else
            _lastRaycastProbeSummary = string.Empty;
    }

    private bool ComputeButtonInteractable(out string reason)
    {
        if (_saveButton == null)
        {
            reason = "save-button-null";
            return false;
        }

        if (_saveButtonBlockedByOwnership)
        {
            reason = "ownership-conflict";
            return false;
        }

        if (_disableButtonWhileSaving && _isSaving)
        {
            reason = "save-in-progress";
            return false;
        }

        if (_sourcePanel == null)
        {
            reason = "source-panel-null";
            return false;
        }

        if (!_sourcePanel.isActiveAndEnabled || !_sourcePanel.gameObject.activeInHierarchy)
        {
            reason = "source-panel-inactive";
            return false;
        }

        if (!IsCurrentUserHost())
        {
            reason = "not-host";
            return false;
        }

        if (_boundManager == null && ChatRoomManager.Instance == null)
        {
            reason = "manager-null";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_tokenOverride) ? null : _tokenOverride.Trim();
    }

    private void SetStatus(string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;

        if (_sourcePanel != null)
            _sourcePanel.SetStatus(text);

        if (_debugLog && !string.IsNullOrWhiteSpace(text))
            Debug.Log($"[BlockShareSaveToMyLevelButton] {text}");
    }

    private void ProbeUiRaycastPath()
    {
        if (!_debugLog || _saveButton == null || EventSystem.current == null)
            return;

        RectTransform buttonRect = _saveButton.transform as RectTransform;
        if (buttonRect == null)
            return;

        Canvas canvas = _saveButton.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        Vector3 worldCenter = buttonRect.TransformPoint(buttonRect.rect.center);
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCenter);

        var pointer = new PointerEventData(EventSystem.current)
        {
            position = screenPoint
        };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);

        string summary = BuildRaycastProbeSummary(results, screenPoint);
        if (string.Equals(_lastRaycastProbeSummary, summary, StringComparison.Ordinal))
            return;

        _lastRaycastProbeSummary = summary;
        LogDiagnostic(summary);
    }

    private string BuildRaycastProbeSummary(List<RaycastResult> results, Vector2 screenPoint)
    {
        if (results == null || results.Count == 0)
            return $"Save raycast probe: no hits. screen=({screenPoint.x:0.0}, {screenPoint.y:0.0})";

        bool saveReachable = IsResultWithinSaveButton(results[0]);
        int previewCount = Mathf.Min(results.Count, 6);
        string[] preview = new string[previewCount];
        for (int i = 0; i < previewCount; i++)
            preview[i] = BuildTransformPath(results[i].gameObject != null ? results[i].gameObject.transform : null);

        return
            $"Save raycast probe: reachable={saveReachable}, screen=({screenPoint.x:0.0}, {screenPoint.y:0.0}), top={preview[0]}, hits={results.Count}, path={string.Join(" -> ", preview)}";
    }

    private bool IsResultWithinSaveButton(RaycastResult result)
    {
        if (_saveButton == null || result.gameObject == null)
            return false;

        Transform hit = result.gameObject.transform;
        Transform buttonTransform = _saveButton.transform;
        return hit == buttonTransform || hit.IsChildOf(buttonTransform);
    }

    private static string BuildTransformPath(Transform transform)
    {
        if (transform == null)
            return "(null)";

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return path;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string DescribeInputModule(BaseEventData eventData)
    {
        BaseInputModule module = eventData != null ? eventData.currentInputModule : null;
        return module != null ? module.GetType().Name : "(null)";
    }
}

public sealed class BlockShareSaveButtonClickProxy :
    MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler,
    ISubmitHandler
{
    private BlockShareSaveToMyLevelButton _owner;

    public void Configure(BlockShareSaveToMyLevelButton owner)
    {
        _owner = owner;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _owner?.NotifyProxyPointerDown(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _owner?.NotifyProxyPointerUp(eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        _owner?.NotifyProxyPointerClick(eventData);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        _owner?.NotifyProxySubmit(eventData);
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
