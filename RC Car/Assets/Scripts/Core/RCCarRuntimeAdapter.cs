using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RCCarRuntimeAdapter : MonoBehaviour, IRuntimeIO
{
    [Header("Sensors")]
    public GameObject[] sensors; // 0=left,1=right
    public int leftPin = 3;
    public int rightPin = 4;
    public float rayDistance = 2f;
    [Range(0f,1f)] public float blackThreshold = 0.2f;
    public bool whiteMeansTrue = true;

    [Header("Motor Pins")]
    public int pinLeftF = 9;
    public int pinLeftB = 6;
    public int pinRightF = 10;
    public int pinRightB = 11;

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
    
    // 개별 PWM 값 저장 (BlocksGenerated 방식)
    int _lfPwm, _lbPwm, _rfPwm, _rbPwm;
    float leftMotor, rightMotor;
    
    // 실행 상태
    bool isRunning = false;
    
    /// <summary>
    /// 현재 실행 중인지 확인
    /// </summary>
    public bool IsRunning => isRunning;

    void Awake() { rb = GetComponent<Rigidbody>(); }

    void Start()
    {
        // RuntimeBlocksRunner 찾기
        if (blocksRunner == null)
            blocksRunner = GetComponent<RuntimeBlocksRunner>();
        if (blocksRunner == null)
            blocksRunner = FindObjectOfType<RuntimeBlocksRunner>();
        
        // 블록 러너 초기화 (이 어댑터를 IRuntimeIO로 전달)
        if (blocksRunner != null)
        {
            blocksRunner.Initialize(this);
            Debug.Log("[RCCarRuntimeAdapter] RuntimeBlocksRunner initialized.");
        }
        else
        {
            Debug.LogWarning("[RCCarRuntimeAdapter] RuntimeBlocksRunner not found!");
        }
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
        
        // 2. 물리 이동 적용
        ApplyWheelVisualRotation(leftMotor, rightMotor);
        Vector3 move = transform.forward * (leftMotor + rightMotor) * 0.5f * maxLinearSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
        float angular = (rightMotor - leftMotor) * maxAngularSpeed * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, angular, 0f));
    }

    // IRuntimeIO
    public bool DigitalRead(int pin)
    {
        if (pin == leftPin) return SampleSensor(0);
        if (pin == rightPin) return SampleSensor(1);
        return false;
    }

    public void AnalogWrite(int pin, float value)
    {
        int pwm = Mathf.Clamp((int)value, 0, 255);
        
        // 각 핀별 PWM 저장 및 상호 배타 처리
        if (pin == pinLeftB) { _lbPwm = pwm; if (pwm > 0) _lfPwm = 0; }
        else if (pin == pinLeftF) { _lfPwm = pwm; if (pwm > 0) _lbPwm = 0; }
        else if (pin == pinRightF) { _rfPwm = pwm; if (pwm > 0) _rbPwm = 0; }
        else if (pin == pinRightB) { _rbPwm = pwm; if (pwm > 0) _rfPwm = 0; }
        else { return; }
        
        // 모터 값 계산 (Forward - Backward)
        leftMotor = Mathf.Clamp((_lfPwm - _lbPwm) / 255f, -1f, 1f);
        rightMotor = Mathf.Clamp((_rfPwm - _rbPwm) / 255f, -1f, 1f);
    }

    public void MoveForward(float speed01)
    {
        float s = Mathf.Clamp01(speed01);
        leftMotor = rightMotor = s;
    }

    public void TurnLeft(float speed01)
    {
        float s = Mathf.Clamp01(speed01);
        leftMotor = -s; rightMotor = s;
    }

    public void TurnRight(float speed01)
    {
        float s = Mathf.Clamp01(speed01);
        leftMotor = s; rightMotor = -s;
    }

    public void Stop()
    {
        leftMotor = rightMotor = 0f;
    }

    bool SampleSensor(int index)
    {
        if (sensors == null || index >= sensors.Length || sensors[index] == null)
            return whiteMeansTrue;
        var sensor = sensors[index];
        Vector3 origin = sensor.transform.position;
        Vector3 dir = -sensor.transform.up;
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, rayDistance))
            return whiteMeansTrue;
        var rend = hit.collider ? hit.collider.GetComponent<Renderer>() : null;
        var mat = rend ? rend.sharedMaterial : null;
        var tex = mat ? mat.mainTexture as Texture2D : null;
        if (tex == null) return whiteMeansTrue;
        var uv = hit.textureCoord;
        uv = Vector2.Scale(uv, mat.mainTextureScale) + mat.mainTextureOffset;
        uv.x -= Mathf.Floor(uv.x); uv.y -= Mathf.Floor(uv.y);
        float gray = tex.GetPixelBilinear(uv.x, uv.y).grayscale;
        bool isBlack = gray <= blackThreshold;
        return whiteMeansTrue ? !isBlack : isBlack;
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
