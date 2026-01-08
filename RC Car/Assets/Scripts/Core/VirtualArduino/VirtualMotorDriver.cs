using UnityEngine;

/// <summary>
/// 가상 모터 드라이버 (L298N 스타일)
/// 기능 이름으로 등록되어 동적 핀 매핑을 지원합니다.
/// </summary>
public class VirtualMotorDriver : MonoBehaviour, IVirtualPeripheral
{
    [Header("Output (Read-Only)")]
    [SerializeField] float leftMotorSpeed;
    [SerializeField] float rightMotorSpeed;
    
    // PWM 값 저장 (0-255)
    int pwmLeftForward, pwmLeftBackward;
    int pwmRightForward, pwmRightBackward;
    
    // 지원하는 기능 이름들
    static readonly string[] supportedFunctions = { "leftMotorF", "leftMotorB", "rightMotorF", "rightMotorB" };
    
    /// <summary>
    /// 현재 왼쪽 모터 속도 (-1 ~ 1)
    /// </summary>
    public float LeftMotorSpeed => leftMotorSpeed;
    
    /// <summary>
    /// 현재 오른쪽 모터 속도 (-1 ~ 1)
    /// </summary>
    public float RightMotorSpeed => rightMotorSpeed;
    
    // ============================================================
    // IVirtualPeripheral 구현
    // ============================================================
    
    public string[] SupportedFunctions => supportedFunctions;
    
    public void OnFunctionWrite(string function, float value)
    {
        int pwm = Mathf.Clamp((int)value, 0, 255);
        
        switch (function)
        {
            case "leftMotorF":
                pwmLeftForward = pwm;
                if (pwm > 0) pwmLeftBackward = 0;
                break;
            case "leftMotorB":
                pwmLeftBackward = pwm;
                if (pwm > 0) pwmLeftForward = 0;
                break;
            case "rightMotorF":
                pwmRightForward = pwm;
                if (pwm > 0) pwmRightBackward = 0;
                break;
            case "rightMotorB":
                pwmRightBackward = pwm;
                if (pwm > 0) pwmRightForward = 0;
                break;
        }
        
        // 모터 속도 계산 (Forward - Backward, 정규화)
        leftMotorSpeed = Mathf.Clamp((pwmLeftForward - pwmLeftBackward) / 255f, -1f, 1f);
        rightMotorSpeed = Mathf.Clamp((pwmRightForward - pwmRightBackward) / 255f, -1f, 1f);
    }
    
    public bool OnFunctionRead(string function)
    {
        // 모터 드라이버는 출력 전용
        return false;
    }
    
    public float OnFunctionAnalogRead(string function)
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
