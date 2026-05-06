# RC Car Start and End Plan

작성일: 2026-05-06

## 결론

도착 지점은 투명 콜라이더를 가진 `End` 오브젝트로 만드는 방식이 맞다. 다만 콜라이더 오브젝트 자체를 비활성화하면 충돌 감지가 되지 않으므로, `GameObject`와 `Collider`는 활성 상태로 두고 `MeshRenderer`만 끄거나 투명 머테리얼을 적용해야 한다.

현재 프로젝트는 시작 지점이 `ChangeMap.carSpawnPoses`와 `ButtonRestart`를 통해 이미 관리되고 있다. 여기에 맵별 도착 트리거를 추가하고, RC카가 해당 트리거에 들어온 뒤 정방향으로 완전히 빠져나갔을 때 `VirtualCarPhysics.StopRunning()`으로 차량을 멈추고 주행 완료 상태를 기록하는 구조가 가장 자연스럽다.

## 목표

- 맵마다 명확한 시작 지점과 도착 지점을 가진다.
- RC카가 도착 지점에 들어오면 주행이 끝난다.
- 도착 후에는 차량 모터와 물리 이동이 멈춘다.
- 리스타트 또는 맵 변경 시 다시 시작 상태로 돌아갈 수 있다.
- 네트워크 주행에서는 호스트/server 쪽에서 종료 판정을 신뢰한다.
- 도착 지점은 화면에 보이지 않아도 되지만, 개발 중에는 위치를 확인할 수 있어야 한다.

## 현재 구조 요약

### 시작 지점

현재 시작 지점은 `ChangeMap`이 담당한다.

- 위치: `RC Car/Assets/Scripts/Map/ChangeMap.cs`
- 핵심 필드:
  - `carSpawnPoses`
  - `carTransform`
  - `carPhysics`
  - `buttonRestart`
- 핵심 동작:
  - 맵 변경 시 `MoveCarToCurrentSpawn()`으로 차량을 현재 맵의 시작 위치로 이동한다.
  - `SyncRestartInitialPosition()`으로 `ButtonRestart`의 리스타트 기준 위치도 같이 갱신한다.

리스타트는 `ButtonRestart`가 담당한다.

- 위치: `RC Car/Assets/Scripts/Player/ButtonRestart.cs`
- 핵심 동작:
  - 현재 맵의 스폰 위치를 기준으로 차량을 되돌린다.
  - 리스타트 시 `VirtualCarPhysics.StopRunning()`을 호출한다.
  - `Rigidbody.velocity`와 `Rigidbody.angularVelocity`를 0으로 초기화한다.

### 차량 주행

실제 차량 물리는 `VirtualCarPhysics`가 담당한다.

- 위치: `RC Car/Assets/Scripts/Core/VirtualArduino/VirtualCarPhysics.cs`
- 핵심 필드/메서드:
  - `IsRunning`
  - `StartRunning()`
  - `StopRunning()`
  - `FixedUpdate()`
- 도착 시점에는 `StopRunning()`을 호출해서 모터 입력을 0으로 만들고 주행 루프를 멈추는 것이 맞다.

### 네트워크 차량

네트워크 차량은 호스트/server에서 스폰된다.

- 위치:
  - `RC Car/Assets/Scripts/NetworkCar/NetworkRCCarSpawner.cs`
  - `RC Car/Assets/Scripts/NetworkCar/HostCarSpawner.cs`
  - `RC Car/Assets/Scripts/NetworkCar/HostExecutionScheduler.cs`
- 핵심 특징:
  - `NetworkRCCarSpawner`는 `runner.IsServer`일 때만 차량을 스폰한다.
  - `HostExecutionScheduler`는 슬롯별로 차량 실행을 시작하고 짧은 시간 후 멈춘다.
  - 네트워크 모드에서 도착 판정은 클라이언트가 임의로 결정하지 않고 호스트/server 기준으로 처리해야 한다.

## 추천 설계

### 씬 오브젝트 구성

맵 씬 또는 코스 루트 아래에 시작 지점과 도착 지점을 명확히 분리한다.

```text
CourseRoot
  StartPoints
    Start_Map_00
    Start_Map_01
    Start_Map_02
  EndTriggers
    End_Map_00
    End_Map_01
    End_Map_02
```

`Start_Map_xx`는 지금처럼 `ChangeMap.carSpawnPoses`에 들어가는 위치/회전 값의 기준으로 사용한다.

`End_Map_xx`는 실제 도착 판정용 오브젝트다.

권장 설정:

- 이름: `End_Map_00`, `End_Map_01`처럼 맵 인덱스와 맞춘다.
- 태그: `End`
- 레이어: `CourseTrigger` 또는 `EndTrigger`
- Collider: `BoxCollider`
- `BoxCollider.isTrigger`: `true`
- `MeshRenderer`: 꺼도 됨
- `GameObject.activeSelf`: `true`
- `Collider.enabled`: `true`

현재 `VirtualCarPhysics`는 3D `Rigidbody`를 사용하므로 `BoxCollider2D`가 아니라 3D `BoxCollider`를 써야 한다.

### 투명 콜라이더 주의점

도착 트리거를 투명하게 만들 때 다음 둘 중 하나를 선택한다.

1. `MeshRenderer`를 끈다.
2. 알파가 0인 투명 머테리얼을 사용한다.

추천은 1번이다. 충돌 판정에는 렌더러가 필요하지 않다. 렌더러만 꺼도 `BoxCollider`는 계속 동작한다.

피해야 할 설정:

- `End_Map_00` 오브젝트를 비활성화
- `BoxCollider.enabled`를 끄기
- `Is Trigger`를 끄기
- 차량과 도착 트리거가 서로 충돌하지 않는 레이어 매트릭스 설정
- 3D 차량에 2D 콜라이더 사용

## 도착 판정 방식

도착 트리거에는 `RCCarEndTrigger` 같은 스크립트를 붙인다. Unity에는 `OnTriggerEnd`라는 이벤트는 없고, 도착 영역을 완전히 통과한 뒤 처리하려면 `OnTriggerEnter`와 `OnTriggerExit`를 같이 사용한다.

권장 판정은 다음과 같다.

1. `OnTriggerEnter`에서 차량이 End 영역에 진입했음을 기록한다.
2. `OnTriggerExit`에서 차량이 End 영역을 완전히 빠져나갔는지 확인한다.
3. End 오브젝트의 `transform.forward` 방향으로 빠져나갔을 때만 완료 처리한다.
4. 완료되면 `RCCarCourseController`에 완료 이벤트를 보낸다.

이 방식은 단순히 End 영역에 닿자마자 끝나는 것보다 "도착선을 통과했다"는 느낌이 좋다. 단, `OnTriggerExit`만 단독으로 쓰면 차량이 뒤로 빠져나가도 완료될 수 있으므로 진입 방향과 이탈 방향을 같이 확인해야 한다.

예상 파일:

```text
RC Car/Assets/Scripts/Map/RCCarEndTrigger.cs
RC Car/Assets/Scripts/Map/RCCarCourseController.cs
```

### `RCCarEndTrigger` 책임

- `OnTriggerEnter(Collider other)`에서 차량 진입을 감지한다.
- `OnTriggerExit(Collider other)`에서 차량이 End 영역을 빠져나간 순간을 감지한다.
- 차량에 여러 콜라이더가 있을 수 있으므로 차량별 overlap count를 관리한다.
- End 오브젝트의 forward 방향을 기준으로 정방향 통과인지 확인한다.
- 차량 오브젝트 또는 부모에서 `VirtualCarPhysics`를 찾는다.
- 이미 종료 처리된 차량이면 중복 처리하지 않는다.
- 실제 종료 처리는 직접 많이 하지 않고 `RCCarCourseController`로 넘긴다.

예시 코드 방향:

```csharp
using System.Collections.Generic;
using UnityEngine;

public class RCCarEndTrigger : MonoBehaviour
{
    [SerializeField] private RCCarCourseController courseController;
    [SerializeField] private LayerMask carLayerMask = ~0;
    [SerializeField] private bool finishOnce = true;
    [SerializeField] private bool requireForwardPass = true;

    private readonly Dictionary<VirtualCarPhysics, int> overlapCounts = new Dictionary<VirtualCarPhysics, int>();
    private readonly Dictionary<VirtualCarPhysics, float> entrySideByCar = new Dictionary<VirtualCarPhysics, float>();
    private readonly HashSet<VirtualCarPhysics> finishedCars = new HashSet<VirtualCarPhysics>();

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & carLayerMask) == 0)
        {
            return;
        }

        VirtualCarPhysics physics = other.GetComponentInParent<VirtualCarPhysics>();
        if (physics == null)
        {
            return;
        }

        if (finishOnce && finishedCars.Contains(physics))
        {
            return;
        }

        if (!overlapCounts.TryGetValue(physics, out int count) || count <= 0)
        {
            entrySideByCar[physics] = GetSide(physics.transform.position);
            overlapCounts[physics] = 1;
            return;
        }

        overlapCounts[physics] = count + 1;
    }

    private void OnTriggerExit(Collider other)
    {
        if (((1 << other.gameObject.layer) & carLayerMask) == 0)
        {
            return;
        }

        VirtualCarPhysics physics = other.GetComponentInParent<VirtualCarPhysics>();
        if (physics == null)
        {
            return;
        }

        if (!overlapCounts.TryGetValue(physics, out int count))
        {
            return;
        }

        count--;
        if (count > 0)
        {
            overlapCounts[physics] = count;
            return;
        }

        overlapCounts.Remove(physics);

        if (finishOnce && finishedCars.Contains(physics))
        {
            return;
        }

        if (requireForwardPass && !DidPassForward(physics))
        {
            entrySideByCar.Remove(physics);
            return;
        }

        entrySideByCar.Remove(physics);
        finishedCars.Add(physics);

        if (courseController != null)
        {
            courseController.FinishCar(physics);
        }
        else
        {
            physics.StopRunning();
        }
    }

    public void ResetFinishState()
    {
        overlapCounts.Clear();
        entrySideByCar.Clear();
        finishedCars.Clear();
    }

    private bool DidPassForward(VirtualCarPhysics physics)
    {
        float entrySide = entrySideByCar.TryGetValue(physics, out float side) ? side : 0f;
        float exitSide = GetSide(physics.transform.position);
        return entrySide <= 0f && exitSide > 0f;
    }

    private float GetSide(Vector3 worldPosition)
    {
        return Vector3.Dot(worldPosition - transform.position, transform.forward);
    }
}
```

이 예시를 쓰려면 `End_Map_xx` 오브젝트의 파란색 Z축, 즉 `transform.forward`가 출발 지점에서 도착 지점으로 나아가는 방향을 바라보게 배치해야 한다. 차량이 End 트리거에 들어올 때는 `entrySide <= 0`, 완전히 통과해서 빠져나갈 때는 `exitSide > 0`이 되도록 하는 것이 기준이다.

### `RCCarCourseController` 책임

- 현재 코스 상태를 관리한다.
- 시작 전, 주행 중, 완료 상태를 구분한다.
- 도착 시 차량을 멈춘다.
- Rigidbody 속도를 0으로 초기화한다.
- 필요하면 UI에 완료 메시지를 보여준다.
- 리스타트 시 도착 상태를 초기화한다.
- 맵 변경 시 해당 맵의 End 트리거만 활성화한다.

예시 상태:

```csharp
public enum RCCarCourseState
{
    Ready,
    Running,
    Finished
}
```

예시 메서드:

```csharp
public void BeginRun()
public void FinishCar(VirtualCarPhysics physics)
public void ResetCourse()
public void ApplyMapEndTrigger(int mapIndex)
```

## 맵별 시작/도착 데이터

현재 `ChangeMap`은 시작 위치만 알고 있다. 도착 지점을 안정적으로 관리하려면 맵별 도착 지점도 같은 수준의 데이터로 관리해야 한다.

### 1차 구현 추천

씬에 `End_Map_00`, `End_Map_01`, `End_Map_02`를 직접 배치하고 `RCCarCourseController`가 배열로 참조한다.

```csharp
[SerializeField] private RCCarEndTrigger[] endTriggersByMap;
```

맵 변경 시:

1. `ChangeMap.ApplyMap(mapIndex, true)`가 호출된다.
2. 차량이 해당 맵 시작 위치로 이동한다.
3. `RCCarCourseController.ApplyMapEndTrigger(mapIndex)`가 호출된다.
4. 현재 맵의 End만 활성화하고 나머지는 비활성화한다.
5. 종료 상태를 `Ready`로 초기화한다.

이 방식은 가장 단순하고 에디터에서 눈으로 확인하기 쉽다.

### 2차 개선

`ChangeMap` 내부 데이터 구조에 도착 지점도 포함한다.

현재 구조:

```csharp
public SpawnPose[] carSpawnPoses;
```

개선 방향:

```csharp
[System.Serializable]
public struct CoursePose
{
    public Vector3 startPosition;
    public Vector3 startRotation;
    public Vector3 endPosition;
    public Vector3 endSize;
}
```

또는 기존 구조를 크게 바꾸지 않고 다음 필드만 추가한다.

```csharp
public Transform[] endTriggerTransforms;
```

현재 프로젝트에는 이미 `ChangeMap.SpawnPose`가 있으므로, 당장은 `endTriggersByMap` 배열을 별도 컨트롤러에 두는 편이 변경 범위가 작다.

## 리스타트와의 연결

리스타트 시에는 단순히 차량 위치만 되돌리면 안 된다. 도착 상태도 같이 초기화해야 한다.

필요한 동작:

1. `ButtonRestart.RestartCar()` 실행
2. `VirtualCarPhysics.StopRunning()`
3. Rigidbody 속도 초기화
4. 차량 위치/회전을 시작 지점으로 복귀
5. `RCCarCourseController.ResetCourse()` 호출
6. 모든 `RCCarEndTrigger.ResetFinishState()` 호출

현재 `ButtonRestart`는 1-4번을 이미 처리한다. 5-6번을 추가하기 위해 `ButtonRestart`에 선택 참조를 하나 추가할 수 있다.

예상 추가 필드:

```csharp
[Tooltip("코스 종료 상태 초기화용 컨트롤러")]
public RCCarCourseController courseController;
```

그리고 `RestartCar()` 끝에 다음을 호출한다.

```csharp
if (courseController != null)
{
    courseController.ResetCourse();
}
```

## 맵 변경과의 연결

맵이 바뀌면 시작 위치와 도착 위치도 같이 바뀌어야 한다.

현재 `ChangeMap.ApplyMap()`은 이미 다음을 수행한다.

- 현재 맵 인덱스 갱신
- Plane 머테리얼 변경
- 차량을 현재 맵 시작 위치로 이동
- 리스타트 기준 위치 동기화

여기에 도착 트리거 동기화를 추가한다.

예상 추가 필드:

```csharp
[Tooltip("맵별 도착 트리거 컨트롤러")]
public RCCarCourseController courseController;
```

`ApplyMap()` 마지막 부근에서 호출:

```csharp
if (courseController != null)
{
    courseController.ApplyMapEndTrigger(currentMapIndex);
    courseController.ResetCourse();
}
```

주의할 점:

- 런타임 맵이 있을 경우 `currentMapIndex`가 정적 맵 개수보다 커질 수 있다.
- 런타임 맵의 도착 지점이 아직 없다면 기본 End 트리거를 사용하거나, 런타임 맵 등록 시 도착 위치도 같이 받아야 한다.
- `endTriggersByMap.Length`보다 큰 인덱스가 들어오면 경고 로그를 남기고 모든 End를 비활성화하는 편이 안전하다.

## 런타임 맵 도착 지점

런타임 생성 맵은 `ChangeMap.RuntimeMapEntry`로 관리된다.

현재 포함된 데이터:

- `mapId`
- `displayName`
- `material`
- `spawnPose`
- `destroyMaterialOnRemove`

런타임 맵에서도 종료가 필요하면 `RuntimeMapEntry`에 도착 정보를 추가해야 한다.

후보 1: 위치와 크기 저장

```csharp
public Vector3 endPosition;
public Vector3 endSize;
public Vector3 endRotation;
```

후보 2: 시작점처럼 `SpawnPose` 재사용

```csharp
public SpawnPose endPose;
public Vector3 endTriggerSize;
```

후보 3: 런타임 맵은 일단 공통 도착 지점 사용

- 구현이 가장 빠르다.
- 모든 런타임 맵의 도착 지점이 같아도 되는 경우에만 적합하다.
- 맵이 실제로 다르게 생긴다면 나중에 문제가 된다.

추천은 후보 2다. 현재 `SpawnPose` 구조를 이미 사용하고 있어 코드 스타일이 맞고, 도착 방향까지 같이 저장할 수 있다.

## 네트워크 모드 처리

네트워크 주행에서는 도착 판정을 호스트/server가 최종 결정해야 한다.

이유:

- 클라이언트가 자기 화면에서만 도착했다고 판단하면 다른 참가자와 상태가 달라질 수 있다.
- `NetworkRCCarSpawner`가 이미 server 기준으로 차량을 생성한다.
- 차량 실행도 `HostExecutionScheduler`에서 호스트가 관리한다.

권장 흐름:

1. 호스트/server 씬에 End 트리거가 있다.
2. 호스트/server에서 차량 Rigidbody가 End 트리거에 들어온다.
3. `RCCarEndTrigger`가 `VirtualCarPhysics`와 가능하면 `NetworkRCCar`를 찾는다.
4. `RCCarCourseController` 또는 `HostNetworkCarCoordinator`에 완료를 알린다.
5. 호스트가 해당 차량의 `VirtualCarPhysics.StopRunning()`을 호출한다.
6. 필요하면 `NetworkRCCar`의 networked 상태에 완료 여부를 반영한다.
7. UI에는 완료한 슬롯/userId를 표시한다.

### 슬롯별 완료 처리

멀티 참가자에서는 한 명이 도착했다고 전체 실행을 끝낼지, 해당 차량만 끝낼지 먼저 결정해야 한다.

추천 기본값:

- 개인별 주행이면 해당 차량만 `Finished` 처리한다.
- 레이스 모드면 첫 도착자를 기록하고 전체 코스를 `Finished` 처리한다.
- 교육용 블록 실행 검증이면 해당 차량만 멈추고 나머지는 계속 실행한다.

현재 구조의 `HostExecutionScheduler`는 슬롯별로 짧게 실행하고 멈추는 루프다. 따라서 도착한 차량은 `HostCarBinding` 또는 별도 딕셔너리에 `Finished` 상태를 저장하고, 이후 스케줄러가 해당 슬롯을 건너뛰게 만드는 것이 좋다.

예상 데이터:

```csharp
private readonly HashSet<string> _finishedUserIds = new HashSet<string>();
```

스케줄러에서 해당 userId가 완료 상태이면:

```csharp
_statusReporter?.SetRuntimeStatus(slot, userId, "finished");
yield return new WaitForSeconds(waitSeconds);
continue;
```

## 도착 후 차량 정지 처리

도착 시에는 다음 순서로 멈춘다.

1. `VirtualCarPhysics.StopRunning()`
2. `VirtualMotorDriver.SetMotorSpeed(0f, 0f)`
3. `Rigidbody.velocity = Vector3.zero`
4. `Rigidbody.angularVelocity = Vector3.zero`
5. 필요하면 짧은 시간 동안 입력/실행 버튼 비활성화

`StopRunning()`은 이미 모터 값을 0으로 만든다. 그러나 물리 속도는 별도로 남을 수 있으므로 Rigidbody 속도도 0으로 초기화하는 것이 좋다.

예시:

```csharp
private static void StopCarCompletely(VirtualCarPhysics physics)
{
    if (physics == null)
    {
        return;
    }

    physics.StopRunning();

    Rigidbody rb = physics.GetComponent<Rigidbody>();
    if (rb == null)
    {
        rb = physics.GetComponentInParent<Rigidbody>();
    }

    if (rb != null)
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }
}
```

## UI 상태

최소 UI 상태:

- `Ready`: 시작 전
- `Running`: 주행 중
- `Finished`: 도착 완료

도착 시 보여줄 수 있는 값:

- 완료 메시지
- 걸린 시간
- 현재 맵 이름
- 참가자 이름 또는 슬롯

타이머가 필요하면 `RCCarCourseController`에 다음 값을 둔다.

```csharp
private float runStartedAt;
private float finishTime;
```

시작 시:

```csharp
runStartedAt = Time.time;
```

완료 시:

```csharp
finishTime = Time.time - runStartedAt;
```

## 개발 중 시각화

도착 지점이 완전히 투명하면 에디터에서 위치를 놓치기 쉽다. 개발 중에는 Gizmo를 그리는 것이 좋다.

`RCCarEndTrigger`에 추가할 수 있는 코드:

```csharp
private void OnDrawGizmos()
{
    Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
    Gizmos.matrix = transform.localToWorldMatrix;

    BoxCollider box = GetComponent<BoxCollider>();
    if (box != null)
    {
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
```

이렇게 하면 게임 화면에서는 보이지 않고, Scene 뷰에서는 도착 영역을 확인할 수 있다.

## 구현 순서

### Step 1. End 태그와 레이어 정리

1. Unity Editor에서 `End` 태그를 만든다.
2. 필요하면 `EndTrigger` 또는 `CourseTrigger` 레이어를 만든다.
3. 차량 루트 또는 차량 콜라이더는 `Car` 태그 또는 `Car` 레이어를 사용한다.
4. Physics Layer Collision Matrix에서 차량 레이어와 End 레이어가 트리거 이벤트를 낼 수 있는지 확인한다.

### Step 2. End 트리거 프리팹 또는 씬 오브젝트 만들기

1. `Create Empty`로 `End_Map_00` 생성
2. `BoxCollider` 추가
3. `Is Trigger` 체크
4. 콜라이더 크기를 도착선 너비보다 넉넉하게 설정
5. `MeshRenderer`는 없애거나 꺼둔다
6. `RCCarEndTrigger` 스크립트 부착
7. End 오브젝트의 forward 방향이 차량 진행 방향을 바라보게 회전
8. Scene 뷰에서 위치 확인

### Step 3. `RCCarEndTrigger` 구현

1. `OnTriggerEnter(Collider other)`에서 차량 진입 기록
2. `OnTriggerExit(Collider other)`에서 차량 이탈 기록
3. 차량별 overlap count로 모든 차량 콜라이더가 빠져나갔는지 확인
4. End forward 방향 기준으로 정방향 통과인지 확인
5. `VirtualCarPhysics` 탐색
6. 중복 완료 방지
7. `RCCarCourseController.FinishCar()` 호출
8. `ResetFinishState()` 제공

### Step 4. `RCCarCourseController` 구현

1. 상태 enum 추가
2. 현재 상태 필드 추가
3. `BeginRun()`, `FinishCar()`, `ResetCourse()` 구현
4. `endTriggersByMap` 배열 추가
5. `ApplyMapEndTrigger(int mapIndex)` 구현
6. 필요하면 타이머와 UI 텍스트 연결

### Step 5. `ChangeMap` 연결

1. `ChangeMap`에 `RCCarCourseController courseController` 참조 추가
2. `ApplyMap()` 이후 현재 맵의 End 트리거 활성화
3. 맵 변경 시 코스 상태 초기화
4. 런타임 맵 인덱스 범위 예외 처리

### Step 6. `ButtonRestart` 연결

1. `ButtonRestart`에 `RCCarCourseController courseController` 참조 추가
2. `RestartCar()` 완료 후 `courseController.ResetCourse()` 호출
3. 리스타트 시 End 트리거의 중복 완료 플래그 초기화 확인

### Step 7. 네트워크 완료 상태 연결

1. `RCCarEndTrigger`에서 `NetworkRCCar`를 찾을 수 있게 한다.
2. 호스트/server에서만 최종 완료 처리한다.
3. 완료한 userId 또는 slotIndex를 저장한다.
4. `HostExecutionScheduler`가 완료된 슬롯을 건너뛰게 한다.
5. `HostStatusPanelReporter`에 `finished` 상태를 표시한다.

## 테스트 체크리스트

### 단일 차량

- 게임 시작 시 차량이 현재 맵 시작 위치에 있는가
- 리스타트 시 같은 시작 위치로 돌아오는가
- End 트리거가 보이지 않아도 충돌 감지가 되는가
- End 트리거를 정방향으로 완전히 통과했을 때 `VirtualCarPhysics.IsRunning`이 false가 되는가
- End 트리거에 들어왔다가 뒤로 빠져나가면 완료 처리되지 않는가
- End 도착 후 Rigidbody 속도가 0이 되는가
- 도착 후 같은 트리거 안에서 중복 완료 로그가 반복되지 않는가
- 리스타트 후 다시 도착 처리가 가능한가

### 맵 변경

- 맵 0에서 `End_Map_00`만 활성화되는가
- 맵 1에서 `End_Map_01`만 활성화되는가
- 맵 변경 시 이전 End 트리거 완료 상태가 초기화되는가
- `ChangeMap.carSpawnPoses`와 End 위치가 서로 맞는가
- 런타임 맵에서 End 인덱스가 없을 때 경고만 나오고 게임이 멈추지 않는가

### 네트워크

- 호스트가 도착 판정을 처리하는가
- 클라이언트 단독 판정으로 완료 상태가 바뀌지 않는가
- 완료한 차량만 멈추는가
- 완료하지 않은 차량은 계속 실행 가능한가
- 참가자가 나가거나 차량이 despawn될 때 완료 상태가 정리되는가

## 흔한 문제와 해결

### `OnTriggerEnter` 또는 `OnTriggerExit`가 호출되지 않음

확인할 것:

- End 오브젝트의 `BoxCollider.isTrigger`가 켜져 있는가
- 차량 또는 End 중 하나에 `Rigidbody`가 있는가
- 현재 차량에는 `VirtualCarPhysics` 때문에 `Rigidbody`가 있어야 한다
- 차량 콜라이더가 실제로 End 영역에 닿는가
- 레이어 충돌 매트릭스에서 두 레이어가 막혀 있지 않은가
- 3D Collider와 2D Collider를 섞어 쓰지 않았는가

### End가 투명해서 위치를 찾기 어려움

해결:

- `OnDrawGizmos()`로 Scene 뷰에 박스 표시
- 개발 중에는 반투명 초록 머테리얼 사용
- 빌드 전 또는 최종 씬에서는 `MeshRenderer`만 끄기

### 도착 후 차량이 조금 더 움직임

원인:

- `StopRunning()`은 모터를 끄지만 Rigidbody 속도가 남아 있을 수 있다.

해결:

- 도착 처리에서 `rb.velocity`와 `rb.angularVelocity`도 0으로 설정한다.

### 리스타트 후 다시 도착해도 완료 처리가 안 됨

원인:

- `RCCarEndTrigger` 내부의 `finishedCars`, `overlapCounts`, `entrySideByCar` 상태가 초기화되지 않았다.

해결:

- `ButtonRestart.RestartCar()` 이후 `courseController.ResetCourse()` 호출
- `ResetCourse()`에서 모든 End 트리거의 `ResetFinishState()` 호출

### 맵 변경 후 이전 맵 End에서 종료됨

원인:

- 이전 맵의 End 트리거가 계속 활성화되어 있다.

해결:

- `ApplyMapEndTrigger(mapIndex)`에서 현재 맵 End만 활성화하고 나머지는 비활성화한다.

## 권장 최종 구조

최소 구현:

```text
ChangeMap
  - 시작 위치 관리
  - 맵 변경
  - 현재 맵 End 활성화 요청

ButtonRestart
  - 시작 위치로 차량 복귀
  - 코스 완료 상태 초기화 요청

RCCarEndTrigger
  - 투명 도착 영역
  - 차량 진입/정방향 통과 감지

RCCarCourseController
  - Ready/Running/Finished 상태 관리
  - 차량 정지
  - End 트리거 초기화
  - UI/타이머 확장 지점
```

네트워크 확장:

```text
RCCarEndTrigger
  -> NetworkRCCar 확인
  -> host/server만 완료 처리

HostNetworkCarCoordinator 또는 별도 FinishTracker
  -> userId/slotIndex 완료 기록

HostExecutionScheduler
  -> 완료된 슬롯 실행 건너뛰기

HostStatusPanelReporter
  -> finished 상태 표시
```

## 1차 작업 범위 제안

먼저 단일 차량 기준으로 종료 지점을 완성하는 것이 좋다.

1. `RCCarEndTrigger.cs` 추가
2. `RCCarCourseController.cs` 추가
3. 씬에 `End_Map_00`부터 배치
4. `ChangeMap`에 코스 컨트롤러 참조 추가
5. `ButtonRestart`에 코스 컨트롤러 참조 추가
6. 맵 변경, 리스타트, 도착 테스트

이후 네트워크 주행에서 완료 상태가 필요하면 `NetworkRCCar`, `HostExecutionScheduler`, `HostStatusPanelReporter`와 연결한다.

## 최종 판단

투명 콜라이더를 `End`로 쓰는 것은 적절하다. 이 프로젝트에서는 3D `BoxCollider`를 `Is Trigger`로 두고, 렌더러만 숨긴 도착 트리거를 맵별로 배치하는 방식이 가장 단순하고 유지보수하기 쉽다. 완료 판정은 `OnTriggerEnter`로 진입을 기록하고 `OnTriggerExit`에서 정방향 통과를 확인하는 방식이 더 자연스럽다. 중요한 점은 도착 지점 자체보다 도착 후 상태 처리다. 완료되면 차량 주행을 멈추고, Rigidbody 속도를 초기화하고, 리스타트와 맵 변경 시 완료 상태를 반드시 초기화해야 한다.
