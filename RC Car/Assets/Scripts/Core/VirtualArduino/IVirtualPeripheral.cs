/// <summary>
/// 가상 주변장치 인터페이스
/// VirtualArduinoMicro에 연결되는 모든 주변장치가 구현해야 하는 인터페이스
/// </summary>
public interface IVirtualPeripheral
{
    /// <summary>
    /// 이 주변장치가 지원하는 기능 이름들
    /// 예: ["leftSensor", "rightSensor"] 또는 ["leftMotorF", "leftMotorB", "rightMotorF", "rightMotorB"]
    /// </summary>
    string[] SupportedFunctions { get; }
    
    /// <summary>
    /// 특정 기능에 값을 쓸 때 호출됨 (analogWrite/digitalWrite)
    /// </summary>
    /// <param name="function">기능 이름 (예: "leftMotorF")</param>
    /// <param name="value">PWM 값 (0-255)</param>
    void OnFunctionWrite(string function, float value);
    
    /// <summary>
    /// 특정 기능의 디지털 값을 읽을 때 호출됨 (digitalRead)
    /// </summary>
    /// <param name="function">기능 이름 (예: "leftSensor")</param>
    /// <returns>센서 값</returns>
    bool OnFunctionRead(string function);
    
    /// <summary>
    /// 특정 기능의 아날로그 값을 읽을 때 호출됨 (analogRead)
    /// </summary>
    /// <param name="function">기능 이름</param>
    /// <returns>아날로그 값 (0-1023)</returns>
    float OnFunctionAnalogRead(string function);
}
