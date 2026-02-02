using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Header("Follow Mode")]
    public Transform target;      // 따라갈 대상
    public Vector3 offset = new Vector3(0f, 3f, -6f); // 대상 기준 위치 오프셋
    public float smoothTime = 0.2f;

    [Header("Fixed View Mode (시점 고정)")]
    public Vector3 pointOfViewOffset = new Vector3(0f, 5f, 10f);           // 고정 시점 1 위치
    public Vector3 TopDownViewOffset = new Vector3(0f, 17.5f, -10f);       // 고정 시점 2 위치
    public Vector3 pointOfViewRotation = new Vector3(10f, 180f, 0f);       // 고정 시점 1 회전
    public Vector3 TopDownViewRotation = new Vector3(90f, 0f, 0f);         // 고정 시점 2 회전

    [Header("TopDown Mode")]
    public float topDownHeight = 15f;     // 탑다운 뷰 높이
    public bool isTopDownView = false;    // 현재 뷰 모드
    public bool isPointofview = false;    // 고정 시점 모드 활성화
    private bool isFixedTopDown = false;  // true: TopDownView 고정, false: PointOfView 고정

    Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPos;
        Quaternion desiredRot;
        
        if (isPointofview)
        {
            // Fixed View Mode: 고정된 위치와 회전 사용 (target 따라가지 않음)
            if (isFixedTopDown)
            {
                // TopDown 고정 시점
                desiredPos = TopDownViewOffset;
                desiredRot = Quaternion.Euler(TopDownViewRotation);
            }
            else
            {
                // PointOfView 고정 시점
                desiredPos = pointOfViewOffset;
                desiredRot = Quaternion.Euler(pointOfViewRotation);
            }
            
            // 부드럽게 위치 이동
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref velocity, smoothTime);
            
            // 부드럽게 회전
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Time.deltaTime * 5f);
        }
        else if (isTopDownView)
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
        isPointofview = false; // TopDown 모드로 전환 시 Fixed View 비활성화
        Debug.Log($"[FollowCamera] View Mode: {(isTopDownView ? "TopDown" : "Follow")}");
    }

    /// <summary>
    /// TopDown View 직접 설정
    /// </summary>
    public void SetTopDownView(bool enabled)
    {
        isTopDownView = enabled;
        if (enabled) isPointofview = false;
    }

    /// <summary>
    /// 고정 시점 토글 (버튼에서 호출)
    /// pointOfViewOffset/Rotation ↔ TopDownViewOffset/Rotation 간 전환
    /// 카메라가 target을 따라가지 않고 고정된 위치와 회전을 사용
    /// </summary>
    public void TogglePointofview()
    {
        if (!isPointofview)
        {
            // 고정 시점 모드 활성화 (처음에는 PointOfView로 시작)
            isPointofview = true;
            isFixedTopDown = false;
            isTopDownView = false;
        }
        else
        {
            // 이미 고정 시점 모드면 두 시점 간 전환
            isFixedTopDown = !isFixedTopDown;
        }
        
        string viewName = isFixedTopDown ? "TopDown Fixed" : "PointOfView Fixed";
        Debug.Log($"[FollowCamera] Fixed View Mode: {viewName}");
    }
}
