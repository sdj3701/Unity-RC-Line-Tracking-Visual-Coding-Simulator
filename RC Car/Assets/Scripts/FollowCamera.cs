using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("Follow Mode")]
    public Transform target;      // 따라갈 대상
    public Vector3 offset = new Vector3(0f, 3f, -6f); // 대상 기준 위치 오프셋
    public float smoothTime = 0.2f;

    [Header("TopDown Mode")]
    public float topDownHeight = 15f;     // 탑다운 뷰 높이
    public bool isTopDownView = false;    // 현재 뷰 모드

    Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPos;
        
        if (isTopDownView)
        {
            // TopDown View: 대상 위에서 아래로 바라봄
            desiredPos = target.position + Vector3.up * topDownHeight;
            
            // 부드럽게 위치 이동
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);
            
            // 아래를 바라봄 (Y축 회전은 대상 따라감)
            Quaternion targetRotation = Quaternion.Euler(90f, target.eulerAngles.y, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 5f);
        }
        else
        {
            // Follow Mode: 기존 방식
            desiredPos = target.TransformPoint(offset);
            
            // 부드럽게 위치 이동
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);
            
            // 항상 대상 바라보기
            transform.LookAt(target);
        }
    }

    /// <summary>
    /// TopDown View 토글 (버튼에서 호출)
    /// </summary>
    public void ToggleTopDownView()
    {
        isTopDownView = !isTopDownView;
        Debug.Log($"[FollowCamera] View Mode: {(isTopDownView ? "TopDown" : "Follow")}");
    }

    /// <summary>
    /// TopDown View 직접 설정
    /// </summary>
    public void SetTopDownView(bool enabled)
    {
        isTopDownView = enabled;
    }
}
