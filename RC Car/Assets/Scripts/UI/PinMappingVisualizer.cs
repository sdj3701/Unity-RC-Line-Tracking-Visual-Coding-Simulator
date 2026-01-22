using UnityEngine;
using TMPro;

/// <summary>
/// 아두이노 핀 매핑 정보를 UI 라벨에 표시합니다.
/// 
/// 사용법:
/// 1. Unity Editor에서 핀 라벨(TextMeshProUGUI)들을 아두이노 이미지 위에 배치
/// 2. 이 스크립트를 PinConnectionPanel에 추가
/// 3. Inspector에서 VirtualArduinoMicro와 각 라벨 연결
/// </summary>
public class PinMappingVisualizer : MonoBehaviour
{
    [Header("Arduino Reference")]
    [SerializeField] VirtualArduinoMicro arduino;
    
    [Header("Sensor Pin Labels")]
    [SerializeField] TextMeshProUGUI leftSensorLabel;
    [SerializeField] TextMeshProUGUI rightSensorLabel;
    
    [Header("Left Motor Pin Labels")]
    [SerializeField] TextMeshProUGUI leftMotorForwardLabel;
    [SerializeField] TextMeshProUGUI leftMotorBackwardLabel;
    
    [Header("Right Motor Pin Labels")]
    [SerializeField] TextMeshProUGUI rightMotorForwardLabel;
    [SerializeField] TextMeshProUGUI rightMotorBackwardLabel;
    
    [Header("Display Format")]
    [Tooltip("라벨 표시 형식. {0}=기능명, {1}=핀번호")]
    [SerializeField] string labelFormat = "Pin {1}";
    
    void Start()
    {
        if (arduino == null)
            arduino = FindObjectOfType<VirtualArduinoMicro>();
            
        UpdateAllLabels();
    }
    
    /// <summary>
    /// 모든 핀 라벨 텍스트 업데이트
    /// </summary>
    public void UpdateAllLabels()
    {
        if (arduino == null)
        {
            Debug.LogWarning("[PinMappingVisualizer] VirtualArduinoMicro not found!");
            return;
        }
        
        SetLabelText(leftSensorLabel, "L Sensor", arduino.defaultLeftSensorPin);
        SetLabelText(rightSensorLabel, "R Sensor", arduino.defaultRightSensorPin);
        SetLabelText(leftMotorForwardLabel, "L Motor F", arduino.defaultLeftMotorFPin);
        SetLabelText(leftMotorBackwardLabel, "L Motor B", arduino.defaultLeftMotorBPin);
        SetLabelText(rightMotorForwardLabel, "R Motor F", arduino.defaultRightMotorFPin);
        SetLabelText(rightMotorBackwardLabel, "R Motor B", arduino.defaultRightMotorBPin);
        
        Debug.Log("[PinMappingVisualizer] Labels updated.");
    }
    
    void SetLabelText(TextMeshProUGUI label, string functionName, int pinNumber)
    {
        if (label != null)
        {
            label.text = string.Format(labelFormat, functionName, pinNumber);
        }
    }
}
