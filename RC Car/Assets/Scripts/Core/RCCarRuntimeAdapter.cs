using UnityEngine;

/// <summary>
/// RC Car 물리 시뮬레이션 어댑터
/// VirtualArduinoMicro의 모터/센서 값을 물리 엔진에 적용합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RCCarRuntimeAdapter : MonoBehaviour
{
    [Header("Virtual Arduino")]
    [Tooltip("가상 아두이노 마이크로 (자동 탐색 가능)")]
    public VirtualArduinoMicro arduino;
    
    [Header("Virtual Peripherals")]
    [Tooltip("가상 모터 드라이버 (자동 탐색 가능)")]
    public VirtualMotorDriver motorDriver;
    [Tooltip("가상 라인 센서 (자동 탐색 가능)")]
    public VirtualLineSensor lineSensor;
    
    [Header("Motion")]
    public float maxLinearSpeed = 5f;
    public float maxAngularSpeed = 120f;
    public float wheelVisualSpeed = 360f;
    public Vector3 wheelRotateAxis = Vector3.up;
    public GameObject[] wheels;

    [Header("Block Runner")]
    [Tooltip("블록 프로그램 실행기 (같은 오브젝트 또는 씬에서 자동 탐색)")]
    public RuntimeBlocksRunner blocksRunner;

    Rigidbody rb;
    
    // 실행 상태
    bool isRunning = false;
    
    /// <summary>
    /// 현재 실행 중인지 확인
    /// </summary>
    public bool IsRunning => isRunning;

    void Awake() 
    { 
        rb = GetComponent<Rigidbody>(); 
    }

    void Start()
    {
        // VirtualArduinoMicro 찾기
        if (arduino == null)
            arduino = GetComponent<VirtualArduinoMicro>();
        if (arduino == null)
            arduino = FindObjectOfType<VirtualArduinoMicro>();
        
        // VirtualMotorDriver 찾기
        if (motorDriver == null)
            motorDriver = GetComponent<VirtualMotorDriver>();
        if (motorDriver == null)
            motorDriver = FindObjectOfType<VirtualMotorDriver>();
            
        // VirtualLineSensor 찾기
        if (lineSensor == null)
            lineSensor = GetComponent<VirtualLineSensor>();
        if (lineSensor == null)
            lineSensor = FindObjectOfType<VirtualLineSensor>();
        
        // RuntimeBlocksRunner 찾기
        if (blocksRunner == null)
            blocksRunner = GetComponent<RuntimeBlocksRunner>();
        if (blocksRunner == null)
            blocksRunner = FindObjectOfType<RuntimeBlocksRunner>();
        
        // 블록 러너 초기화 (VirtualArduinoMicro를 IRuntimeIO로 전달)
        if (blocksRunner != null && arduino != null)
        {
            blocksRunner.Initialize(arduino);
            Debug.Log("[RCCarRuntimeAdapter] RuntimeBlocksRunner initialized with VirtualArduinoMicro.");
        }
        else if (blocksRunner != null)
        {
            Debug.LogWarning("[RCCarRuntimeAdapter] VirtualArduinoMicro not found! Block runner not initialized.");
        }
        else
        {
            Debug.LogWarning("[RCCarRuntimeAdapter] RuntimeBlocksRunner not found!");
        }
        
        // 컴포넌트 상태 로그
        LogComponentStatus();
    }
    
    void LogComponentStatus()
    {
        Debug.Log($"[RCCarRuntimeAdapter] Components status:");
        Debug.Log($"  - VirtualArduinoMicro: {(arduino != null ? "OK" : "MISSING")}");
        Debug.Log($"  - VirtualMotorDriver: {(motorDriver != null ? "OK" : "MISSING")}");
        Debug.Log($"  - VirtualLineSensor: {(lineSensor != null ? "OK" : "MISSING")}");
        Debug.Log($"  - RuntimeBlocksRunner: {(blocksRunner != null ? "OK" : "MISSING")}");
    }

    // ============================================================
    // 공개 API (UI 버튼에서 호출)
    // ============================================================
    
    /// <summary>
    /// 블록 프로그램 실행 시작
    /// </summary>
    public void StartRunning()
    {
        isRunning = true;
        Debug.Log("[RCCarRuntimeAdapter] Started running.");
    }
    
    /// <summary>
    /// 블록 프로그램 실행 중지 및 모터 정지
    /// </summary>
    public void StopRunning()
    {
        isRunning = false;
        
        // 모터 정지
        if (motorDriver != null)
        {
            motorDriver.SetMotorSpeed(0f, 0f);
        }
        
        Debug.Log("[RCCarRuntimeAdapter] Stopped running.");
    }
    
    /// <summary>
    /// 실행 상태 토글 (하나의 버튼으로 Start/Stop)
    /// </summary>
    public void ToggleRunning()
    {
        if (isRunning)
            StopRunning();
        else
            StartRunning();
    }

    void FixedUpdate()
    {
        if (!isRunning) return;
        
        // 1. 블록 프로그램 평가 (센서 판단 → 모터 값 설정)
        if (blocksRunner != null && blocksRunner.IsReady)
        {
            blocksRunner.Tick();
        }
        
        // 2. 모터 드라이버에서 속도 읽기 및 물리 이동 적용
        float leftMotor = 0f;
        float rightMotor = 0f;
        
        if (motorDriver != null)
        {
            leftMotor = motorDriver.LeftMotorSpeed;
            rightMotor = motorDriver.RightMotorSpeed;
        }
        
        ApplyWheelVisualRotation(leftMotor, rightMotor);
        
        Vector3 move = transform.forward * (leftMotor + rightMotor) * 0.5f * maxLinearSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
        
        float angular = (rightMotor - leftMotor) * maxAngularSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, angular, 0f));
    }

    void ApplyWheelVisualRotation(float left, float right)
    {
        if (wheels == null || wheels.Length == 0) return;
        float dt = Time.fixedDeltaTime;
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            if (!w) continue;
            float m = (i % 2 == 0) ? left : right;
            w.transform.Rotate(wheelRotateAxis, m * wheelVisualSpeed * dt, Space.Self);
        }
    }
}
