using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target;      // 따라갈 대상
    public Vector3 offset = new Vector3(0f, 3f, -6f); // 대상 기준 위치 오프셋
    public float smoothTime = 0.2f;

    Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        // 목표 위치 = 대상 위치 + 오프셋(대상 로컬 회전 반영)
        Vector3 desiredPos = target.TransformPoint(offset);

        // 부드럽게 위치 이동
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);

        // 항상 대상 바라보기
        transform.LookAt(target);
    }
}
