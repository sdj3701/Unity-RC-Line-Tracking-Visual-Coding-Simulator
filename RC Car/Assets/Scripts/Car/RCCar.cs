using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RCCar : MonoBehaviour
{
    public GameObject[] wheels;
    public GameObject[] Sensor;
    private BlocksGenerated controller;
    public Button But_Start, But_Stop;

    public float speed = 360f; // 초당 회전 속도 (도/초)
    public float moveSpeed = 3f;     // 이동 속도 (m/s)
    public float turnSpeed = 120f;
    public float rayDistance = 2f;
    public float blackThreshold = 0.2f;
    public string lineTag = "Line";
    private bool stopped = true;
    public const byte MOTOR_SPEED = 150;
    private byte m_a_spd = 0, m_b_spd = 0;
    private bool m_a_dir = false, m_b_dir = false;
    private bool s0Black = false, s1Black = false;


    void Start()
    {
        if (controller == null)
            controller = this.gameObject.AddComponent<BlocksGenerated>();
            
        But_Start.onClick.AddListener(() => stopped = false);
        But_Stop.onClick.AddListener(() => stopped = true);
        
        // 내가 만든 블록으로 오브젝트 제어
        controller.Start();
    }

    void Update()
    {
        // 바퀴 회전 
        if (!stopped)
        {
            SensorCheck();

            

            // 라인 탐지 센서 0 = a,1 = d
            if (s0Black)
                RcCtrlVal('a');
            else if (s1Black)
                RcCtrlVal('d');
            else
                RcCtrlVal('s');

            WheelRotationAndMotorDrive();
        }
    }

    // 센서 라인 탐지
    private void SensorCheck()
    {
        int sLen = Sensor != null ? Sensor.Length : 0;
        for (int i = 0; i < sLen; i++)
        {
            GameObject s = Sensor[i];
            if (s == null) continue;
            Vector3 origin = s.transform.position;
            Vector3 d = -s.transform.up;
            RaycastHit hit;
            if (Physics.Raycast(origin, d, out hit, rayDistance))
            {
                bool black = IsBlack(hit);
                Debug.Log(s0Black + " " + s1Black);
                if (i == 0) s0Black = black;
                else if (i == 1) s1Black = black;
                Vector3 end = hit.point;
                Color lineColor = black ? Color.green : Color.yellow;
                Debug.DrawLine(origin, end, lineColor);
            }
            else
            {
                Debug.DrawLine(origin, origin + d * rayDistance, Color.red);
            }
        }
    }

    // 바퀴 회전 및 모터 제어
    private void WheelRotationAndMotorDrive()
    {
        if (s0Black)
            wheels[0].transform.Rotate(-Vector3.up * speed * Time.deltaTime);
        else if (s1Black)
            wheels[1].transform.Rotate(-Vector3.up * speed * Time.deltaTime);
        else
        {
            wheels[0].transform.Rotate(-Vector3.up * speed * Time.deltaTime);
            wheels[1].transform.Rotate(-Vector3.up * speed * Time.deltaTime);
        }
            
        // 모터 및 바퀴 좌우회전
        MotorDrive();
    }

    public void RcCtrlVal(char cmd)
    {
        if (cmd == 'w')
        {
            m_a_dir = false;
            m_b_dir = false;
            m_a_spd = MOTOR_SPEED;
            m_b_spd = MOTOR_SPEED;
        }
        else if (cmd == 'a')
        {
            m_a_dir = true;
            m_b_dir = false;
            m_a_spd = MOTOR_SPEED;
            m_b_spd = MOTOR_SPEED;
        }
        else if (cmd == 'd')
        {
            m_a_dir = false;
            m_b_dir = true;
            m_a_spd = MOTOR_SPEED;
            m_b_spd = MOTOR_SPEED;
        }
        else if (cmd == 's')
        {
            m_a_dir = true;
            m_b_dir = true;
            m_a_spd = MOTOR_SPEED;
            m_b_spd = MOTOR_SPEED;
        }
        else if (cmd == 'x')
        {
            m_a_dir = false;
            m_b_dir = false;
            m_a_spd = 0;
            m_b_spd = 0;
        }
    }

    public void MotorDrive()
    {
        float left = (m_a_dir ? -1f : 1f) * (m_a_spd / 255f);
        float right = (m_b_dir ? -1f : 1f) * (m_b_spd / 255f);
        float v = (left + right) * 0.5f;
        float w = (right - left) * 0.5f;
        float move = v * moveSpeed * Time.deltaTime;
        float yawDeg = w * turnSpeed * Time.deltaTime;
        transform.Translate(Vector3.forward * move, Space.Self);
        transform.Rotate(Vector3.up, yawDeg, Space.Self);
    }

    public void SetMotors(byte aPwm, bool aReverse, byte bPwm, bool bReverse)
    {
        m_a_spd = aPwm;
        m_a_dir = aReverse;
        m_b_spd = bPwm;
        m_b_dir = bReverse;
    }

    bool IsBlack(RaycastHit hit)
    {
        Renderer rend = (hit.collider != null) ? hit.collider.GetComponent<Renderer>() : null;
        if (rend != null)
        {
            Material mat = rend.sharedMaterial;
            if (mat != null)
            {
                Texture2D tex = mat.mainTexture as Texture2D;
                if (tex != null)
                {
                    try
                    {
                        Vector2 uv = hit.textureCoord;
                        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * tex.width), 0, tex.width - 1);
                        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * tex.height), 0, tex.height - 1);
                        Color c = tex.GetPixel(x, y);
                        return c.grayscale <= blackThreshold;
                    }
                    catch { }
                }
                if (mat.HasProperty("_Color"))
                {
                    return mat.color.grayscale <= blackThreshold;
                }
            }
        }
        if (hit.collider != null && hit.collider.gameObject.tag == lineTag) return true;
        return false;
    }
    #region Gizmo
    // Scene 뷰에서만 보임
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        for(int i = 0 ; i< Sensor.Length ; i++)
        {
            Gizmos.DrawLine(Sensor[i].transform.position, Sensor[i].transform.position + Vector3.down * rayDistance);
        }
    }

    // 선택되었을 때만 보이게 하려면
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        for (int i = 0; i < Sensor.Length; i++)
        {
            Gizmos.DrawLine(Sensor[i].transform.position, Sensor[i].transform.position + Vector3.down * rayDistance);
        }
    }
    #endregion

    // bool TrySampleGray(RaycastHit hit, out float gray)
    // {
    //     gray = 1f;
    //     Renderer rend = (hit.collider != null) ? hit.collider.GetComponent<Renderer>() : null;
    //     if (rend != null)
    //     {
    //         Material mat = rend.sharedMaterial;
    //         if (mat != null)
    //         {
    //             Texture2D tex = mat.mainTexture as Texture2D;
    //             if (tex != null)
    //             {
    //                 try
    //                 {
    //                     Vector2 uv = hit.textureCoord;
    //                     int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * tex.width), 0, tex.width - 1);
    //                     int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * tex.height), 0, tex.height - 1);
    //                     gray = tex.GetPixel(x, y).grayscale;
    //                     return true;
    //                 }
    //                 catch {}
    //             }
    //             if (mat.HasProperty("_Color"))
    //             {
    //                 gray = mat.color.grayscale;
    //                 return true;
    //             }
    //         }
    //     }
    //     return false;
    // }

    // bool TrySampleColor(RaycastHit hit, out Color color)
    // {
    //     color = Color.clear;
    //     Renderer rend = (hit.collider != null) ? hit.collider.GetComponent<Renderer>() : null;
    //     if (rend != null)
    //     {
    //         Material mat = rend.sharedMaterial;
    //         if (mat != null)
    //         {
    //             Texture2D tex = mat.mainTexture as Texture2D;
    //             if (tex != null)
    //             {
    //                 try
    //                 {
    //                     Vector2 uv = hit.textureCoord;
    //                     int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * tex.width), 0, tex.width - 1);
    //                     int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * tex.height), 0, tex.height - 1);
    //                     color = tex.GetPixel(x, y);
    //                     return true;
    //                 }
    //                 catch {}
    //             }
    //             if (mat.HasProperty("_Color"))
    //             {
    //                 color = mat.color;
    //                 return true;
    //             }
    //         }
    //     }
    //     return false;
    // }
}
