using System;
using System.Collections.Generic;
using UnityEngine;

public enum RCCarCourseState
{
    Ready,
    Running,
    Finished
}

/// <summary>
/// Coordinates course start/end state and stops the RC car when it finishes.
/// </summary>
public class RCCarCourseController : MonoBehaviour
{
    [Header("End Triggers")]
    [SerializeField] private RCCarEndTrigger[] endTriggersByMap;
    [SerializeField] private bool autoFindEndTriggersOnStart = true;
    [SerializeField] private bool disableInactiveEndTriggers = true;
    [SerializeField] private bool resetCourseOnMapApply = true;

    [Header("Finish Policy")]
    [SerializeField] private bool finishWholeCourseOnFirstCar = true;
    [SerializeField] private bool stopCarOnFinish = true;
    [SerializeField] private bool stopAllCarsOnFinish = true;
    [SerializeField] private bool stopRigidbodyOnFinish = true;
    [SerializeField] private bool invokeHostStopButtonOnFinish = true;
    [SerializeField] private bool beginRunIfFinishArrivesBeforeBegin = true;

    [Header("Optional References")]
    [SerializeField] private Transform carSearchRoot;
    [SerializeField] private HostNetworkCarCoordinator hostNetworkCarCoordinator;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    [SerializeField] private RCCarCourseState state = RCCarCourseState.Ready;
    [SerializeField] private int currentMapIndex = -1;
    [SerializeField] private float runStartedAt = -1f;
    [SerializeField] private float lastFinishElapsedSeconds = 0f;
    [SerializeField] private VirtualCarPhysics lastFinishedCar;

    private readonly HashSet<VirtualCarPhysics> finishedCars = new HashSet<VirtualCarPhysics>();

    public event Action<RCCarCourseState> StateChanged;
    public event Action<VirtualCarPhysics, float> CarFinished;

    public RCCarCourseState State => state;
    public int CurrentMapIndex => currentMapIndex;
    public float LastFinishElapsedSeconds => lastFinishElapsedSeconds;
    public VirtualCarPhysics LastFinishedCar => lastFinishedCar;
    public IReadOnlyList<RCCarEndTrigger> EndTriggersByMap => endTriggersByMap;

    private void Awake()
    {
        EnsureEndTriggers();
        BindEndTriggers();
    }

    private void Start()
    {
        EnsureEndTriggers();
        BindEndTriggers();
    }

    public void BeginRun()
    {
        finishedCars.Clear();
        ResetAllEndTriggerStates();
        lastFinishedCar = null;
        lastFinishElapsedSeconds = 0f;
        runStartedAt = Time.time;
        SetState(RCCarCourseState.Running);
        Log("Run started.");
    }

    public void FinishCar(VirtualCarPhysics physics)
    {
        if (physics == null)
        {
            return;
        }

        if (finishWholeCourseOnFirstCar && state == RCCarCourseState.Finished)
        {
            return;
        }

        if (finishedCars.Contains(physics))
        {
            return;
        }

        if (state == RCCarCourseState.Ready && beginRunIfFinishArrivesBeforeBegin)
        {
            runStartedAt = Time.time;
            SetState(RCCarCourseState.Running);
        }

        finishedCars.Add(physics);
        lastFinishedCar = physics;
        lastFinishElapsedSeconds = runStartedAt >= 0f ? Mathf.Max(0f, Time.time - runStartedAt) : 0f;

        if (stopCarOnFinish)
        {
            StopFinishedCars(physics);
        }

        if (finishWholeCourseOnFirstCar)
        {
            SetState(RCCarCourseState.Finished);
        }

        CarFinished?.Invoke(physics, lastFinishElapsedSeconds);
        Log($"Car finished. car={physics.name}, elapsed={lastFinishElapsedSeconds:0.000}, map={currentMapIndex}");
    }

    public void ResetCourse()
    {
        finishedCars.Clear();
        ResetAllEndTriggerStates();
        lastFinishedCar = null;
        lastFinishElapsedSeconds = 0f;
        runStartedAt = -1f;
        SetState(RCCarCourseState.Ready);
        Log("Course reset.");
    }

    public void ApplyMapEndTrigger(int mapIndex)
    {
        currentMapIndex = mapIndex;
        EnsureEndTriggers();
        BindEndTriggers();

        if (endTriggersByMap == null || endTriggersByMap.Length == 0)
        {
            LogWarning("No end triggers are assigned.");
            if (resetCourseOnMapApply)
            {
                ResetCourse();
            }
            return;
        }

        bool hasMatchingTrigger = mapIndex >= 0 &&
            mapIndex < endTriggersByMap.Length &&
            endTriggersByMap[mapIndex] != null;

        if (disableInactiveEndTriggers)
        {
            for (int i = 0; i < endTriggersByMap.Length; i++)
            {
                RCCarEndTrigger trigger = endTriggersByMap[i];
                if (trigger == null)
                {
                    continue;
                }

                trigger.gameObject.SetActive(hasMatchingTrigger && i == mapIndex);
            }
        }

        if (!hasMatchingTrigger)
        {
            LogWarning($"No end trigger is assigned for map index {mapIndex}.");
        }

        if (resetCourseOnMapApply)
        {
            ResetCourse();
        }

        Log($"Map end trigger applied. map={mapIndex}, hasTrigger={hasMatchingTrigger}");
    }

    public void SetEndTriggers(RCCarEndTrigger[] triggers)
    {
        endTriggersByMap = triggers;
        BindEndTriggers();
    }

    private void EnsureEndTriggers()
    {
        if (!autoFindEndTriggersOnStart)
        {
            return;
        }

        if (endTriggersByMap != null && endTriggersByMap.Length > 0)
        {
            return;
        }

        endTriggersByMap = GetComponentsInChildren<RCCarEndTrigger>(true);
    }

    private void BindEndTriggers()
    {
        if (endTriggersByMap == null)
        {
            return;
        }

        for (int i = 0; i < endTriggersByMap.Length; i++)
        {
            RCCarEndTrigger trigger = endTriggersByMap[i];
            if (trigger != null)
            {
                trigger.SetCourseController(this);
            }
        }
    }

    private void ResetAllEndTriggerStates()
    {
        if (endTriggersByMap == null)
        {
            return;
        }

        for (int i = 0; i < endTriggersByMap.Length; i++)
        {
            RCCarEndTrigger trigger = endTriggersByMap[i];
            if (trigger != null)
            {
                trigger.ResetFinishState();
            }
        }
    }

    private void StopFinishedCars(VirtualCarPhysics finishedPhysics)
    {
        if (stopAllCarsOnFinish)
        {
            StopAllCars(finishedPhysics);
            return;
        }

        StopCarCompletely(finishedPhysics);
    }

    private void StopAllCars(VirtualCarPhysics firstCar)
    {
        if (TryInvokeHostStopButton())
        {
            return;
        }

        var stoppedCars = new HashSet<VirtualCarPhysics>();
        int stoppedCount = 0;

        if (firstCar != null)
        {
            StopCarCompletely(firstCar);
            stoppedCars.Add(firstCar);
            stoppedCount++;
        }

        VirtualCarPhysics[] cars = ResolveCarsToStop();
        for (int i = 0; i < cars.Length; i++)
        {
            VirtualCarPhysics car = cars[i];
            if (car == null || stoppedCars.Contains(car))
            {
                continue;
            }

            StopCarCompletely(car);
            stoppedCars.Add(car);
            stoppedCount++;
        }

        Log($"All cars stopped. count={stoppedCount}");
    }

    private VirtualCarPhysics[] ResolveCarsToStop()
    {
        if (carSearchRoot != null)
        {
            return carSearchRoot.GetComponentsInChildren<VirtualCarPhysics>(true);
        }

        return FindObjectsOfType<VirtualCarPhysics>();
    }

    private bool TryInvokeHostStopButton()
    {
        if (!invokeHostStopButtonOnFinish)
        {
            return false;
        }

        if (hostNetworkCarCoordinator == null)
        {
            hostNetworkCarCoordinator = FindObjectOfType<HostNetworkCarCoordinator>();
        }

        if (hostNetworkCarCoordinator == null)
        {
            return false;
        }

        hostNetworkCarCoordinator.StopHostExecutionFromButton();
        Log("Invoked host stop button flow.");
        return true;
    }

    private void StopCarCompletely(VirtualCarPhysics physics)
    {
        if (physics == null)
        {
            return;
        }

        physics.StopRunning();

        if (!stopRigidbodyOnFinish)
        {
            return;
        }

        Rigidbody rb = physics.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = physics.GetComponentInParent<Rigidbody>();
        }

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void SetState(RCCarCourseState newState)
    {
        if (state == newState)
        {
            return;
        }

        state = newState;
        StateChanged?.Invoke(state);
    }

    private void Log(string message)
    {
        if (!debugLog || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.Log($"[RCCarCourseController] {message}");
    }

    private void LogWarning(string message)
    {
        if (!debugLog || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.LogWarning($"[RCCarCourseController] {message}");
    }
}
