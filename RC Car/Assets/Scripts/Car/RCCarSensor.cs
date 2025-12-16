using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RCCarSensor : MonoBehaviour
{
    public BlocksGenerated logic;   // 블럭 로직 스크립트
    public GameObject[] wheels;
    public GameObject[] Sensors;

    public float maxLinearSpeed = 5f;    // 앞뒤 속도
    public float maxAngularSpeed = 120f; // 회전 속도 (도/초)

    public float wheelVisualSpeed = 360f; // 회전 속도 (도/초)
    public Vector3 wheelRotateAxis = Vector3.up;

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (logic == null) logic = GetComponent<BlocksGenerated>();
    }

    void FixedUpdate()
    {
        if (logic == null) return;
        logic.Loop();

        float left  = logic.LeftMotor;   // -1 ~ 1
        float right = logic.RightMotor;  // -1 ~ 1
        Debug.Log(left + " " + right);

        ApplyWheelVisualRotation(left, right);

        // 선속 / 각속도로 변환
        float linear  = (left + right) * 0.5f * maxLinearSpeed;
        float angular = (right - left) * maxAngularSpeed;

        // 앞으로 이동
        Vector3 move = transform.forward * linear * Time.fixedDeltaTime;

        rb.MovePosition(rb.position + move);

        // 자동차의 회전
        Quaternion turn = Quaternion.Euler(0f, angular * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);
    }

    // 바퀴의 회전
    void ApplyWheelVisualRotation(float leftMotor, float rightMotor)
    {
        // 바퀴 배열이 없거나 비어 있으면 더 할 일이 없으니 종료
        if (wheels == null || wheels.Length == 0) return;

        // 고정 업데이트 한 프레임의 시간(회전량 계산에 사용)
        float dt = Time.fixedDeltaTime;

        // 바퀴가 1개만 있는 경우: 해당 바퀴에 왼쪽 모터 값을 재사용해 회전
        if (wheels.Length == 1)
        {
            var w = wheels[0];
            if (w != null)
                // wheelRotateAxis 축을 기준으로 leftMotor * wheelVisualSpeed * dt 만큼 회전
                w.transform.Rotate(wheelRotateAxis, leftMotor * wheelVisualSpeed * dt, Space.Self);
            return;
        }

        // 바퀴가 여러 개인 경우: 인덱스에 따라 좌/우 모터 값을 선택해 각각 회전
        for (int i = 0; i < wheels.Length; i++)
        {
            var w = wheels[i];
            if (w == null) continue;

            // 짝수 인덱스는 왼쪽 모터, 홀수 인덱스는 오른쪽 모터 값을 사용
            float motor = (i % 2 == 0) ? leftMotor : rightMotor;

            // wheelRotateAxis 축으로 motor 출력에 비례해 시각적 회전을 적용
            w.transform.Rotate(wheelRotateAxis, motor * wheelVisualSpeed * dt, Space.Self);
        }
    }
}
