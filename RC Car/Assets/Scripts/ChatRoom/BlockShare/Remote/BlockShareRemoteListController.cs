using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class BlockShareRemoteListController : MonoBehaviour
{
    [SerializeField] private BlockShareRemoteListPanel _panel;
    [SerializeField] private string _roomIdOverride = string.Empty;
    [SerializeField] private string _tokenOverride = string.Empty;
    [SerializeField] private bool _autoRoomFromSession = true;
    [SerializeField] private bool _refreshOnEnable = true;
    [SerializeField] private bool _pollOnEnable = true;
    [SerializeField] private float _pollIntervalSeconds = 5f;
    [SerializeField] private int _defaultPage = 1;
    [SerializeField] private int _defaultSize = 20;
    [SerializeField] private bool _debugLog = true;

    private IRoomIdProvider _roomIdProvider;
    private IBlockShareListService _listService;
    private Coroutine _pollRoutine;
    private bool _isRefreshing;
    private bool _pendingRefresh;

    private void Awake()
    {
        if (_panel == null)
            _panel = GetComponent<BlockShareRemoteListPanel>();

        _roomIdProvider = new FusionRoomIdProvider(_roomIdOverride, _autoRoomFromSession);
        _listService = new ChatRoomBlockShareListService();
    }

    private void OnEnable()
    {
        if (_panel != null)
            _panel.RefreshRequested += RequestRefresh;

        if (_refreshOnEnable)
            RequestRefresh();

        StartPollingIfNeeded();
    }

    private void OnDisable()
    {
        if (_panel != null)
            _panel.RefreshRequested -= RequestRefresh;

        StopPolling();
    }

    public void RequestRefresh()
    {
        _pendingRefresh = true;
        if (!_isRefreshing)
            _ = RefreshLoopAsync();
    }

    private void StartPollingIfNeeded()
    {
        if (!_pollOnEnable || _pollRoutine != null)
            return;

        _pollRoutine = StartCoroutine(PollRoutine());
    }

    private void StopPolling()
    {
        if (_pollRoutine == null)
            return;

        StopCoroutine(_pollRoutine);
        _pollRoutine = null;
    }

    private IEnumerator PollRoutine()
    {
        var wait = new WaitForSeconds(Mathf.Max(1f, _pollIntervalSeconds));
        while (enabled)
        {
            RequestRefresh();
            yield return wait;
        }

        _pollRoutine = null;
    }

    private async Task RefreshLoopAsync()
    {
        while (_pendingRefresh)
        {
            _pendingRefresh = false;
            _isRefreshing = true;

            if (_panel != null)
                _panel.SetBusy(true);

            try
            {
                string roomId = _roomIdProvider != null ? _roomIdProvider.GetRoomId() : string.Empty;
                if (string.IsNullOrWhiteSpace(roomId))
                {
                    if (_panel != null)
                        _panel.SetStatus("API roomId is empty.");
                    continue;
                }

                IReadOnlyList<BlockShareListItemViewModel> items = await _listService.FetchListAsync(
                    roomId,
                    Mathf.Max(1, _defaultPage),
                    Mathf.Max(1, _defaultSize),
                    ResolveTokenOverride());

                if (_panel != null)
                {
                    _panel.RenderRemoteShares(items);
                    _panel.SetStatus($"Loaded {items.Count} remote share item(s).");
                }
            }
            catch (InvalidOperationException e) when (IsBusyMessage(e))
            {
                _pendingRefresh = true;
                await Task.Delay(250);
            }
            catch (OperationCanceledException)
            {
                if (_panel != null)
                    _panel.SetStatus("Remote share list refresh canceled.");
            }
            catch (Exception e)
            {
                if (_panel != null)
                    _panel.SetStatus($"Remote share list refresh failed. ({e.Message})");
            }
            finally
            {
                if (_panel != null)
                    _panel.SetBusy(false);

                _isRefreshing = false;
            }
        }

        if (_debugLog)
            Debug.Log("[BlockShareRemoteListController] Refresh loop completed.");
    }

    private string ResolveTokenOverride()
    {
        return string.IsNullOrWhiteSpace(_tokenOverride)
            ? null
            : _tokenOverride.Trim();
    }

    private static bool IsBusyMessage(Exception exception)
    {
        return exception != null &&
               exception.Message.IndexOf("busy", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
