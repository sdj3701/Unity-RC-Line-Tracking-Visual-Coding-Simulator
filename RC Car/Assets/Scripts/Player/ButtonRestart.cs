using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Car 오브젝트의 위치를 초기 상태로 되돌리는 버튼 기능
/// </summary>
public class ButtonRestart : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("리셋할 Car 오브젝트 (미지정 시 'Car' 태그 또는 이름으로 자동 탐색)")]
    public Transform carTransform;
    
    [Header("Optional References")]
    [Tooltip("물리 시뮬레이션 컴포넌트 (자동 탐색 가능)")]
    public VirtualCarPhysics carPhysics;
    
    // 초기 상태 저장용
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool isInitialized = false;
    
    void Start()
    {
        // Car Transform 자동 탐색
        if (carTransform == null)
        {
            // 태그로 먼저 시도
            GameObject carObj = GameObject.FindGameObjectWithTag("Car");
            if (carObj == null)
            {
                // 이름으로 시도
                carObj = GameObject.Find("Car");
            }
            if (carObj != null)
            {
                carTransform = carObj.transform;
            }
        }
        
        // VirtualCarPhysics 자동 탐색
        if (carPhysics == null && carTransform != null)
        {
            carPhysics = carTransform.GetComponent<VirtualCarPhysics>();
        }
        if (carPhysics == null)
        {
            carPhysics = FindObjectOfType<VirtualCarPhysics>();
        }
        
        // 초기 위치/회전 저장
        SaveInitialState();
        
        // 버튼 클릭 이벤트 연결
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(RestartCar);
        }
    }
    
    /// <summary>
    /// Car의 초기 위치와 회전을 저장합니다.
    /// </summary>
    public void SaveInitialState()
    {
        if (carTransform != null)
        {
            initialPosition = carTransform.position;
            initialRotation = carTransform.rotation;
            isInitialized = true;
            Debug.Log($"[ButtonRestart] Initial state saved - Position: {initialPosition}, Rotation: {initialRotation.eulerAngles}");
        }
        else
        {
            Debug.LogWarning("[ButtonRestart] Car Transform not found! Cannot save initial state.");
        }
    }
    
    /// <summary>
    /// Car를 초기 위치로 되돌립니다.
    /// 버튼의 OnClick 이벤트에 연결하세요.
    /// </summary>
    public void RestartCar()
    {
        if (!isInitialized)
        {
            Debug.LogWarning("[ButtonRestart] Initial state not saved. Cannot restart.");
            return;
        }
        
        if (carTransform == null)
        {
            Debug.LogWarning("[ButtonRestart] Car Transform is null. Cannot restart.");
            return;
        }
        
        // 물리 시뮬레이션 정지
        if (carPhysics != null)
        {
            carPhysics.StopRunning();
        }
        
        // Rigidbody가 있으면 속도 초기화
        Rigidbody rb = carTransform.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        // 위치 및 회전 복원
        carTransform.position = initialPosition;
        carTransform.rotation = initialRotation;
        
        Debug.Log($"[ButtonRestart] Car restarted to initial position: {initialPosition}");
    }
    
    /// <summary>
    /// 새로운 초기 위치를 설정합니다.
    /// </summary>
    public void SetNewInitialPosition(Vector3 position, Quaternion rotation)
    {
        initialPosition = position;
        initialRotation = rotation;
        isInitialized = true;
        Debug.Log($"[ButtonRestart] New initial position set: {position}");
    }
}
