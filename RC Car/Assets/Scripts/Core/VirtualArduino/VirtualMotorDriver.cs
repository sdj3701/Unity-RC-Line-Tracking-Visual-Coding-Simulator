using UnityEngine;

/// <summary>
/// 가상 모터 드라이버 (L298N 스타일)
/// 4개의 PWM 핀으로 좌/우 모터를 제어합니다.
/// </summary>
public class VirtualMotorDriver : MonoBehaviour, IVirtualPeripheral
{
    [Header("Pin Configuration")]
    [Tooltip("왼쪽 모터 전진 핀")]
    public int pinLeftForward = 9;
    [Tooltip("왼쪽 모터 후진 핀")]
    public int pinLeftBackward = 6;
    [Tooltip("오른쪽 모터 전진 핀")]
    public int pinRightForward = 10;
    [Tooltip("오른쪽 모터 후진 핀")]
    public int pinRightBackward = 11;
    
    [Header("Output (Read-Only)")]
    [SerializeField] float leftMotorSpeed;
    [SerializeField] float rightMotorSpeed;
    
    // PWM 값 저장 (0-255)
    int pwmLeftForward, pwmLeftBackward;
    int pwmRightForward, pwmRightBackward;
    
    // 연결된 핀 목록
    int[] connectedPins;
    
    /// <summary>
    /// 현재 왼쪽 모터 속도 (-1 ~ 1)
    /// </summary>
    public float LeftMotorSpeed => leftMotorSpeed;
    
    /// <summary>
    /// 현재 오른쪽 모터 속도 (-1 ~ 1)
    /// </summary>
    public float RightMotorSpeed => rightMotorSpeed;
    
    void Awake()
    {
        connectedPins = new int[] { pinLeftForward, pinLeftBackward, pinRightForward, pinRightBackward };
    }
    
    // ============================================================
    // IVirtualPeripheral 구현
    // ============================================================
    
    public int[] ConnectedPins => connectedPins ?? new int[] { pinLeftForward, pinLeftBackward, pinRightForward, pinRightBackward };
    
    public void OnPinWrite(int pin, float value)
    {
        int pwm = Mathf.Clamp((int)value, 0, 255);
        
        // 각 핀별 PWM 저장 및 상호 배타 처리
        if (pin == pinLeftBackward) 
        { 
            pwmLeftBackward = pwm; 
            if (pwm > 0) pwmLeftForward = 0; 
        }
        else if (pin == pinLeftForward) 
        { 
            pwmLeftForward = pwm; 
            if (pwm > 0) pwmLeftBackward = 0; 
        }
        else if (pin == pinRightForward) 
        { 
            pwmRightForward = pwm; 
            if (pwm > 0) pwmRightBackward = 0; 
        }
        else if (pin == pinRightBackward) 
        { 
            pwmRightBackward = pwm; 
            if (pwm > 0) pwmRightForward = 0; 
        }
        
        // 모터 속도 계산 (Forward - Backward, 정규화)
        leftMotorSpeed = Mathf.Clamp((pwmLeftForward - pwmLeftBackward) / 255f, -1f, 1f);
        rightMotorSpeed = Mathf.Clamp((pwmRightForward - pwmRightBackward) / 255f, -1f, 1f);
    }
    
    public bool OnPinRead(int pin)
    {
        // 모터 드라이버는 출력 전용
        return false;
    }
    
    public float OnPinAnalogRead(int pin)
    {
        // 모터 드라이버는 출력 전용
        return 0f;
    }
    
    // ============================================================
    // 고수준 API (VirtualArduinoMicro에서 호출)
    // ============================================================
    
    /// <summary>
    /// 모터 속도 직접 설정 (-1 ~ 1)
    /// </summary>
    public void SetMotorSpeed(float left, float right)
    {
        leftMotorSpeed = Mathf.Clamp(left, -1f, 1f);
        rightMotorSpeed = Mathf.Clamp(right, -1f, 1f);
        
        // PWM 값 역계산 (동기화용)
        if (leftMotorSpeed >= 0)
        {
            pwmLeftForward = (int)(leftMotorSpeed * 255);
            pwmLeftBackward = 0;
        }
        else
        {
            pwmLeftForward = 0;
            pwmLeftBackward = (int)(-leftMotorSpeed * 255);
        }
        
        if (rightMotorSpeed >= 0)
        {
            pwmRightForward = (int)(rightMotorSpeed * 255);
            pwmRightBackward = 0;
        }
        else
        {
            pwmRightForward = 0;
            pwmRightBackward = (int)(-rightMotorSpeed * 255);
        }
    }
}
