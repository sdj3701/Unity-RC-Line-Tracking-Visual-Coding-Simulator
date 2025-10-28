using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateMap : MonoBehaviour
{
    public GameObject LinePrefab;

    [Tooltip("이전 포인트와 현재 마우스 위치의 최소 거리. 이 값보다 가까우면 새 포인트를 추가하지 않습니다.")]
    public float MinDrawDistance = 0.1f; // (유니티 단위) 최소 드로잉 거리

    // 2D 환경에서 그릴 평면의 Z축 거리.
    // (예: 메인 카메라 Z = -10, 오브젝트 Z = 0 일 경우, 거리는 10)
    private const float Z_PLANE_DISTANCE = 10f; 

    LineRenderer lr;
    EdgeCollider2D collider2D;
    List<Vector2> points = new List<Vector2>();
    
    // 마우스 위치를 월드 좌표로 변환하는 헬퍼 함수
    private Vector2 GetWorldMousePosition()
    {
        Vector3 mousePos3D = Input.mousePosition;
        // 마우스의 Z축 거리를 원하는 평면까지의 거리로 설정
        mousePos3D.z = Z_PLANE_DISTANCE; 
        
        // Z축 정보를 포함하여 월드 좌표로 변환, Vector2로 반환
        return Camera.main.ScreenToWorldPoint(mousePos3D);
    }

    void Update()
    {
        // -----------------------------------------------------------------
        // 1. 그리기 시작 (마우스 버튼 누름)
        // -----------------------------------------------------------------
        if (Input.GetMouseButtonDown(0))
        {
            // 새로운 라인 오브젝트 생성 및 컴포넌트 할당
            GameObject newLine = Instantiate(LinePrefab);
            lr = newLine.GetComponent<LineRenderer>();
            collider2D = newLine.GetComponent<EdgeCollider2D>();

            // 새로운 라인을 그리기 전에 포인트 리스트 초기화
            points.Clear();

            // 시작 위치 설정 (Z축 보정 포함)
            Vector2 startPos = GetWorldMousePosition();
            
            points.Add(startPos);
            
            // LineRenderer 설정
            lr.positionCount = 1;
            lr.SetPosition(0, startPos);
        }

        // -----------------------------------------------------------------
        // 2. 그리기 중 (마우스 버튼 누르고 있는 상태)
        // -----------------------------------------------------------------
        else if (Input.GetMouseButton(0))
        {
            // Null 체크 및 포인트가 최소 1개인지 확인
            if (lr == null || points.Count < 1) return; 

            // 현재 마우스 위치 가져오기 (Z축 보정 포함)
            Vector2 pos = GetWorldMousePosition();

            // 현재 위치가 마지막 포인트로부터 MinDrawDistance보다 멀리 떨어져 있는지 확인
            if (Vector2.Distance(pos, points[points.Count - 1]) > MinDrawDistance)
            {
                // 디버그 로그가 나오지 않던 문제를 해결했는지 확인하기 위한 로그
                Debug.Log($"새로운 포인트 추가: {pos}");
                
                // 새로운 포인트 추가
                points.Add(pos);
                
                // LineRenderer 업데이트
                lr.positionCount++;
                lr.SetPosition(lr.positionCount - 1, pos);
                
                // EdgeCollider2D 업데이트
                collider2D.points = points.ToArray(); 
            }
        }

        // -----------------------------------------------------------------
        // 3. 그리기 끝 (마우스 버튼 뗌)
        // -----------------------------------------------------------------
        else if(Input.GetMouseButtonUp(0))
        {
            // 포인트 리스트 초기화
            points.Clear();
            
            // 참조 해제 (다음 드로잉을 위해 깨끗한 상태로)
            lr = null;
            collider2D = null;
        }
    }
}