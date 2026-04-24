using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("Follow Mode")]
    public Transform target;
    public Vector3 offset = new Vector3(0f, 3f, -6f);
    public float smoothTime = 0.2f;

    [Header("Auto Target")]
    public bool autoResolveNetworkTarget = true;
    public float resolveTargetInterval = 0.5f;

    [Header("Fixed View Mode")]
    public Vector3 pointOfViewOffset = new Vector3(0f, 10f, 10f);
    public Vector3 TopDownViewOffset = new Vector3(0f, 17.5f, -10f);
    public Vector3 pointOfViewRotation = new Vector3(20f, 180f, 0f);
    public Vector3 TopDownViewRotation = new Vector3(90f, 0f, 0f);

    [Header("TopDown Mode")]
    public float topDownHeight = 15f;
    public bool isTopDownView = false;
    public bool isPointofview = true;
    private bool isFixedTopDown = false;

    private Vector3 velocity = Vector3.zero;
    private float _nextResolveAt;

    private void LateUpdate()
    {
        TryAutoResolveTarget();

        Vector3 desiredPos;
        Quaternion desiredRot;

        if (isPointofview)
        {
            if (isFixedTopDown)
            {
                desiredPos = pointOfViewOffset;
                desiredRot = Quaternion.Euler(pointOfViewRotation);
            }
            else
            {
                desiredPos = TopDownViewOffset;
                desiredRot = Quaternion.Euler(TopDownViewRotation);
            }

            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Time.deltaTime * 5f);
            return;
        }

        if (target == null)
            return;

        if (isTopDownView)
        {
            desiredPos = target.position + Vector3.up * topDownHeight;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);

            Quaternion targetRotation = Quaternion.Euler(90f, target.eulerAngles.y, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
            return;
        }

        desiredPos = target.TransformPoint(offset);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);
        transform.LookAt(target);
    }

    public void ToggleTopDownView()
    {
        isTopDownView = !isTopDownView;
        isPointofview = false;
        Debug.Log($"[FollowCamera] View Mode: {(isTopDownView ? "TopDown" : "Follow")}");
    }

    public void SetTopDownView(bool enabled)
    {
        isTopDownView = enabled;
        if (enabled)
            isPointofview = false;
    }

    public void TogglePointofview()
    {
        if (!isPointofview)
        {
            isPointofview = true;
            isFixedTopDown = false;
            isTopDownView = false;
        }
        else
        {
            isFixedTopDown = !isFixedTopDown;
        }

        string viewName = isFixedTopDown ? "TopDown Fixed" : "PointOfView Fixed";
        Debug.Log($"[FollowCamera] Fixed View Mode: {viewName}");
    }

    private void TryAutoResolveTarget()
    {
        if (!autoResolveNetworkTarget)
            return;

        if (target != null && HasPreferredAuthority(target))
            return;

        if (Time.unscaledTime < _nextResolveAt)
            return;

        _nextResolveAt = Time.unscaledTime + Mathf.Max(0.1f, resolveTargetInterval);

        Transform resolved = ResolvePreferredTarget();
        if (resolved != null)
            target = resolved;
    }

    private static Transform ResolvePreferredTarget()
    {
        NetworkRCCar[] cars = FindObjectsOfType<NetworkRCCar>(true);
        if (cars == null || cars.Length == 0)
            return null;

        Transform fallback = null;
        for (int i = 0; i < cars.Length; i++)
        {
            NetworkRCCar car = cars[i];
            if (car == null)
                continue;

            if (car.HasLocalInputAuthority)
                return car.transform;

            if (fallback == null)
                fallback = car.transform;
        }

        return fallback;
    }

    private static bool HasPreferredAuthority(Transform currentTarget)
    {
        if (currentTarget == null)
            return false;

        NetworkRCCar targetCar = currentTarget.GetComponent<NetworkRCCar>();
        if (targetCar == null)
            targetCar = currentTarget.GetComponentInParent<NetworkRCCar>();

        return targetCar != null && targetCar.HasLocalInputAuthority;
    }
}
