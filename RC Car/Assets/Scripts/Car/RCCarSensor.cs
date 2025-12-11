using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RCCarSensor : MonoBehaviour
{
    public BlocksGenerated logic;   // 블럭 로직 스크립트

    public float maxLinearSpeed = 5f;    // 앞뒤 속도
    public float maxAngularSpeed = 120f; // 회전 속도 (도/초)

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (logic == null) return;

        float left  = logic.LeftMotor;   // -1 ~ 1
        float right = logic.RightMotor;  // -1 ~ 1

        // 선속 / 각속도로 변환
        float linear  = (left + right) * 0.5f * maxLinearSpeed;
        float angular = (right - left) * maxAngularSpeed;

        // 앞으로 이동
        Vector3 move = transform.forward * linear * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        // 회전
        Quaternion turn = Quaternion.Euler(0f, angular * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * turn);
    }
}
