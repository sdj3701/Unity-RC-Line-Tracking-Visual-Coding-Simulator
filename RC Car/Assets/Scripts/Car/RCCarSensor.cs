using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RCCarSensor : MonoBehaviour
{
    public BlocksGenerated logic;   // 블럭 로직 스크립트
    public GameObject[] wheels;
    public GameObject[] Sensors;

    [SerializeField] int leftPin = 3;
    [SerializeField] int rightPin = 4;

    [SerializeField] float rayDistance = 2f;
    [SerializeField, Range(0f, 1f)] float blackThreshold = 0.2f;

    // 흰색이면 true로 넣을지
    [SerializeField] bool whiteMeansTrue = true;

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

        // 1) 센서 -> digital input 주입
        PushSensorInputs(); // 반드시 Loop()보다 먼저

        // 2) 블록 로직 실행 (digitalRead가 여기서 소비됨)
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

    // 센서 기능
    void PushSensorInputs()
    {
        if (Sensors == null) return;

        // Sensors[0]을 왼쪽, Sensors[1]을 오른쪽으로 고정 매핑(원하면 배열/루프로 확장 가능)
        if (Sensors.Length > 0)
            logic.SetDigitalInput(leftPin, SensorToBool(Sensors[0]));

        if (Sensors.Length > 1)
            logic.SetDigitalInput(rightPin, SensorToBool(Sensors[1]));
    }

    bool SensorToBool(GameObject sensor)
    {
        if (sensor == null) return false;

        // 센서 위치에서 센서의 -up 방향으로 레이를 쏴서 바닥(텍스처가 있는 메쉬)을 맞춘다.
        Vector3 origin = sensor.transform.position;
        Vector3 dir = -sensor.transform.up;

        // 바닥(혹은 라인 오브젝트)에 Collider가 있어야 Raycast가 hit 됨
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, rayDistance))
        {
            // 아무것도 못 맞추면 기본값 반환
            // 흰색이면 true로 쓰는 구조라면, 여기서 true를 반환하면 "바닥을 못찾아도 일단 전진" 같은 동작이 됨
            return whiteMeansTrue;
        }

        // hit 지점의 텍스처 픽셀을 샘플링해서 grayscale 값을 얻음
        bool sampled = TrySampleGray(hit, out float gray);

        // 텍스처를 못 읽는 경우(가장 흔한 원인: Texture Import에서 Read/Write Enabled 꺼짐)
        // 여기서 false를 리턴하면 digitalRead가 false가 되어 차가 안 움직일 수 있음
        if (!sampled) return whiteMeansTrue;

        // grayscale이 임계값보다 작으면 검정으로 간주
        bool isBlack = gray <= blackThreshold;

        // 최종적으로 BlocksGenerated에 넣을 bool 결정
        // whiteMeansTrue == true 이면: 흰색일 때 true(= !isBlack)
        return whiteMeansTrue ? !isBlack : isBlack;
    }

    bool TrySampleGray(RaycastHit hit, out float gray)
    {
        gray = 1f; // 기본은 흰색(밝음)

        // Raycast로 맞은 오브젝트의 Renderer/Material/Texture를 가져온다
        Renderer rend = hit.collider ? hit.collider.GetComponent<Renderer>() : null;
        if (rend == null) return false;

        Material mat = rend.sharedMaterial;
        if (mat == null) return false;

        Texture2D tex = mat.mainTexture as Texture2D;
        if (tex == null) return false;

        // hit.textureCoord는 맞은 위치의 UV(0~1 범위) 좌표
        Vector2 uv = hit.textureCoord;

        // 머티리얼의 Tiling/Offset까지 반영(씬에서 타일/오프셋 조정했으면 이거 안하면 좌표가 어긋남)
        uv = Vector2.Scale(uv, mat.mainTextureScale) + mat.mainTextureOffset;

        // 타일링 반복을 고려해서 0~1로 wrap
        uv.x = uv.x - Mathf.Floor(uv.x);
        uv.y = uv.y - Mathf.Floor(uv.y);

        // GetPixelBilinear: (x,y)가 0~1일 때 자동으로 보간해서 픽셀값을 줌
        // 주의: 텍스처 Import Settings에서 Read/Write Enabled가 켜져 있어야 예외 없이 동작함
        try
        {
            gray = tex.GetPixelBilinear(uv.x, uv.y).grayscale;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
