using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Auth;
using Fusion;
using Fusion.Sockets;
using RC.Network.Fusion;
using UnityEngine;
using UnityEngine.UI;

public class HostNetworkCarCoordinator : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("References")]
    [SerializeField] private ChatRoomManager _chatRoomManager;
    [SerializeField] private HostExecutionScheduler _executionScheduler;
    [SerializeField] private HostStatusPanelReporter _statusReporter;

    [Header("Spawn")]
    [SerializeField] private GameObject _carPrefab;
    [SerializeField] private Transform _carRoot;
    [SerializeField] private List<Transform> _slotSpawnPoints = new List<Transform>();

    [Header("Control Buttons")]
    [SerializeField] private Button _runButton;
    [SerializeField] private Button _stopButton;

    [Header("Option")]
    [SerializeField] private bool _hostOnly = true;
    [SerializeField] private bool _fetchJoinRequestsOnEnable = true;
    [SerializeField] private string _tokenOverride = string.Empty;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private readonly HostParticipantSlotRegistry _slotRegistry = new HostParticipantSlotRegistry();
    private readonly HostCarBindingStore _bindingStore = new HostCarBindingStore();
    private readonly HostCarColorAllocator _colorAllocator = new HostCarColorAllocator();
    private readonly HostRuntimeBinder _runtimeBinder = new HostRuntimeBinder();
    private readonly Dictionary<string, ChatRoomJoinRequestInfo> _joinRequestById =
        new Dictionary<string, ChatRoomJoinRequestInfo>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _shareToUserId =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, ChatRoomBlockShareSaveInfo> _pendingSaveByShareId =
        new Dictionary<string, ChatRoomBlockShareSaveInfo>(StringComparer.Ordinal);
    private readonly HashSet<string> _completedSaveKeys =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _inProgressSaveKeys =
        new HashSet<string>(StringComparer.Ordinal);

    private HostCarSpawner _carSpawner;
    private HostBlockCodeResolver _codeResolver;
    private bool _isBound;
    private float _nextPendingResolveTryAt;
    private NetworkRunner _boundRunner;
    private bool _runnerCallbacksBound;
    private readonly Dictionary<PlayerRef, string> _userIdByPlayer =
        new Dictionary<PlayerRef, string>();

    private static readonly string[] UserIdCandidateKeys =
    {
        "requestUserId",
        "request_user_id",
        "requesterUserId",
        "requester_user_id",
        "senderUserId",
        "sender_user_id",
        "userId",
        "user_id"
    };

    private void Awake()
    {
        EnsureServices();
    }

    private void OnEnable()
    {
        EnsureServices();
        BindButtons();
        BindManagerEvents();
        BindRunnerCallbacks();
        TryFetchJoinRequestsNow();
        SyncFusionParticipants();
        UpdateSummary();
    }

    private void OnDisable()
    {
        UnbindButtons();
        UnbindManagerEvents();
        UnbindRunnerCallbacks();

        if (_executionScheduler != null)
            _executionScheduler.StopExecution();
    }

    private void Update()
    {
        if (_executionScheduler != null && _executionScheduler.IsRunning)
            UpdateSummary();

        BindRunnerCallbacks();
        SyncFusionParticipants();
        TryResolvePendingShareOwners();
    }

    private void EnsureServices()
    {
        if (_chatRoomManager == null)
            _chatRoomManager = ChatRoomManager.Instance;

        if (_statusReporter == null)
            _statusReporter = GetComponent<HostStatusPanelReporter>();

        if (_executionScheduler == null)
            _executionScheduler = GetComponent<HostExecutionScheduler>();

        if (_executionScheduler == null)
            _executionScheduler = gameObject.AddComponent<HostExecutionScheduler>();

        if (_codeResolver == null)
            _codeResolver = new HostBlockCodeResolver(_debugLog);

        _carSpawner = new HostCarSpawner(_carPrefab, _carRoot, _slotSpawnPoints, _debugLog);
        _executionScheduler.Configure(_slotRegistry, _bindingStore, _runtimeBinder, _statusReporter);
    }

    private void BindButtons()
    {
        if (_runButton != null)
        {
            _runButton.onClick.RemoveListener(OnClickRun);
            _runButton.onClick.AddListener(OnClickRun);
        }

        if (_stopButton != null)
        {
            _stopButton.onClick.RemoveListener(OnClickStop);
            _stopButton.onClick.AddListener(OnClickStop);
        }
    }

    private void UnbindButtons()
    {
        if (_runButton != null)
            _runButton.onClick.RemoveListener(OnClickRun);

        if (_stopButton != null)
            _stopButton.onClick.RemoveListener(OnClickStop);
    }

    private void OnClickRun()
    {
        if (_hostOnly && !IsHost())
        {
            _statusReporter?.SetError("Current user is not host.");
            return;
        }

        if (_executionScheduler == null)
        {
            _statusReporter?.SetError("ExecutionScheduler is missing.");
            return;
        }

        _executionScheduler.StartExecution();
        _statusReporter?.SetInfo("Execution started by host.");
        UpdateSummary();
    }

    private void OnClickStop()
    {
        if (_executionScheduler == null)
            return;

        _executionScheduler.StopExecution();
        _statusReporter?.SetInfo("Execution stopped by host.");
        UpdateSummary();
    }

    private void BindManagerEvents()
    {
        if (_isBound)
            return;

        if (_chatRoomManager == null)
            _chatRoomManager = ChatRoomManager.Instance;

        if (_chatRoomManager == null)
        {
            _statusReporter?.SetWarning("ChatRoomManager.Instance is null.");
            return;
        }

        _chatRoomManager.OnBlockShareListFetchSucceeded += HandleBlockShareListFetchSucceeded;
        _chatRoomManager.OnBlockShareDetailFetchSucceeded += HandleBlockShareDetailFetchSucceeded;
        _chatRoomManager.OnBlockShareSaveSucceeded += HandleBlockShareSaveSucceeded;
        _chatRoomManager.OnBlockShareSaveFailed += HandleBlockShareSaveFailed;
        _chatRoomManager.OnBlockShareSaveCanceled += HandleBlockShareSaveCanceled;
        _isBound = true;
    }

    private void UnbindManagerEvents()
    {
        if (!_isBound || _chatRoomManager == null)
            return;

        _chatRoomManager.OnBlockShareListFetchSucceeded -= HandleBlockShareListFetchSucceeded;
        _chatRoomManager.OnBlockShareDetailFetchSucceeded -= HandleBlockShareDetailFetchSucceeded;
        _chatRoomManager.OnBlockShareSaveSucceeded -= HandleBlockShareSaveSucceeded;
        _chatRoomManager.OnBlockShareSaveFailed -= HandleBlockShareSaveFailed;
        _chatRoomManager.OnBlockShareSaveCanceled -= HandleBlockShareSaveCanceled;
        _isBound = false;
    }

    private void TryFetchJoinRequestsNow()
    {
        if (!_fetchJoinRequestsOnEnable || _chatRoomManager == null)
            return;

        if (FusionConnectionManager.Instance != null && FusionConnectionManager.Instance.IsInGameSession)
            return;

        if (_chatRoomManager.IsBusy)
            return;

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
            return;

        _chatRoomManager.FetchJoinRequests(roomId, ResolveTokenOverride());
    }

    private void BindRunnerCallbacks()
    {
        NetworkRunner runner = FusionConnectionManager.Instance != null
            ? FusionConnectionManager.Instance.Runner
            : null;

        if (runner == null || runner.IsShutdown)
        {
            UnbindRunnerCallbacks();
            return;
        }

        if (_runnerCallbacksBound && ReferenceEquals(_boundRunner, runner))
            return;

        UnbindRunnerCallbacks();
        runner.RemoveCallbacks(this);
        runner.AddCallbacks(this);
        _boundRunner = runner;
        _runnerCallbacksBound = true;

        if (_debugLog)
            Log($"Bound Fusion runner callbacks. session={runner.SessionInfo.Name}, isServer={runner.IsServer}, isClient={runner.IsClient}");
    }

    private void UnbindRunnerCallbacks()
    {
        if (!_runnerCallbacksBound || _boundRunner == null)
            return;

        _boundRunner.RemoveCallbacks(this);
        _boundRunner = null;
        _runnerCallbacksBound = false;
    }

    private void SyncFusionParticipants()
    {
        if (_boundRunner == null || !_boundRunner.IsRunning || _boundRunner.IsShutdown || !_boundRunner.IsServer)
            return;

        foreach (PlayerRef player in _boundRunner.ActivePlayers)
            EnsureParticipantForPlayer(_boundRunner, player, "active-player-sync");
    }

    private void EnsureParticipantForPlayer(NetworkRunner runner, PlayerRef player, string source)
    {
        if (runner == null || !runner.IsServer)
            return;

        string userId = ResolveFusionUserId(runner, player);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _statusReporter?.SetWarning($"Photon userId unresolved. player={player}, source={source}");
            return;
        }

        _userIdByPlayer[player] = userId;
        EnsureParticipantSlotAndCar(userId, userName: userId, source: source, ownerPlayer: player);
        UpdateSummary();
    }

    private string ResolveFusionUserId(NetworkRunner runner, PlayerRef player)
    {
        if (runner == null)
            return string.Empty;

        string userId = runner.GetPlayerUserId(player);
        if (!string.IsNullOrWhiteSpace(userId))
            return userId.Trim();

        if (player == runner.LocalPlayer &&
            AuthManager.Instance != null &&
            AuthManager.Instance.CurrentUser != null &&
            !string.IsNullOrWhiteSpace(AuthManager.Instance.CurrentUser.userId))
        {
            return AuthManager.Instance.CurrentUser.userId.Trim();
        }

        return string.Empty;
    }

    private void HandleJoinRequestsFetchSucceeded(ChatRoomJoinRequestInfo[] requests)
    {
        if (requests == null)
            return;

        for (int i = 0; i < requests.Length; i++)
        {
            ChatRoomJoinRequestInfo request = requests[i];
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
                continue;

            _joinRequestById[request.RequestId.Trim()] = request;
        }

        if (_debugLog)
            Log($"Join request cache updated. count={_joinRequestById.Count}");
    }

    private void HandleJoinRequestDecisionSucceeded(ChatRoomJoinRequestDecisionInfo info)
    {
        if (info == null || !info.Approved)
            return;

        string userId = ResolveApprovedUserId(info);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _statusReporter?.SetWarning(
                $"Join approved but userId unresolved. requestId={info.RequestId}, roomId={info.RoomId}");
            return;
        }

        EnsureParticipantSlotAndCar(userId, userName: userId, source: "approve", ownerPlayer: default);
        UpdateSummary();
    }

    private void HandleBlockShareListFetchSucceeded(ChatRoomBlockShareListInfo info)
    {
        ChatRoomBlockShareInfo[] items = info != null ? info.Items : null;
        if (items == null || items.Length == 0)
            return;

        for (int i = 0; i < items.Length; i++)
        {
            ChatRoomBlockShareInfo item = items[i];
            if (item == null)
                continue;

            if (string.IsNullOrWhiteSpace(item.UserId))
            {
                LogMappingNeeded($"Share list item userId is empty. shareId={item.BlockShareId}");
                continue;
            }

            RememberShareOwner(item.BlockShareId, item.UserId);
            RetryPendingSaveIfNeeded(item.BlockShareId);
        }
    }

    private void HandleBlockShareDetailFetchSucceeded(ChatRoomBlockShareInfo info)
    {
        if (info == null)
            return;

        if (string.IsNullOrWhiteSpace(info.UserId))
            LogMappingNeeded($"Share detail userId is empty. shareId={info.BlockShareId}");

        RememberShareOwner(info.BlockShareId, info.UserId);
        RetryPendingSaveIfNeeded(info.BlockShareId);
    }

    private void HandleBlockShareSaveSucceeded(ChatRoomBlockShareSaveInfo info)
    {
        _ = HandleBlockShareSaveSucceededAsync(info, fromPending: false);
    }

    private async Task HandleBlockShareSaveSucceededAsync(ChatRoomBlockShareSaveInfo info, bool fromPending)
    {
        if (info == null)
            return;

        string shareId = string.IsNullOrWhiteSpace(info.ShareId) ? string.Empty : info.ShareId.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
        {
            _statusReporter?.SetWarning("Save success event has empty shareId.");
            return;
        }

        int savedSeq = info.SavedUserLevelSeq;
        string saveKey = BuildSaveDedupeKey(shareId, savedSeq);
        if (_completedSaveKeys.Contains(saveKey))
        {
            if (_debugLog)
                Log($"Skip duplicated save event. shareId={shareId}, savedSeq={savedSeq}");
            return;
        }

        if (_inProgressSaveKeys.Contains(saveKey))
        {
            if (_debugLog)
                Log($"Skip in-progress duplicate save event. shareId={shareId}, savedSeq={savedSeq}");
            return;
        }

        if (!fromPending && IsPendingDuplicateSave(shareId, savedSeq))
        {
            if (_debugLog)
                Log($"Skip pending duplicate save event. shareId={shareId}, savedSeq={savedSeq}");
            return;
        }

        _inProgressSaveKeys.Add(saveKey);
        try
        {
            LogMappingNeeded(
                $"Mapping trigger. source={(fromPending ? "pending-retry" : "save-success")}, shareId={shareId}, savedSeq={savedSeq}");

            int preSlots = _slotRegistry.MaxCount;
            int preCars = _bindingStore.CountRuntimeCars();

            string userId = ResolveUserIdFromShare(shareId, info.OwnerUserId, info.ResponseBody);
            if (string.IsNullOrWhiteSpace(userId))
            {
                _pendingSaveByShareId[shareId] = info;
                RequestShareOwnerResolve(shareId);
                LogMappingNeeded($"Owner unresolved. shareId={shareId}, ownerUserId={info.OwnerUserId}, savedSeq={savedSeq}");
                _statusReporter?.SetWarning($"Save success but userId unresolved. shareId={shareId}, savedSeq={savedSeq}");
                return;
            }

            _pendingSaveByShareId.Remove(shareId);

            if (!_slotRegistry.TryGetSlotByUserId(userId, out HostParticipantSlot approvedSlot) || approvedSlot == null)
            {
                _completedSaveKeys.Add(saveKey);
                LogMappingNeeded($"Resolved user is not approved. shareId={shareId}, resolvedUser={userId}, savedSeq={savedSeq}");
                _statusReporter?.SetWarning($"save ignored: user not approved. shareId={shareId}, user={userId}, savedSeq={savedSeq}");
                LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
                return;
            }

            _bindingStore.UpsertParticipant(approvedSlot);

            if (_bindingStore.TryActivateExistingCodeVersion(
                    userId,
                    shareId,
                    savedSeq,
                    out HostCarBinding existingBinding,
                    out HostCodeVersion existingVersion))
            {
                if (existingBinding != null)
                {
                    existingBinding.SlotIndex = approvedSlot.SlotIndex;
                    existingBinding.UserName = approvedSlot.UserName;
                }

                _completedSaveKeys.Add(saveKey);
                LogMappingNeeded(
                    $"Reapply skipped. reason=duplicate-key, shareId={shareId}, savedSeq={savedSeq}, user={userId}");

                if (existingBinding != null &&
                    existingBinding.RuntimeRefs != null &&
                    existingBinding.RuntimeRefs.Executor != null &&
                    existingBinding.RuntimeRefs.Physics != null &&
                    !existingBinding.RuntimeReady &&
                    existingVersion != null &&
                    !string.IsNullOrWhiteSpace(existingVersion.Json))
                {
                    bool reloaded = _runtimeBinder.TryApplyJson(
                        existingBinding.RuntimeRefs.Executor,
                        existingVersion.Json,
                        $"save-duplicate:{shareId}:{userId}:version={existingVersion.VersionKey}",
                        out string duplicateLoadError);
                    existingBinding.RuntimeReady = reloaded;
                    if (!reloaded)
                    {
                        existingBinding.LastError = duplicateLoadError;
                        _statusReporter?.SetError(
                            $"Runtime load failed(duplicate). user={userId}, shareId={shareId}, error={duplicateLoadError}");
                    }
                }

                _statusReporter?.SetInfo(
                    $"Code already mapped. user={userId}, slot={approvedSlot.SlotIndex}, shareId={shareId}, savedSeq={savedSeq}");
                LogSaveTarget(shareId, savedSeq, userId, approvedSlot.SlotIndex, existingBinding != null ? existingBinding.RuntimeRefs : null);
                LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
                UpdateSummary();
                return;
            }

            string token = ResolveAccessToken();
            ResolvedCodePayload payload = await _codeResolver.ResolveBySavedSeqAsync(
                userId,
                shareId,
                savedSeq,
                token);

            if (payload == null || !payload.IsSuccess)
            {
                _completedSaveKeys.Add(saveKey);
                string error = payload != null ? payload.Error : "payload is null";
                if (_bindingStore.TryGetBinding(userId, out HostCarBinding failedBinding) && failedBinding != null)
                    failedBinding.LastError = error;

                _statusReporter?.SetError($"Code resolve failed. user={userId}, shareId={shareId}, error={error}");
                LogSaveTarget(shareId, savedSeq, userId, approvedSlot.SlotIndex, null);
                LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
                UpdateSummary();
                return;
            }

            HostCarBinding binding = _bindingStore.UpsertCode(payload);
            if (binding == null)
            {
                _completedSaveKeys.Add(saveKey);
                _statusReporter?.SetError($"Code mapping failed. user={userId}, shareId={shareId}, savedSeq={savedSeq}");
                LogSaveTarget(shareId, savedSeq, userId, approvedSlot.SlotIndex, null);
                LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
                UpdateSummary();
                return;
            }

            binding.SlotIndex = approvedSlot.SlotIndex;
            binding.UserName = approvedSlot.UserName;

            if (!binding.TryGetActiveCodeVersion(out HostCodeVersion activeVersion) ||
                activeVersion == null ||
                string.IsNullOrWhiteSpace(activeVersion.Json))
            {
                _completedSaveKeys.Add(saveKey);
                binding.LastError = "active json missing";
                _statusReporter?.SetError(
                    $"Code mapping failed(active json missing). user={userId}, slot={approvedSlot.SlotIndex}, shareId={shareId}");
                LogSaveTarget(shareId, savedSeq, userId, approvedSlot.SlotIndex, binding.RuntimeRefs);
                LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
                UpdateSummary();
                return;
            }

            if (binding.RuntimeRefs == null || binding.RuntimeRefs.Executor == null || binding.RuntimeRefs.Physics == null)
            {
                _completedSaveKeys.Add(saveKey);
                binding.LastError = "runtime refs missing";
                _statusReporter?.SetError($"Runtime refs missing. user={userId}, slot={approvedSlot.SlotIndex}, shareId={shareId}");
                LogSaveTarget(shareId, savedSeq, userId, approvedSlot.SlotIndex, binding.RuntimeRefs);
                LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
                UpdateSummary();
                return;
            }

            bool loaded = _runtimeBinder.TryApplyJson(
                binding.RuntimeRefs.Executor,
                activeVersion.Json,
                $"save:{shareId}:{userId}:version={activeVersion.VersionKey}",
                out string loadError);
            binding.RuntimeReady = loaded;
            if (!loaded)
            {
                binding.LastError = loadError;
                _statusReporter?.SetError($"Runtime load failed. user={userId}, shareId={shareId}, error={loadError}");
            }
            else
            {
                binding.LastError = string.Empty;
            }

            _completedSaveKeys.Add(saveKey);
            _statusReporter?.SetInfo(
                $"Code mapped. user={userId}, slot={approvedSlot.SlotIndex}, shareId={shareId}, savedSeq={savedSeq}");
            LogSaveTarget(shareId, savedSeq, userId, approvedSlot.SlotIndex, binding.RuntimeRefs);
            LogSaveIntegrity(shareId, savedSeq, preSlots, preCars);
            UpdateSummary();
        }
        finally
        {
            _inProgressSaveKeys.Remove(saveKey);
        }
    }

    private void RequestShareOwnerResolve(string shareIdRaw)
    {
        if (_chatRoomManager == null)
            return;

        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
            return;

        string roomId = ResolveTargetRoomId();
        if (string.IsNullOrWhiteSpace(roomId))
            return;

        if (_chatRoomManager.IsBusy)
        {
            if (_debugLog)
                Log($"Skip detail fetch for owner resolve (busy). shareId={shareId}");
            return;
        }

        _chatRoomManager.FetchBlockShareDetail(roomId, shareId, ResolveTokenOverride());
        if (_debugLog)
            Log($"Requested detail fetch to resolve owner. roomId={roomId}, shareId={shareId}");
    }

    private void RetryPendingSaveIfNeeded(string shareIdRaw)
    {
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
            return;

        if (!_pendingSaveByShareId.TryGetValue(shareId, out ChatRoomBlockShareSaveInfo pending) || pending == null)
            return;

        _pendingSaveByShareId.Remove(shareId);
        _ = HandleBlockShareSaveSucceededAsync(pending, fromPending: true);
    }

    private void TryResolvePendingShareOwners()
    {
        if (_pendingSaveByShareId.Count <= 0)
            return;

        if (Time.unscaledTime < _nextPendingResolveTryAt)
            return;

        _nextPendingResolveTryAt = Time.unscaledTime + 0.5f;

        foreach (KeyValuePair<string, ChatRoomBlockShareSaveInfo> pair in _pendingSaveByShareId)
        {
            RequestShareOwnerResolve(pair.Key);
            break;
        }
    }

    private static string BuildSaveDedupeKey(string shareIdRaw, int savedSeq)
    {
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        string versionKey = HostCarBindingStore.BuildVersionKey(shareId, savedSeq);
        if (!string.IsNullOrWhiteSpace(versionKey))
            return versionKey;

        return $"{shareId}:{savedSeq}";
    }

    private bool IsPendingDuplicateSave(string shareIdRaw, int savedSeq)
    {
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId))
            return false;

        if (!_pendingSaveByShareId.TryGetValue(shareId, out ChatRoomBlockShareSaveInfo pending) || pending == null)
            return false;

        return pending.SavedUserLevelSeq == savedSeq;
    }

    private void LogSaveTarget(string shareId, int savedSeq, string userId, int slotIndex, HostCarRuntimeRefs refs)
    {
        if (!_debugLog)
            return;

        string carName = refs != null && refs.CarObject != null ? refs.CarObject.name : "-";
        Log($"Save target. shareId={shareId}, savedSeq={savedSeq}, resolvedUserId={userId}, resolvedSlot={slotIndex}, carObjectName={carName}");
    }

    private void LogSaveIntegrity(string shareId, int savedSeq, int preSlots, int preCars)
    {
        int postSlots = _slotRegistry.MaxCount;
        int postCars = _bindingStore.CountRuntimeCars();

        if (_debugLog)
        {
            Log(
                $"Save integrity. shareId={shareId}, savedSeq={savedSeq}, preSlots={preSlots}, preCars={preCars}, postSlots={postSlots}, postCars={postCars}");
        }

        if (postSlots != preSlots || postCars != preCars)
        {
            _statusReporter?.SetWarning(
                $"save integrity changed. shareId={shareId}, savedSeq={savedSeq}, slots {preSlots}->{postSlots}, cars {preCars}->{postCars}");
        }
    }

    private void HandleBlockShareSaveFailed(string shareId, string message)
    {
        _statusReporter?.SetWarning($"Save failed. shareId={shareId}, message={message}");
    }

    private void HandleBlockShareSaveCanceled(string shareId)
    {
        _statusReporter?.SetWarning($"Save canceled. shareId={shareId}");
    }

    private void EnsureParticipantSlotAndCar(string userIdRaw, string userName, string source, PlayerRef ownerPlayer)
    {
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(userId))
            return;

        bool created = _slotRegistry.TryRegisterUser(userId, userName, out HostParticipantSlot slot);
        if (slot == null && !_slotRegistry.TryGetSlotByUserId(userId, out slot))
        {
            _statusReporter?.SetError($"Slot resolve failed. user={userId}");
            return;
        }

        HostCarBinding binding = _bindingStore.UpsertParticipant(slot);
        Color color = _colorAllocator.Resolve(slot.SlotIndex);
        HostCarRuntimeRefs refs = _carSpawner.EnsureCarForSlot(
            slot.SlotIndex,
            userId,
            ownerPlayer,
            binding != null ? binding.RuntimeRefs : null,
            color);
        _bindingStore.UpsertRuntimeRefs(userId, refs, color);

        if (created)
            _statusReporter?.SetInfo($"Slot created. source={source}, user={userId}, slot={slot.SlotIndex}");
    }

    private void RememberShareOwner(string shareIdRaw, string userIdRaw)
    {
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        string userId = string.IsNullOrWhiteSpace(userIdRaw) ? string.Empty : userIdRaw.Trim();
        if (string.IsNullOrWhiteSpace(shareId) || string.IsNullOrWhiteSpace(userId))
            return;

        _shareToUserId[shareId] = userId;
        LogMappingNeeded($"Share owner cached. shareId={shareId}, userId={userId}");
    }

    private string ResolveApprovedUserId(ChatRoomJoinRequestDecisionInfo info)
    {
        if (info == null)
            return string.Empty;

        string requestId = string.IsNullOrWhiteSpace(info.RequestId) ? string.Empty : info.RequestId.Trim();
        if (!string.IsNullOrWhiteSpace(requestId) &&
            _joinRequestById.TryGetValue(requestId, out ChatRoomJoinRequestInfo cached) &&
            cached != null &&
            !string.IsNullOrWhiteSpace(cached.RequestUserId))
        {
            return cached.RequestUserId.Trim();
        }

        return ExtractFirstUserId(info.ResponseBody);
    }

    private string ResolveUserIdFromShare(string shareIdRaw, string ownerUserIdRaw, string responseBody)
    {
        string shareId = string.IsNullOrWhiteSpace(shareIdRaw) ? string.Empty : shareIdRaw.Trim();
        if (!string.IsNullOrWhiteSpace(shareId) &&
            _shareToUserId.TryGetValue(shareId, out string mappedUserId) &&
            !string.IsNullOrWhiteSpace(mappedUserId))
        {
            string mapped = mappedUserId.Trim();
            if (_slotRegistry.TryGetSlotByUserId(mapped, out HostParticipantSlot mappedSlot) && mappedSlot != null)
                return mapped;

            LogMappingNeeded($"Share owner is not approved. shareId={shareId}, mappedUserId={mapped}");
        }

        string ownerUserId = string.IsNullOrWhiteSpace(ownerUserIdRaw) ? string.Empty : ownerUserIdRaw.Trim();
        if (!string.IsNullOrWhiteSpace(ownerUserId))
        {
            if (_slotRegistry.TryGetSlotByUserId(ownerUserId, out HostParticipantSlot ownerSlot) && ownerSlot != null)
                return ownerUserId;

            LogMappingNeeded($"OwnerUserId is not approved. shareId={shareId}, ownerUserId={ownerUserId}");
        }

        string parsedUserId = ExtractFirstUserId(responseBody);
        if (!string.IsNullOrWhiteSpace(parsedUserId))
        {
            string parsed = parsedUserId.Trim();
            if (_slotRegistry.TryGetSlotByUserId(parsed, out HostParticipantSlot parsedSlot) && parsedSlot != null)
                return parsed;

            LogMappingNeeded($"Parsed userId is not approved. shareId={shareId}, parsedUserId={parsed}");
        }

        return string.Empty;
    }

    private static string ExtractFirstUserId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        for (int i = 0; i < UserIdCandidateKeys.Length; i++)
        {
            string value = ExtractJsonScalarAsString(json, UserIdCandidateKeys[i]);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string ExtractJsonScalarAsString(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
            return null;

        string pattern = $"\"{Regex.Escape(key)}\"\\s*:\\s*(\"(?<s>(?:\\\\.|[^\"\\\\])*)\"|(?<n>-?\\d+(?:\\.\\d+)?)|(?<b>true|false)|null)";
        Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        Group s = match.Groups["s"];
        if (s != null && s.Success)
            return Regex.Unescape(s.Value);

        Group n = match.Groups["n"];
        if (n != null && n.Success)
            return n.Value;

        Group b = match.Groups["b"];
        if (b != null && b.Success)
            return b.Value;

        return null;
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

        if (AuthManager.Instance == null)
            return string.Empty;

        string token = AuthManager.Instance.GetAccessToken();
        return string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
    }

    private string ResolveTargetRoomId()
    {
        FusionRoomSessionInfo fusionContext = FusionRoomSessionContext.Current;
        if (fusionContext != null && !string.IsNullOrWhiteSpace(fusionContext.SessionName))
            return fusionContext.SessionName.Trim();

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom != null && !string.IsNullOrWhiteSpace(currentRoom.RoomId))
            return currentRoom.RoomId.Trim();

        return string.Empty;
    }

    private bool IsHost()
    {
        if (_boundRunner != null && _boundRunner.IsRunning && !_boundRunner.IsShutdown)
            return _boundRunner.IsServer;

        FusionRoomSessionInfo context = FusionRoomSessionContext.Current;
        if (context != null)
            return context.IsHost || context.GameMode == GameMode.Host;

        RoomInfo currentRoom = RoomSessionContext.CurrentRoom;
        if (currentRoom == null || string.IsNullOrWhiteSpace(currentRoom.HostUserId))
            return false;

        if (AuthManager.Instance == null || AuthManager.Instance.CurrentUser == null)
            return false;

        string hostUserId = currentRoom.HostUserId.Trim();
        string currentUserId = string.IsNullOrWhiteSpace(AuthManager.Instance.CurrentUser.userId)
            ? string.Empty
            : AuthManager.Instance.CurrentUser.userId.Trim();

        return string.Equals(hostUserId, currentUserId, StringComparison.Ordinal);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        EnsureParticipantForPlayer(runner, player, "player-joined");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_userIdByPlayer.TryGetValue(player, out string userId))
        {
            _statusReporter?.SetInfo($"Photon player left. user={userId}, player={player}");
            _userIdByPlayer.Remove(player);
        }

        UpdateSummary();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        BindRunnerCallbacks();
        SyncFusionParticipants();
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        _statusReporter?.SetWarning($"Photon disconnected. reason={reason}");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (_boundRunner == runner)
        {
            _boundRunner = null;
            _runnerCallbacksBound = false;
        }

        _statusReporter?.SetWarning($"Photon runner shutdown. reason={shutdownReason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        BindRunnerCallbacks();
        SyncFusionParticipants();
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    private void UpdateSummary()
    {
        if (_statusReporter == null)
            return;

        int maxCount = _slotRegistry.MaxCount;
        int mappedCount = _bindingStore.CountMappedCode();
        bool running = _executionScheduler != null && _executionScheduler.IsRunning;
        int currentSlot = _executionScheduler != null ? _executionScheduler.CurrentSlot : 0;
        _statusReporter.UpdateSummary(maxCount, mappedCount, running, currentSlot);
    }

    private void Log(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"[HostNetworkCarCoordinator] {message}");
    }

    private void LogMappingNeeded(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"<color=orange>[HostNetworkCarCoordinator][MAPPING] {message}</color>");
    }
}

