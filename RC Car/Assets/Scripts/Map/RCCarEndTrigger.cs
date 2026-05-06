using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Invisible finish trigger for an RC car course.
/// A car is treated as finished only after it enters this trigger and exits through transform.forward.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RCCarEndTrigger : MonoBehaviour
{
    [Header("Finish Target")]
    [SerializeField] private RCCarCourseController courseController;
    [SerializeField] private LayerMask carLayerMask = ~0;

    [Header("Finish Rule")]
    [Tooltip("If enabled, the same car can finish this trigger only once until ResetFinishState is called.")]
    [SerializeField] private bool finishOnce = true;

    [Tooltip("If enabled, the car must exit through this object's forward direction to finish.")]
    [SerializeField] private bool requireForwardPass = true;

    [Tooltip("If the car is a NetworkRCCar, only the state-authority side can finish it.")]
    [SerializeField] private bool requireNetworkStateAuthority = true;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private bool debugLog = false;

    private readonly Dictionary<VirtualCarPhysics, int> overlapCounts = new Dictionary<VirtualCarPhysics, int>();
    private readonly Dictionary<VirtualCarPhysics, float> entrySideByCar = new Dictionary<VirtualCarPhysics, float>();
    private readonly HashSet<VirtualCarPhysics> finishedCars = new HashSet<VirtualCarPhysics>();

    public RCCarCourseController CourseController => courseController;

    private void Reset()
    {
        EnsureTriggerCollider();
    }

    private void Awake()
    {
        EnsureTriggerCollider();
        ResolveCourseController();
    }

    private void OnValidate()
    {
        EnsureTriggerCollider();
    }

    private void OnDisable()
    {
        ResetFinishState();
    }

    private void OnTriggerEnter(Collider other)
    {
        VirtualCarPhysics physics = ResolveCarPhysics(other);
        if (physics == null)
        {
            return;
        }

        if (finishOnce && finishedCars.Contains(physics))
        {
            return;
        }

        int count;
        if (!overlapCounts.TryGetValue(physics, out count) || count <= 0)
        {
            entrySideByCar[physics] = GetSide(ResolveCarPosition(physics));
            overlapCounts[physics] = 1;
            Log($"enter car={physics.name}, entrySide={entrySideByCar[physics]:0.000}");
            return;
        }

        overlapCounts[physics] = count + 1;
        Log($"enter overlap car={physics.name}, count={overlapCounts[physics]}");
    }

    private void OnTriggerExit(Collider other)
    {
        VirtualCarPhysics physics = ResolveCarPhysics(other);
        if (physics == null)
        {
            return;
        }

        int count;
        if (!overlapCounts.TryGetValue(physics, out count))
        {
            return;
        }

        count--;
        if (count > 0)
        {
            overlapCounts[physics] = count;
            Log($"exit overlap car={physics.name}, remaining={count}");
            return;
        }

        overlapCounts.Remove(physics);

        if (finishOnce && finishedCars.Contains(physics))
        {
            entrySideByCar.Remove(physics);
            return;
        }

        if (requireForwardPass && !DidPassForward(physics))
        {
            Log($"exit ignored car={physics.name}, exitSide={GetSide(ResolveCarPosition(physics)):0.000}");
            entrySideByCar.Remove(physics);
            return;
        }

        entrySideByCar.Remove(physics);
        finishedCars.Add(physics);

        if (courseController != null)
        {
            courseController.FinishCar(physics);
        }
        else
        {
            StopCarFallback(physics);
        }

        Log($"finish car={physics.name}");
    }

    public void SetCourseController(RCCarCourseController controller)
    {
        courseController = controller;
    }

    public void ResetFinishState()
    {
        overlapCounts.Clear();
        entrySideByCar.Clear();
        finishedCars.Clear();
    }

    private void EnsureTriggerCollider()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void ResolveCourseController()
    {
        if (courseController != null)
        {
            return;
        }

        courseController = GetComponentInParent<RCCarCourseController>();
        if (courseController == null)
        {
            courseController = FindObjectOfType<RCCarCourseController>();
        }
    }

    private VirtualCarPhysics ResolveCarPhysics(Collider other)
    {
        if (other == null)
        {
            return null;
        }

        if (!IsInCarLayer(other.gameObject.layer))
        {
            return null;
        }

        VirtualCarPhysics physics = other.GetComponentInParent<VirtualCarPhysics>();
        if (physics == null)
        {
            return null;
        }

        if (ShouldIgnoreForNetworkAuthority(physics))
        {
            return null;
        }

        return physics;
    }

    private bool IsInCarLayer(int layer)
    {
        int layerMask = 1 << layer;
        return (carLayerMask.value & layerMask) != 0;
    }

    private bool DidPassForward(VirtualCarPhysics physics)
    {
        float entrySide;
        if (!entrySideByCar.TryGetValue(physics, out entrySide))
        {
            entrySide = 0f;
        }

        float exitSide = GetSide(ResolveCarPosition(physics));
        return entrySide <= 0f && exitSide > 0f;
    }

    private bool ShouldIgnoreForNetworkAuthority(VirtualCarPhysics physics)
    {
        if (!requireNetworkStateAuthority || physics == null)
        {
            return false;
        }

        NetworkRCCar networkCar = physics.GetComponentInParent<NetworkRCCar>();
        if (networkCar == null || networkCar.Object == null)
        {
            return false;
        }

        return !networkCar.Object.HasStateAuthority;
    }

    private Vector3 ResolveCarPosition(VirtualCarPhysics physics)
    {
        if (physics == null)
        {
            return transform.position;
        }

        Rigidbody rb = physics.GetComponent<Rigidbody>();
        return rb != null ? rb.worldCenterOfMass : physics.transform.position;
    }

    private float GetSide(Vector3 worldPosition)
    {
        return Vector3.Dot(worldPosition - transform.position, transform.forward);
    }

    private static void StopCarFallback(VirtualCarPhysics physics)
    {
        if (physics == null)
        {
            return;
        }

        physics.StopRunning();

        Rigidbody rb = physics.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos)
        {
            return;
        }

        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            return;
        }

        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(box.center, box.size);

        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }

    private void Log(string message)
    {
        if (!debugLog || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.Log($"[RCCarEndTrigger] {message}");
    }
}
