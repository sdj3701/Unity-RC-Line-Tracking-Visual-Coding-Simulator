using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 가상 아두이노 마이크로 시뮬레이터
/// 핀 상태를 관리하고 연결된 주변장치와 통신합니다.
/// </summary>
public class VirtualArduinoMicro : MonoBehaviour, IRuntimeIO
{
    public enum PinMode { Input, Output, InputPullup }
    
    [Header("Arduino Configuration")]
    [Tooltip("총 핀 개수 (Arduino Micro = 20)")]
    public int totalPins = 20;
    
    [Header("Connected Peripherals")]
    [Tooltip("연결된 가상 주변장치들")]
    public List<MonoBehaviour> peripheralComponents = new List<MonoBehaviour>();
    
    // 핀 상태 저장
    bool[] digitalPins;
    float[] analogPins;
    PinMode[] pinModes;
    
    // 연결된 주변장치 (IVirtualPeripheral 인터페이스)
    List<IVirtualPeripheral> peripherals = new List<IVirtualPeripheral>();
    
    // 핀 → 주변장치 매핑 (빠른 조회용)
    Dictionary<int, IVirtualPeripheral> pinToPeripheral = new Dictionary<int, IVirtualPeripheral>();
    
    void Awake()
    {
        // 핀 배열 초기화
        digitalPins = new bool[totalPins];
        analogPins = new float[totalPins];
        pinModes = new PinMode[totalPins];
        
        // 주변장치 수집
        CollectPeripherals();
    }
    
    void CollectPeripherals()
    {
        peripherals.Clear();
        pinToPeripheral.Clear();
        
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
        
        Debug.Log($"[VirtualArduinoMicro] Registered {peripherals.Count} peripherals");
    }
    
    void RegisterPeripheral(IVirtualPeripheral peripheral)
    {
        peripherals.Add(peripheral);
        
        // 핀 매핑 생성
        foreach (int pin in peripheral.ConnectedPins)
        {
            if (pin >= 0 && pin < totalPins)
            {
                pinToPeripheral[pin] = peripheral;
                Debug.Log($"[VirtualArduinoMicro] Pin {pin} → {peripheral.GetType().Name}");
            }
        }
    }
    
    // ============================================================
    // IRuntimeIO 구현
    // ============================================================
    
    public bool DigitalRead(int pin)
    {
        if (pin < 0 || pin >= totalPins) return false;
        
        // 주변장치에서 읽기
        if (pinToPeripheral.TryGetValue(pin, out var peripheral))
        {
            return peripheral.OnPinRead(pin);
        }
        
        return digitalPins[pin];
    }
    
    public void AnalogWrite(int pin, float value)
    {
        if (pin < 0 || pin >= totalPins) return;
        
        // 값 저장
        analogPins[pin] = Mathf.Clamp(value, 0f, 255f);
        digitalPins[pin] = value > 0;
        
        // 주변장치에 전달
        if (pinToPeripheral.TryGetValue(pin, out var peripheral))
        {
            peripheral.OnPinWrite(pin, value);
        }
    }
    
    // ============================================================
    // 고수준 명령 (블록 코딩용)
    // 주변장치를 통해 간접 실행
    // ============================================================
    
    public void MoveForward(float speed01)
    {
        // 모터 드라이버를 찾아서 전달
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
            if (pinToPeripheral.TryGetValue(pin, out var peripheral))
            {
                peripheral.OnPinWrite(pin, value ? 255f : 0f);
            }
        }
    }
    
    public float AnalogRead(int pin)
    {
        if (pin < 0 || pin >= totalPins) return 0f;
        
        if (pinToPeripheral.TryGetValue(pin, out var peripheral))
        {
            return peripheral.OnPinAnalogRead(pin);
        }
        
        return analogPins[pin];
    }
}
