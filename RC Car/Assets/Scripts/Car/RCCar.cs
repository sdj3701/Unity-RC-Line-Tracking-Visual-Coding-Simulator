using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RCCar : MonoBehaviour
{
    public GameObject[] wheel;
    public GameObject[] Sensor;
    public float speed = 360f; // 초당 회전 속도 (도/초)
    public float moveSpeed = 3f;     // 이동 속도 (m/s)
    public bool reverse = false;     // 반대 방향
    private UBlocklyGenerated controller;
    public float turnSpeed = 120f;
    public float rayDistance = 2f;
    public float blackThreshold = 0.2f;
    public string lineTag = "Line";
    public bool stopOnBlack = true;
    private bool stopped = false;

    void Start()
    {
        if(controller == null)
            controller = this.gameObject.AddComponent<UBlocklyGenerated>();
    }

    void Update()
    {
        // 내가 만든 블록으로 오브젝트 제어
        controller.Run();
        if (!stopped)
        {
            for (int i = 0; i < wheel.Length; i++)
            {
                wheel[i].transform.Rotate(Vector3.up * speed * Time.deltaTime);
            }
            float dir = reverse ? -1f : 1f;
            float distance = moveSpeed * Time.deltaTime * dir;
            transform.Translate(Vector3.forward * distance, Space.Self);
        }

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
                Vector3 end = hit.point;
                Color lineColor = black ? Color.green : Color.yellow;
                Debug.DrawLine(origin, end, lineColor);
                float g;
                Debug.Log(black);
                Color c;
                if (TrySampleColor(hit, out c))
                    Debug.Log($"Sensor {i} hit color: #{ColorUtility.ToHtmlStringRGBA(c)} r={c.r:F2} g={c.g:F2} b={c.b:F2} a={c.a:F2}");

                if (black)
                {
                    if (stopOnBlack) stopped = true;
                    if (TrySampleGray(hit, out g))
                        Debug.Log($"Black line detected (sensor {i}) gray={g:F2}");
                    else
                        Debug.Log($"Black line detected (sensor {i})");
                }
            }
            else
            {
                Debug.DrawLine(origin, origin + d * rayDistance, Color.red);
            }
        }
    }


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
        for(int i = 0 ; i< Sensor.Length ; i++)
        {
            Gizmos.DrawLine(Sensor[i].transform.position, Sensor[i].transform.position + Vector3.down * rayDistance);
        }
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
                    catch {}
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

    bool TrySampleGray(RaycastHit hit, out float gray)
    {
        gray = 1f;
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
                        gray = tex.GetPixel(x, y).grayscale;
                        return true;
                    }
                    catch {}
                }
                if (mat.HasProperty("_Color"))
                {
                    gray = mat.color.grayscale;
                    return true;
                }
            }
        }
        return false;
    }

    bool TrySampleColor(RaycastHit hit, out Color color)
    {
        color = Color.clear;
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
                        color = tex.GetPixel(x, y);
                        return true;
                    }
                    catch {}
                }
                if (mat.HasProperty("_Color"))
                {
                    color = mat.color;
                    return true;
                }
            }
        }
        return false;
    }
}
