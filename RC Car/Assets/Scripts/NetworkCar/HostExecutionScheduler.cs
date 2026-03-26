using System.Collections;
using UnityEngine;

public class HostExecutionScheduler : MonoBehaviour
{
    [Header("Runtime")]
    [SerializeField] private float _slotRunSeconds = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool _debugLog = true;

    private HostParticipantSlotRegistry _slotRegistry;
    private HostCarBindingStore _bindingStore;
    private HostRuntimeBinder _runtimeBinder;
    private HostStatusPanelReporter _statusReporter;
    private Coroutine _runRoutine;
    private int _currentSlot;

    public bool IsRunning => _runRoutine != null;
    public int CurrentSlot => _currentSlot;
    public float SlotRunSeconds => Mathf.Max(0.02f, _slotRunSeconds);

    public void Configure(
        HostParticipantSlotRegistry slotRegistry,
        HostCarBindingStore bindingStore,
        HostRuntimeBinder runtimeBinder,
        HostStatusPanelReporter statusReporter)
    {
        _slotRegistry = slotRegistry;
        _bindingStore = bindingStore;
        _runtimeBinder = runtimeBinder;
        _statusReporter = statusReporter;
    }

    public void StartExecution()
    {
        if (_runRoutine != null)
            return;

        _runRoutine = StartCoroutine(RunLoop());
        Log($"Execution started. slotRunSeconds={SlotRunSeconds:0.00}");
    }

    public void StopExecution()
    {
        if (_runRoutine == null)
            return;

        StopCoroutine(_runRoutine);
        _runRoutine = null;
        _currentSlot = 0;

        if (_runtimeBinder != null && _bindingStore != null)
            _runtimeBinder.StopAll(_bindingStore);

        _statusReporter?.SetInfo("Execution stopped.");
        Log("Execution stopped.");
    }

    private void OnDisable()
    {
        StopExecution();
    }

    private IEnumerator RunLoop()
    {
        float waitSeconds = SlotRunSeconds;

        while (true)
        {
            int maxCount = _slotRegistry != null ? _slotRegistry.MaxCount : 0;
            if (maxCount <= 0)
            {
                _statusReporter?.SetWarning("No approved slots. Waiting...");
                yield return new WaitForSeconds(waitSeconds);
                continue;
            }

            for (int slot = 1; slot <= maxCount; slot++)
            {
                _currentSlot = slot;

                if (_slotRegistry == null || !_slotRegistry.TryGetUserIdBySlot(slot, out string userId))
                {
                    _statusReporter?.SetRuntimeStatus(slot, "-", "empty-slot");
                    yield return new WaitForSeconds(waitSeconds);
                    continue;
                }

                if (_bindingStore == null || !_bindingStore.TryGetBinding(userId, out HostCarBinding binding) || binding == null)
                {
                    _statusReporter?.SetRuntimeStatus(slot, userId, "no-binding");
                    yield return new WaitForSeconds(waitSeconds);
                    continue;
                }

                if (!binding.HasCode || string.IsNullOrWhiteSpace(binding.Json))
                {
                    _statusReporter?.SetRuntimeStatus(slot, userId, "no-code");
                    yield return new WaitForSeconds(waitSeconds);
                    continue;
                }

                if (binding.RuntimeRefs == null || binding.RuntimeRefs.Executor == null || binding.RuntimeRefs.Physics == null)
                {
                    binding.LastError = "runtime refs missing";
                    _statusReporter?.SetError($"slot={slot}, user={userId}, runtime refs missing");
                    yield return new WaitForSeconds(waitSeconds);
                    continue;
                }

                if (!binding.RuntimeReady)
                {
                    string loadError;
                    bool loaded;
                    if (_runtimeBinder == null)
                    {
                        loaded = false;
                        loadError = "runtimeBinder is null";
                    }
                    else
                    {
                        loaded = _runtimeBinder.TryApplyJson(
                            binding.RuntimeRefs.Executor,
                            binding.Json,
                            $"slot={slot}:{userId}",
                            out loadError);
                    }

                    if (!loaded)
                    {
                        binding.LastError = loadError;
                        _statusReporter?.SetError($"slot={slot}, user={userId}, load failed: {loadError}");
                        yield return new WaitForSeconds(waitSeconds);
                        continue;
                    }

                    binding.RuntimeReady = true;
                }

                _runtimeBinder?.StartCar(binding.RuntimeRefs.Physics);
                _statusReporter?.SetRuntimeStatus(slot, userId, "running");
                yield return new WaitForSeconds(waitSeconds);
                _runtimeBinder?.StopCar(binding.RuntimeRefs.Physics);
                _statusReporter?.SetRuntimeStatus(slot, userId, "done");
            }
        }
    }

    private void Log(string message)
    {
        if (!_debugLog || string.IsNullOrWhiteSpace(message))
            return;

        Debug.Log($"[HostExecutionScheduler] {message}");
    }
}
