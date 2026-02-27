using UnityEngine;

/// <summary>
/// Virtual IR line sensor.
/// </summary>
public class VirtualLineSensor : MonoBehaviour, IVirtualPeripheral
{
    [Header("Sensor Objects")]
    [Tooltip("Sensor objects (0=left, 1=right).")]
    public GameObject[] sensorObjects;

    [Header("Detection Settings")]
    [Tooltip("Raycast distance.")]
    public float rayDistance = 2f;
    [Tooltip("Black detection threshold (0~1).")]
    [Range(0f, 1f)] public float blackThreshold = 0.2f;
    [Tooltip("Return true when white is detected.")]
    public bool whiteMeansTrue = true;
    [Tooltip("Extra sample radius around sensor center (world units).")]
    [Min(0f)] public float sampleRadius = 0.04f;
    [Tooltip("Additional samples around center. Recommended values: 0, 4, 8.")]
    [Range(0, 8)] public int extraSamples = 4;
    [Tooltip("Forward preview distance for sharper corner anticipation.")]
    [Min(0f)] public float forwardLookAheadDistance = 0.12f;
    [Tooltip("Additional samples at forward preview position.")]
    [Range(0, 8)] public int forwardLookAheadSamples = 4;
    [Tooltip("Keeps black detection for a short time to reduce missed corners.")]
    [Min(0f)] public float blackHoldSeconds = 0.06f;
    [Tooltip("Layer mask for sensor raycasts.")]
    [SerializeField] LayerMask sensorMask = ~0;
    [Tooltip("Ignore colliders that are part of this car hierarchy.")]
    [SerializeField] bool ignoreSelfColliders = true;

    [Header("Debug")]
    [SerializeField] bool leftSensorValue;
    [SerializeField] bool rightSensorValue;
    [SerializeField] bool leftOnBlack;
    [SerializeField] bool rightOnBlack;
    [SerializeField] bool leftBlackLatched;
    [SerializeField] bool rightBlackLatched;
    [SerializeField] float leftBlackUntilTime;
    [SerializeField] float rightBlackUntilTime;
    [SerializeField] bool logBlueOnBlackHit = true;

    static readonly string[] supportedFunctions = { "leftSensor", "rightSensor" };
    readonly RaycastHit[] raycastBuffer = new RaycastHit[16];

    public string[] SupportedFunctions => supportedFunctions;

    public void OnFunctionWrite(string function, float value)
    {
        // Input-only peripheral.
    }

    public bool OnFunctionRead(string function)
    {
        switch (function)
        {
            case "leftSensor":
                leftSensorValue = SampleSensor(0, "leftSensor", ref leftOnBlack, ref leftBlackUntilTime, ref leftBlackLatched);
                return leftSensorValue;
            case "rightSensor":
                rightSensorValue = SampleSensor(1, "rightSensor", ref rightOnBlack, ref rightBlackUntilTime, ref rightBlackLatched);
                return rightSensorValue;
            default:
                return false;
        }
    }

    public float OnFunctionAnalogRead(string function)
    {
        return OnFunctionRead(function) ? 1f : 0f;
    }

    bool SampleSensor(int index, string sensorName, ref bool wasBlackPreviously, ref float blackUntilTime, ref bool blackLatched)
    {
        if (sensorObjects == null || index >= sensorObjects.Length || sensorObjects[index] == null)
        {
            wasBlackPreviously = false;
            blackLatched = false;
            blackUntilTime = 0f;
            return whiteMeansTrue;
        }

        Transform sensor = sensorObjects[index].transform;
        bool rawIsBlack = DetectBlackByMultiSample(sensor, out float minGray, out Collider hitCollider);
        if (rawIsBlack)
        {
            blackUntilTime = Time.time + blackHoldSeconds;
        }

        blackLatched = !rawIsBlack && blackHoldSeconds > 0f && Time.time < blackUntilTime;
        bool isBlack = rawIsBlack || blackLatched;

        if (logBlueOnBlackHit && rawIsBlack && !wasBlackPreviously)
        {
            string hitName = hitCollider != null ? hitCollider.name : "unknown";
            Debug.Log($"<color=blue>[Sensor] {sensorName} touched black line (hit={hitName}, gray={minGray:F3})</color>");
        }

        wasBlackPreviously = isBlack;
        return whiteMeansTrue ? !isBlack : isBlack;
    }

    bool DetectBlackByMultiSample(Transform sensor, out float minGray, out Collider minGrayCollider)
    {
        Vector3 centerOrigin = sensor.position;
        Vector3 dir = -sensor.up;

        bool foundSurface = false;
        minGray = 1f;
        minGrayCollider = null;

        SamplePattern(centerOrigin, sensor.right, sensor.forward, dir, sampleRadius, extraSamples, ref foundSurface, ref minGray, ref minGrayCollider);

        if (forwardLookAheadDistance > 0f)
        {
            Vector3 lookAheadOrigin = centerOrigin + sensor.forward * forwardLookAheadDistance;
            SamplePattern(lookAheadOrigin, sensor.right, sensor.forward, dir, sampleRadius, forwardLookAheadSamples, ref foundSurface, ref minGray, ref minGrayCollider);
        }

        if (!foundSurface)
            return false;

        return minGray <= blackThreshold;
    }

    void SamplePattern(
        Vector3 origin,
        Vector3 rightDir,
        Vector3 forwardDir,
        Vector3 rayDir,
        float radius,
        int patternSamples,
        ref bool foundSurface,
        ref float minGray,
        ref Collider minGrayCollider)
    {
        SampleOrigin(origin, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);

        if (radius <= 0f || patternSamples <= 0)
            return;

        Vector3 right = rightDir * radius;
        Vector3 forward = forwardDir * radius;

        if (patternSamples >= 1) SampleOrigin(origin + right, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 2) SampleOrigin(origin - right, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 3) SampleOrigin(origin + forward, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 4) SampleOrigin(origin - forward, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 5) SampleOrigin(origin + right + forward, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 6) SampleOrigin(origin + right - forward, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 7) SampleOrigin(origin - right + forward, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
        if (patternSamples >= 8) SampleOrigin(origin - right - forward, rayDir, ref foundSurface, ref minGray, ref minGrayCollider);
    }

    void SampleOrigin(Vector3 origin, Vector3 dir, ref bool foundSurface, ref float minGray, ref Collider minGrayCollider)
    {
        if (!TryGetNearestHit(origin, dir, out RaycastHit hit))
            return;

        if (!TryGetHitGrayscale(hit, out float gray))
            return;

        foundSurface = true;
        if (gray < minGray)
        {
            minGray = gray;
            minGrayCollider = hit.collider;
        }
    }

    bool TryGetNearestHit(Vector3 origin, Vector3 dir, out RaycastHit nearestHit)
    {
        nearestHit = default;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            dir,
            raycastBuffer,
            rayDistance,
            sensorMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        float nearestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastBuffer[i];
            Collider col = hit.collider;
            if (col == null)
                continue;

            if (ignoreSelfColliders && col.transform.IsChildOf(transform.root))
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestHit = hit;
                found = true;
            }
        }

        return found;
    }

    bool TryGetHitGrayscale(RaycastHit hit, out float gray)
    {
        gray = 1f;

        if (hit.collider == null)
            return false;

        Renderer rend = hit.collider.GetComponent<Renderer>();
        if (rend == null)
            rend = hit.collider.GetComponentInParent<Renderer>();
        if (rend == null)
            return false;

        Material mat = rend.sharedMaterial;
        if (mat == null)
            return false;

        Texture2D tex = null;
        if (mat.mainTexture is Texture2D mainTex)
        {
            tex = mainTex;
        }
        else if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") is Texture2D baseTex)
        {
            tex = baseTex;
        }

        if (tex != null)
        {
            Vector2 uv = hit.textureCoord;
            uv = Vector2.Scale(uv, mat.mainTextureScale) + mat.mainTextureOffset;
            gray = SampleTextureNeighborhoodGray(tex, uv);
            return true;
        }

        if (mat.HasProperty("_BaseColor"))
        {
            gray = mat.GetColor("_BaseColor").grayscale;
            return true;
        }

        if (mat.HasProperty("_Color"))
        {
            gray = mat.color.grayscale;
            return true;
        }

        return false;
    }

    float SampleTextureNeighborhoodGray(Texture2D tex, Vector2 uv)
    {
        float du = 1f / Mathf.Max(1, tex.width);
        float dv = 1f / Mathf.Max(1, tex.height);

        float minGray = SampleTextureGray(tex, uv.x, uv.y);
        minGray = Mathf.Min(minGray, SampleTextureGray(tex, uv.x + du, uv.y));
        minGray = Mathf.Min(minGray, SampleTextureGray(tex, uv.x - du, uv.y));
        minGray = Mathf.Min(minGray, SampleTextureGray(tex, uv.x, uv.y + dv));
        minGray = Mathf.Min(minGray, SampleTextureGray(tex, uv.x, uv.y - dv));

        return minGray;
    }

    float SampleTextureGray(Texture2D tex, float u, float v)
    {
        u -= Mathf.Floor(u);
        v -= Mathf.Floor(v);
        return tex.GetPixelBilinear(u, v).grayscale;
    }

    void OnDrawGizmosSelected()
    {
        if (sensorObjects == null) return;

        foreach (var sensor in sensorObjects)
        {
            if (sensor == null) continue;

            Vector3 origin = sensor.transform.position;
            Vector3 end = origin - sensor.transform.up * rayDistance;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawWireSphere(end, 0.05f);

            if (sampleRadius > 0f && extraSamples > 0)
            {
                Gizmos.color = Color.yellow;
                Vector3 right = sensor.transform.right * sampleRadius;
                Vector3 forward = sensor.transform.forward * sampleRadius;

                if (extraSamples >= 1) Gizmos.DrawWireSphere(origin + right, 0.015f);
                if (extraSamples >= 2) Gizmos.DrawWireSphere(origin - right, 0.015f);
                if (extraSamples >= 3) Gizmos.DrawWireSphere(origin + forward, 0.015f);
                if (extraSamples >= 4) Gizmos.DrawWireSphere(origin - forward, 0.015f);
            }

            if (forwardLookAheadDistance > 0f)
            {
                Vector3 lookAheadOrigin = origin + sensor.transform.forward * forwardLookAheadDistance;
                Vector3 lookAheadEnd = lookAheadOrigin - sensor.transform.up * rayDistance;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(lookAheadOrigin, lookAheadEnd);
                Gizmos.DrawWireSphere(lookAheadOrigin, 0.02f);
            }
        }
    }
}
