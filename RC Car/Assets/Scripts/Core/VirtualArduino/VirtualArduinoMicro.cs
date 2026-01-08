using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가상 아두이노 마이크로 시뮬레이터
/// 동적 핀 매핑을 지원하여 블록에서 설정한 핀 번호가 올바른 기능과 연결됩니다.
/// </summary>
public class VirtualArduinoMicro : MonoBehaviour, IRuntimeIO
{
    public enum PinMode { Input, Output, InputPullup }
    
    [Header("Arduino Configuration")]
    [Tooltip("총 핀 개수 (Arduino Micro = 20)")]
    public int totalPins = 20;
    
    [Header("Default Pin Mapping")]
    [Tooltip("왼쪽 센서 기본 핀")]
    public int defaultLeftSensorPin = 3;
    [Tooltip("오른쪽 센서 기본 핀")]
    public int defaultRightSensorPin = 4;
    [Tooltip("왼쪽 모터 전진 기본 핀")]
    public int defaultLeftMotorFPin = 9;
    [Tooltip("왼쪽 모터 후진 기본 핀")]
    public int defaultLeftMotorBPin = 6;
    [Tooltip("오른쪽 모터 전진 기본 핀")]
    public int defaultRightMotorFPin = 10;
    [Tooltip("오른쪽 모터 후진 기본 핀")]
    public int defaultRightMotorBPin = 11;
    
    [Header("Connected Peripherals")]
    [Tooltip("연결된 가상 주변장치들")]
    public List<MonoBehaviour> peripheralComponents = new List<MonoBehaviour>();
    
    // 핀 상태 저장
    bool[] digitalPins;
    float[] analogPins;
    PinMode[] pinModes;
    
    // 동적 핀 매핑: 핀 번호 → 기능 이름
    Dictionary<int, string> pinToFunction = new Dictionary<int, string>();
    
    // 기능 → 주변장치 매핑
    Dictionary<string, IVirtualPeripheral> functionToPeripheral = new Dictionary<string, IVirtualPeripheral>();
    
    // 연결된 주변장치 목록
    List<IVirtualPeripheral> peripherals = new List<IVirtualPeripheral>();
    
    void Awake()
    {
        // 핀 배열 초기화
        digitalPins = new bool[totalPins];
        analogPins = new float[totalPins];
        pinModes = new PinMode[totalPins];
        
        // 주변장치 수집
        CollectPeripherals();
        
        // 기본 핀 매핑 설정
        SetupDefaultPinMapping();
    }
    
    void CollectPeripherals()
    {
        peripherals.Clear();
        functionToPeripheral.Clear();
        
        // Inspector에서 할당된 컴포넌트들 수집
        foreach (var comp in peripheralComponents)
        {
            if (comp is IVirtualPeripheral peripheral)
            {
                RegisterPeripheral(peripheral);
            }
        }
        
        // 자식 오브젝트에서 자동 탐색
        var childPeripherals = GetComponentsInChildren<IVirtualPeripheral>();
        foreach (var peripheral in childPeripherals)
        {
            if (!peripherals.Contains(peripheral))
            {
                RegisterPeripheral(peripheral);
            }
        }
        
        Debug.Log($"[VirtualArduinoMicro] Registered {peripherals.Count} peripherals, {functionToPeripheral.Count} functions");
    }
    
    void RegisterPeripheral(IVirtualPeripheral peripheral)
    {
        peripherals.Add(peripheral);
        
        // 기능 매핑 생성
        foreach (string function in peripheral.SupportedFunctions)
        {
            functionToPeripheral[function] = peripheral;
            Debug.Log($"[VirtualArduinoMicro] Function '{function}' → {peripheral.GetType().Name}");
        }
    }
    
    void SetupDefaultPinMapping()
    {
        // 기본 핀 → 기능 매핑 설정
        pinToFunction[defaultLeftSensorPin] = "leftSensor";
        pinToFunction[defaultRightSensorPin] = "rightSensor";
        pinToFunction[defaultLeftMotorFPin] = "leftMotorF";
        pinToFunction[defaultLeftMotorBPin] = "leftMotorB";
        pinToFunction[defaultRightMotorFPin] = "rightMotorF";
        pinToFunction[defaultRightMotorBPin] = "rightMotorB";
        
        Debug.Log($"[VirtualArduinoMicro] Default pin mapping:");
        foreach (var kvp in pinToFunction)
        {
            Debug.Log($"  Pin {kvp.Key} → {kvp.Value}");
        }
    }
    
    // ============================================================
    // 동적 핀 설정 API
    // ============================================================
    
    /// <summary>
    /// 핀 번호와 기능을 동적으로 매핑합니다.
    /// 블록에서 핀 번호를 변경하면 이 메서드로 매핑을 업데이트할 수 있습니다.
    /// </summary>
    /// <param name="pin">핀 번호</param>
    /// <param name="function">기능 이름 (예: "leftSensor", "rightMotorF")</param>
    public void ConfigurePin(int pin, string function)
    {
        // 기존 매핑에서 같은 기능을 가진 핀 제거
        int? oldPin = null;
        foreach (var kvp in pinToFunction)
        {
            if (kvp.Value == function)
            {
                oldPin = kvp.Key;
                break;
            }
        }
        if (oldPin.HasValue)
        {
            pinToFunction.Remove(oldPin.Value);
        }
        
        // 새 매핑 설정
        pinToFunction[pin] = function;
        Debug.Log($"[VirtualArduinoMicro] Pin {pin} → {function} (dynamic)");
    }
    
    /// <summary>
    /// 센서 핀을 동적으로 설정합니다.
    /// </summary>
    public void ConfigureSensorPins(int leftPin, int rightPin)
    {
        ConfigurePin(leftPin, "leftSensor");
        ConfigurePin(rightPin, "rightSensor");
    }
    
    /// <summary>
    /// 모터 핀을 동적으로 설정합니다.
    /// </summary>
    public void ConfigureMotorPins(int leftF, int leftB, int rightF, int rightB)
    {
        ConfigurePin(leftF, "leftMotorF");
        ConfigurePin(leftB, "leftMotorB");
        ConfigurePin(rightF, "rightMotorF");
        ConfigurePin(rightB, "rightMotorB");
    }
    
    // ============================================================
    // IRuntimeIO 구현
    // ============================================================
    
    public bool DigitalRead(int pin)
    {
        if (pin < 0 || pin >= totalPins) return false;
        
        // 핀 → 기능 → 주변장치 순으로 조회
        if (pinToFunction.TryGetValue(pin, out string function))
        {
            if (functionToPeripheral.TryGetValue(function, out var peripheral))
            {
                return peripheral.OnFunctionRead(function);
            }
        }
        
        return digitalPins[pin];
    }
    
    public void AnalogWrite(int pin, float value)
    {
        if (pin < 0 || pin >= totalPins) return;
        
        // 값 저장
        analogPins[pin] = Mathf.Clamp(value, 0f, 255f);
        digitalPins[pin] = value > 0;
        
        // 핀 → 기능 → 주변장치 순으로 전달
        if (pinToFunction.TryGetValue(pin, out string function))
        {
            if (functionToPeripheral.TryGetValue(function, out var peripheral))
            {
                peripheral.OnFunctionWrite(function, value);
            }
        }
    }
    
    // ============================================================
    // 고수준 명령 (블록 코딩용)
    // ============================================================
    
    public void MoveForward(float speed01)
    {
        foreach (var peripheral in peripherals)
        {
            if (peripheral is VirtualMotorDriver motor)
            {
                motor.SetMotorSpeed(speed01, speed01);
                return;
            }
        }
    }
    
    public void TurnLeft(float speed01)
    {
        foreach (var peripheral in peripherals)
        {
            if (peripheral is VirtualMotorDriver motor)
            {
                motor.SetMotorSpeed(-speed01, speed01);
                return;
            }
        }
    }
    
    public void TurnRight(float speed01)
    {
        foreach (var peripheral in peripherals)
        {
            if (peripheral is VirtualMotorDriver motor)
            {
                motor.SetMotorSpeed(speed01, -speed01);
                return;
            }
        }
    }
    
    public void Stop()
    {
        foreach (var peripheral in peripherals)
        {
            if (peripheral is VirtualMotorDriver motor)
            {
                motor.SetMotorSpeed(0f, 0f);
                return;
            }
        }
    }
    
    // ============================================================
    // Arduino 스타일 API (필요시 확장용)
    // ============================================================
    
    public void SetPinMode(int pin, PinMode mode)
    {
        if (pin >= 0 && pin < totalPins)
            pinModes[pin] = mode;
    }
    
    public void DigitalWrite(int pin, bool value)
    {
        if (pin >= 0 && pin < totalPins)
        {
            digitalPins[pin] = value;
            
            if (pinToFunction.TryGetValue(pin, out string function))
            {
                if (functionToPeripheral.TryGetValue(function, out var peripheral))
                {
                    peripheral.OnFunctionWrite(function, value ? 255f : 0f);
                }
            }
        }
    }
    
    public float AnalogRead(int pin)
    {
        if (pin < 0 || pin >= totalPins) return 0f;
        
        if (pinToFunction.TryGetValue(pin, out string function))
        {
            if (functionToPeripheral.TryGetValue(function, out var peripheral))
            {
                return peripheral.OnFunctionAnalogRead(function);
            }
        }
        
        return analogPins[pin];
    }
    
    // ============================================================
    // 디버그
    // ============================================================
    
    public string GetPinMappingDebugInfo()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Pin → Function Mapping:");
        foreach (var kvp in pinToFunction)
        {
            sb.AppendLine($"  Pin {kvp.Key} → {kvp.Value}");
        }
        return sb.ToString();
    }
}
