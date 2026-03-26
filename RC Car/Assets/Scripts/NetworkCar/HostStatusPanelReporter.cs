using TMPro;
using UnityEngine;

public class HostStatusPanelReporter : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text _summaryText;
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private TMP_Text _errorText;
    [SerializeField] private TMP_Text _runtimeText;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private string _lastStatus = "Idle";
    private string _lastError = string.Empty;
    private string _lastRuntime = "-";

    public void UpdateSummary(int hostCount, int mappedCount, bool running, int currentSlot)
    {
        string summary = $"HostCount={hostCount} | Mapped={mappedCount} | Running={running} | CurrentSlot={currentSlot}";
        if (_summaryText != null)
            _summaryText.text = summary;
    }

    public void SetInfo(string message)
    {
        _lastStatus = Normalize(message, "Idle");
        if (_statusText != null)
            _statusText.text = _lastStatus;

        Log($"INFO: {_lastStatus}");
    }

    public void SetWarning(string message)
    {
        _lastStatus = Normalize(message, "Warning");
        if (_statusText != null)
            _statusText.text = _lastStatus;

        Log($"WARN: {_lastStatus}");
    }

    public void SetError(string message)
    {
        _lastError = Normalize(message, "Unknown error");
        if (_errorText != null)
            _errorText.text = _lastError;

        Log($"ERROR: {_lastError}");
    }

    public void SetRuntimeStatus(int slot, string userId, string state)
    {
        string normalizedUser = string.IsNullOrWhiteSpace(userId) ? "-" : userId.Trim();
        _lastRuntime = $"slot={slot}, user={normalizedUser}, state={Normalize(state, "-")}";
        if (_runtimeText != null)
            _runtimeText.text = _lastRuntime;

        Log($"RUNTIME: {_lastRuntime}");
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private void Log(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"[HostStatusPanelReporter] {message}");
    }
}

