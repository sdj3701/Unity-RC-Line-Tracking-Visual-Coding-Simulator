using UnityEngine;
using TMPro;
using MG_BlocksEngine2.UI;

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
    
    [Header("Status Colors")]
    [Tooltip("맵핑 성공 시 색상")]
    [SerializeField] Color successColor = Color.green;
    [Tooltip("맵핑 실패/대기 시 색상")]
    [SerializeField] Color defaultColor = Color.white;
    
    void Start()
    {
        if (arduino == null)
            arduino = FindObjectOfType<VirtualArduinoMicro>();
            
        UpdateAllLabels();
        SetAllLabelsColor(defaultColor);
    }
    
    void OnEnable()
    {
        // 핀 맵핑 완료 이벤트 구독
        VirtualArduinoMicro.OnPinMappingCompleted += OnPinMappingCompleted;
    }
    
    void OnDisable()
    {
        // 이벤트 구독 해제
        VirtualArduinoMicro.OnPinMappingCompleted -= OnPinMappingCompleted;
    }
    
    /// <summary>
    /// 핀 맵핑 완료 시 호출되는 핸들러
    /// </summary>
    void OnPinMappingCompleted(System.Collections.Generic.HashSet<int> mappedPins)
    {
        if (arduino == null)
        {
            Debug.LogWarning("[PinMappingVisualizer] VirtualArduinoMicro not found!");
            return;
        }
        
        UpdateAllLabels();
        
        // 각 핀별로 맵핑 여부에 따라 색상 설정
        SetLabelColorByMapping(leftSensorLabel, arduino.defaultLeftSensorPin, mappedPins);
        SetLabelColorByMapping(rightSensorLabel, arduino.defaultRightSensorPin, mappedPins);
        SetLabelColorByMapping(leftMotorForwardLabel, arduino.defaultLeftMotorFPin, mappedPins);
        SetLabelColorByMapping(leftMotorBackwardLabel, arduino.defaultLeftMotorBPin, mappedPins);
        SetLabelColorByMapping(rightMotorForwardLabel, arduino.defaultRightMotorFPin, mappedPins);
        SetLabelColorByMapping(rightMotorBackwardLabel, arduino.defaultRightMotorBPin, mappedPins);
        
        Debug.Log($"[PinMappingVisualizer] Individual pin colors updated. Mapped: {mappedPins.Count}/6");
    }
    
    /// <summary>
    /// 핀 맵핑 여부에 따라 라벨 색상 설정
    /// </summary>
    void SetLabelColorByMapping(TextMeshProUGUI label, int pinNumber, System.Collections.Generic.HashSet<int> mappedPins)
    {
        if (label != null)
        {
            bool isMapped = mappedPins.Contains(pinNumber);
            label.color = isMapped ? successColor : defaultColor;
        }
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
    
    /// <summary>
    /// 모든 라벨의 색상 변경
    /// </summary>
    void SetAllLabelsColor(Color color)
    {
        SetLabelColor(leftSensorLabel, color);
        SetLabelColor(rightSensorLabel, color);
        SetLabelColor(leftMotorForwardLabel, color);
        SetLabelColor(leftMotorBackwardLabel, color);
        SetLabelColor(rightMotorForwardLabel, color);
        SetLabelColor(rightMotorBackwardLabel, color);
    }
    
    void SetLabelColor(TextMeshProUGUI label, Color color)
    {
        if (label != null)
        {
            label.color = color;
        }
    }
}
