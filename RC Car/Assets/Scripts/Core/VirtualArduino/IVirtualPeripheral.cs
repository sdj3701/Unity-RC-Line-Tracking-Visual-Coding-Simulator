/// <summary>
/// 가상 주변장치 인터페이스
/// VirtualArduinoMicro에 연결되는 모든 주변장치가 구현해야 하는 인터페이스
/// </summary>
public interface IVirtualPeripheral
{
    /// <summary>
    /// 이 주변장치가 사용하는 핀 번호들
    /// </summary>
    int[] ConnectedPins { get; }
    
    /// <summary>
    /// 핀에 값이 쓰여질 때 호출됨 (analogWrite/digitalWrite)
    /// </summary>
    void OnPinWrite(int pin, float value);
    
    /// <summary>
    /// 핀 값을 읽을 때 호출됨 (digitalRead)
    /// </summary>
    bool OnPinRead(int pin);
    
    /// <summary>
    /// 핀 아날로그 값을 읽을 때 호출됨 (analogRead)
    /// </summary>
    float OnPinAnalogRead(int pin);
}
