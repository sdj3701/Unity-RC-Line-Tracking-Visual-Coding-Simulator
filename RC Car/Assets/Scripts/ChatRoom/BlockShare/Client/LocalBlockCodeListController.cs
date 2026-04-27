using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class LocalBlockCodeListController : MonoBehaviour
{
    [SerializeField] private LocalBlockCodeListPanel _panel;
    [SerializeField] private int _fallbackUserLevelSeq = 1;
    [SerializeField] private bool _refreshOnEnable;
    [SerializeField] private bool _debugLog = true;

    private ILocalBlockCodeRepository _repository;
    private bool _isRefreshing;
    private bool _pendingRefresh;

    private void Awake()
    {
        if (_panel == null)
            _panel = GetComponent<LocalBlockCodeListPanel>();

        _repository = new BE2LocalBlockCodeRepository(new FileNameUserLevelSeqResolver(_fallbackUserLevelSeq));
    }

    private void OnEnable()
    {
        if (_panel != null)
            _panel.RefreshRequested += RequestRefresh;

        if (_refreshOnEnable)
            RequestRefresh();
    }

    private void OnDisable()
    {
        if (_panel != null)
            _panel.RefreshRequested -= RequestRefresh;
    }

    private void RequestRefresh()
    {
        _pendingRefresh = true;
        if (!_isRefreshing)
            _ = RefreshLoopAsync();
    }

    public bool TryGetSelectedEntry(out LocalBlockCodeEntry entry)
    {
        if (_panel != null)
            return _panel.TryGetSelectedEntry(out entry);

        entry = null;
        return false;
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
                IReadOnlyList<LocalBlockCodeEntry> entries = await _repository.GetEntriesAsync();
                if (_panel != null)
                {
                    _panel.RenderLocalFiles(entries);
                    _panel.SetStatus($"Loaded {entries.Count} local code item(s).");
                }
            }
            catch (Exception e)
            {
                if (_panel != null)
                {
                    _panel.RenderLocalFiles(Array.Empty<LocalBlockCodeEntry>());
                    _panel.SetStatus($"Failed to load local code list. ({e.Message})");
                }
            }
            finally
            {
                if (_panel != null)
                    _panel.SetBusy(false);

                _isRefreshing = false;
            }
        }

        if (_debugLog)
            Debug.Log("[LocalBlockCodeListController] Refresh loop completed.");
    }
}
