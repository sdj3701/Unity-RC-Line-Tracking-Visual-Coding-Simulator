using UnityEngine;

/// <summary>
/// 가상 라인 센서 (IR 센서)
/// 레이캐스트로 검은선/흰선을 감지합니다.
/// </summary>
public class VirtualLineSensor : MonoBehaviour, IVirtualPeripheral
{
    [Header("Pin Configuration")]
    [Tooltip("왼쪽 센서 핀")]
    public int pinLeft = 3;
    [Tooltip("오른쪽 센서 핀")]
    public int pinRight = 4;
    
    [Header("Sensor Objects")]
    [Tooltip("센서 위치 오브젝트들 (0=왼쪽, 1=오른쪽)")]
    public GameObject[] sensorObjects;
    
    [Header("Detection Settings")]
    [Tooltip("레이캐스트 거리")]
    public float rayDistance = 2f;
    [Tooltip("검은색 판단 임계값 (0~1)")]
    [Range(0f, 1f)] public float blackThreshold = 0.2f;
    [Tooltip("흰색일 때 true 반환")]
    public bool whiteMeansTrue = true;
    
    [Header("Debug")]
    [SerializeField] bool leftSensorValue;
    [SerializeField] bool rightSensorValue;
    
    // 연결된 핀 목록
    int[] connectedPins;
    
    void Awake()
    {
        connectedPins = new int[] { pinLeft, pinRight };
    }
    
    // ============================================================
    // IVirtualPeripheral 구현
    // ============================================================
    
    public int[] ConnectedPins => connectedPins ?? new int[] { pinLeft, pinRight };
    
    public void OnPinWrite(int pin, float value)
    {
        // 센서는 입력 전용 - 쓰기 무시
    }
    
    public bool OnPinRead(int pin)
    {
        if (pin == pinLeft)
        {
            leftSensorValue = SampleSensor(0);
            return leftSensorValue;
        }
        else if (pin == pinRight)
        {
            rightSensorValue = SampleSensor(1);
            return rightSensorValue;
        }
        return false;
    }
    
    public float OnPinAnalogRead(int pin)
    {
        // 디지털 센서이므로 0 또는 1 반환
        return OnPinRead(pin) ? 1f : 0f;
    }
    
    // ============================================================
    // 센서 샘플링 로직
    // ============================================================
    
    bool SampleSensor(int index)
    {
        if (sensorObjects == null || index >= sensorObjects.Length || sensorObjects[index] == null)
            return whiteMeansTrue;
        
        var sensor = sensorObjects[index];
        Vector3 origin = sensor.transform.position;
        Vector3 dir = -sensor.transform.up;
        
        // 레이캐스트
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, rayDistance))
            return whiteMeansTrue;
        
        // 텍스처 색상 샘플링
        var rend = hit.collider ? hit.collider.GetComponent<Renderer>() : null;
        var mat = rend ? rend.sharedMaterial : null;
        var tex = mat ? mat.mainTexture as Texture2D : null;
        
        if (tex == null) 
            return whiteMeansTrue;
        
        // UV 좌표 계산
        var uv = hit.textureCoord;
        uv = Vector2.Scale(uv, mat.mainTextureScale) + mat.mainTextureOffset;
        uv.x -= Mathf.Floor(uv.x); 
        uv.y -= Mathf.Floor(uv.y);
        
        // 그레이스케일 값으로 검은선 판단
        float gray = tex.GetPixelBilinear(uv.x, uv.y).grayscale;
        bool isBlack = gray <= blackThreshold;
        
        return whiteMeansTrue ? !isBlack : isBlack;
    }
    
    // ============================================================
    // 디버그 시각화
    // ============================================================
    
    void OnDrawGizmosSelected()
    {
        if (sensorObjects == null) return;
        
        foreach (var sensor in sensorObjects)
        {
            if (sensor == null) continue;
            
            Gizmos.color = Color.red;
            Vector3 origin = sensor.transform.position;
            Vector3 end = origin - sensor.transform.up * rayDistance;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawWireSphere(end, 0.05f);
        }
    }
}
