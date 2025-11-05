using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RCCar : MonoBehaviour
{
    public GameObject[] go;
    public float speed = 360f; // 초당 회전 속도 (도/초)
    public float moveSpeed = 3f;     // 이동 속도 (m/s)
    public bool reverse = false;     // 반대 방향
    private UBlocklyGenerated controller;

    void Start()
    {
        if(controller == null)
            controller = this.gameObject.AddComponent<UBlocklyGenerated>();
    }

    void Update()
    {
        controller.Run();
        int a = go.Length;
        for (int i = 0; i < a; i++)
        {
            go[i].transform.Rotate(Vector3.up * speed * Time.deltaTime);
        }
        float dir = reverse ? -1f : 1f;

        // 1️⃣ 이동 거리 계산
        float distance = moveSpeed * Time.deltaTime * dir;
        transform.Translate(Vector3.forward * distance, Space.Self);
    }
}
