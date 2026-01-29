using UnityEngine;

/// <summary>
/// RC Car 물리 시뮬레이션
/// VirtualMotorDriver의 모터 값을 읽어 실제 물리 이동에 적용합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VirtualCarPhysics : MonoBehaviour
{
    [Header("Motor Driver")]
    [Tooltip("모터 드라이버 (자동 탐색 가능)")]
    public VirtualMotorDriver motorDriver;
    
    [Header("Motion Settings")]
    [Tooltip("최대 선형 속도 (m/s)")]
    public float maxLinearSpeed = 5f;
    [Tooltip("최대 회전 속도 (deg/s)")]
    public float maxAngularSpeed = 120f;
    
    [Header("Wheel Visuals")]
    [Tooltip("휠 회전 속도 (deg/s)")]
    public float wheelVisualSpeed = 360f;
    [Tooltip("휠 회전 축")]
    public Vector3 wheelRotateAxis = Vector3.up;
    [Tooltip("바퀴 오브젝트들 (좌/우 순서)")]
    public GameObject[] wheels;
    
    Rigidbody rb;
    bool isRunning = false;
    
    /// <summary>
    /// 물리 시뮬레이션 실행 중 여부
    /// </summary>
    public bool IsRunning => isRunning;
    
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }
    
    [Header("Block Code Executor")]
    [Tooltip("블록 코드 실행기 (자동 탐색 가능)")]
    public BlockCodeExecutor blockCodeExecutor;
    
    void Start()
    {
        // VirtualMotorDriver 자동 탐색
        if (motorDriver == null)
            motorDriver = GetComponent<VirtualMotorDriver>();
        if (motorDriver == null)
            motorDriver = GetComponentInChildren<VirtualMotorDriver>();
        if (motorDriver == null)
            motorDriver = FindObjectOfType<VirtualMotorDriver>();
        
        // BlockCodeExecutor 자동 탐색
        if (blockCodeExecutor == null)
            blockCodeExecutor = FindObjectOfType<BlockCodeExecutor>();
        
        if (motorDriver == null)
            Debug.LogWarning("[VirtualCarPhysics] VirtualMotorDriver not found!");
        if (blockCodeExecutor == null)
            Debug.LogWarning("[VirtualCarPhysics] BlockCodeExecutor not found!");
        else
            Debug.Log("[VirtualCarPhysics] Initialized with VirtualMotorDriver and BlockCodeExecutor.");
    }
    
    // ============================================================
    // 공개 API
    // ============================================================
    
    /// <summary>
    /// 물리 시뮬레이션 시작
    /// </summary>
    public void StartRunning()
    {
        isRunning = true;
        Debug.Log("[VirtualCarPhysics] Started running.");
    }
    
    /// <summary>
    /// 물리 시뮬레이션 중지 및 모터 정지
    /// </summary>
    public void StopRunning()
    {
        isRunning = false;
        
        if (motorDriver != null)
        {
            motorDriver.SetMotorSpeed(0f, 0f);
        }
        
        Debug.Log("[VirtualCarPhysics] Stopped running.");
    }
    
    /// <summary>
    /// 실행 상태 토글
    /// </summary>
    public void ToggleRunning()
    {
        if (isRunning)
            StopRunning();
        else
            StartRunning();
    }
    
    // ============================================================
    // 물리 업데이트
    // ============================================================
    
    void FixedUpdate()
    {
        if (!isRunning) return;
        
        // 블록 코드 실행 (모터 값 업데이트)
        if (blockCodeExecutor != null && blockCodeExecutor.IsLoaded)
        {
            blockCodeExecutor.Tick();
        }
        else
        {
            Debug.LogWarning("<color=red>[Physics] BlockCodeExecutor not loaded!</color>");
        }
        
        if (motorDriver == null)
        {
            Debug.LogWarning("<color=red>[Physics] MotorDriver is NULL!</color>");
            return;
        }
        
        float leftMotor = motorDriver.LeftMotorSpeed;
        float rightMotor = motorDriver.RightMotorSpeed;
        
        Debug.Log($"<color=magenta>[5] VirtualCarPhysics: L={leftMotor:F2}, R={rightMotor:F2}</color>");
        
        // 바퀴 시각적 회전
        ApplyWheelVisualRotation(leftMotor, rightMotor);
        
        // 선형 이동: 좌우 모터 평균
        Vector3 move = transform.forward * (leftMotor + rightMotor) * 0.5f * maxLinearSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
        
        // 회전: 좌우 모터 차이
        float angular = (rightMotor - leftMotor) * maxAngularSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, -angular, 0f));
    }
    
    void ApplyWheelVisualRotation(float left, float right)
    {
        if (wheels == null || wheels.Length == 0) return;
        
        float dt = Time.fixedDeltaTime;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            if (!w) continue;
            
            // 짝수 인덱스는 왼쪽, 홀수 인덱스는 오른쪽
            float motorSpeed = (i % 2 == 0) ? left : right;
            w.transform.Rotate(wheelRotateAxis, motorSpeed * wheelVisualSpeed * dt, Space.Self);
        }
    }
}
