# NetworkCarPlan

## 0) 문서 목적
- 본 문서는 `NetworkCarPRD.md(v1.1)`를 구현 가능한 작업 단위로 변환한 개발 실행 계획서다.
- 목표는 코드 작성 시 "무엇을 어디에 어떻게 연결할지"를 빠르게 참조하는 것이다.
- 본 문서는 1차 프로토타입 기준이며, 정책 우선순위는 다음과 같다.
  - Host 화면에서 동작 보장
  - 승인 기반 Slot 생성
  - XML/JSON 매핑 후 순차 실행(0.2초 time-slice)
  - 이탈/재입장 처리 제외

---

## 1) 구현 범위 요약

### In Scope (1차)
- Host 전용 NetworkCar Coordinator 도입
- 승인 이벤트 -> `Count++` -> `UserId <-> Slot` 매핑
- Slot 수만큼 차량 생성 + 무지개 색상 배정
- Host가 확보한 XML/JSON을 사용자 차량에 매핑
- Host 실행 버튼으로 `slot=1..maxCount` 순차 실행
- Host 상태/오류 Text 패널 출력

### Out of Scope (1차)
- Client 화면 실시간 반영
- 이탈/재입장 정교 처리
- 네트워크 트랜스폼 동기화
- 병렬 실행 최적화

---

## 2) 전체 흐름 (End-to-End)

### Flow A: 참여 승인 -> Slot/Car 생성
1. Host가 Join Request 승인 성공
2. `HostParticipantSlotRegistry.RegisterIfNew(userId, userName)` 호출
3. 신규 유저라면 `slotIndex = ++count`
4. `HostCarSpawner.EnsureCarForSlot(slotIndex, userId)` 호출
5. `HostCarColorAllocator.ResolveColor(slotIndex)`로 색 배정
6. 상태 패널 업데이트

### Flow B: 코드 공유 -> Host 저장 -> 매핑
1. Client가 block share 업로드 (기존 구현)
2. Host가 목록/상세 확인 (기존 구현)
3. Host가 save-to-my-level 클릭 (기존 구현)
4. save 성공 시 `shareId`, `savedSeq`, `userId` 획득
5. `HostBlockCodeResolver.ResolveCodeAsync(...)`로 XML/JSON 확보
6. `HostCarBindingStore.UpsertCode(userId, code)` 저장
7. 상태 패널에 매핑 성공/실패 출력

### Flow C: 실행 버튼 -> 순차 실행 루프
1. Host가 Run 클릭
2. `HostExecutionScheduler.Start()`
3. while running:
   - slot 1..maxCount 순회
   - userId 조회
   - code 없는 slot은 skip + 상태 출력
   - code 있으면 해당 차량 executor에 주입 후 0.2초 실행
4. Stop 클릭 시 루프 종료

---

## 3) 클래스 설계

## 3-1) 신규 클래스 목록

### 1. `HostNetworkCarCoordinator` (중앙 오케스트레이터)
- 책임
  - 모든 서브시스템 초기화/연결
  - 이벤트 라우팅
  - 실행 시작/중지 제어
- 주요 의존성
  - `ChatRoomManager`
  - `HostParticipantSlotRegistry`
  - `HostCarSpawner`
  - `HostBlockCodeResolver`
  - `HostCarBindingStore`
  - `HostExecutionScheduler`
  - `HostStatusPanelReporter`
- 주요 메서드(예시)
  - `Initialize()`
  - `HandleJoinApproved(...)`
  - `HandleBlockShareSaved(...)`
  - `StartExecution()`
  - `StopExecution()`

### 2. `HostParticipantSlotRegistry` (Slot/Count 관리)
- 책임
  - `count`, `userId<->slot` 매핑 보관
  - 동일 user 중복 등록 방지
- 데이터
  - `int MaxCount`
  - `Dictionary<string,int> slotByUserId`
  - `SortedDictionary<int,string> userIdBySlot`
- 주요 메서드
  - `bool TryRegisterUser(string userId, string userName, out HostParticipantSlot slot)`
  - `bool TryGetUserIdBySlot(int slot, out string userId)`
  - `bool TryGetSlotByUserId(string userId, out int slot)`

### 3. `HostCarSpawner` (차량 생성/참조 캐시)
- 책임
  - slot별 차량 생성
  - 차량 컴포넌트 참조 캐시 (`VirtualCarPhysics`, `BlockCodeExecutor`, `VirtualArduinoMicro`)
- 주요 메서드
  - `HostCarRuntimeRefs EnsureCar(int slot, string userId, Color color)`
  - `bool TryGetCar(string userId, out HostCarRuntimeRefs refs)`

### 4. `HostCarColorAllocator` (순번 색상 정책)
- 책임
  - slot index 기반 색상 반환
- 정책
  - 7색 무지개 팔레트 순환
- 주요 메서드
  - `Color Resolve(int slotIndex)`

### 5. `HostBlockCodeResolver` (XML/JSON 확보)
- 책임
  - save 결과를 기반으로 XML/JSON 확보
  - 우선순위: `savedSeq DB 조회` -> 실패 시 fallback
- 주요 메서드
  - `Task<ResolvedCodePayload> ResolveBySavedSeqAsync(string userId, string shareId, int savedSeq, string token)`

### 6. `HostCarBindingStore` (실행 데이터 저장소)
- 책임
  - 사용자별 실행 데이터 보관
  - 차량 레퍼런스 + 코드 + 마지막 오류 상태 통합 관리
- 데이터
  - `Dictionary<string, HostCarBinding>`
- 주요 메서드
  - `UpsertParticipant(...)`
  - `UpsertCarRefs(...)`
  - `UpsertCode(...)`
  - `TryGetBySlot(...)`
  - `GetAllOrderedBySlot()`

### 7. `HostExecutionScheduler` (0.2초 순차 실행)
- 책임
  - slot 순차 루프 실행
  - code 미존재 slot 스킵
  - 현재 실행 상태 노출
- 주요 메서드
  - `Start()`, `Stop()`
  - `IEnumerator RunLoop()`

### 8. `HostStatusPanelReporter` (UI 상태 출력)
- 책임
  - 상태 텍스트/오류 텍스트 표준 포맷으로 출력
  - 디버그 로그와 UI 동시 갱신
- 주요 메서드
  - `SetInfo(string msg)`
  - `SetWarning(string msg)`
  - `SetError(string msg)`
  - `SetRuntimeStatus(...)`

---

## 3-2) 기존 클래스와의 연결 포인트

### `ChatRoomManager` (기존)
- 사용 이벤트
  - `OnJoinRequestDecisionSucceeded`
  - `OnBlockShareSaveSucceeded`
  - 필요 시 `OnBlockShareDetailFetchSucceeded`
- 주의점
  - `IsBusy` 전역 직렬화 제약 있음
  - Coordinator는 호출 충돌을 줄이기 위해 요청 타이밍 분리 필요

### `HostBlockShareAutoRefreshPanel` (기존)
- 역할
  - Host가 share 선택/상세 확인
- Coordinator와의 관계
  - 직접 의존 최소화
  - 필요 데이터는 `ChatRoomManager` 이벤트를 기준으로 가져감

### `HostBlockShareSaveToMyLevelButton` (기존)
- 역할
  - save 실행 및 debug verify
- 확장 포인트
  - save 성공 후 coordinator로 payload 전달 hook 추가 가능

### `BlockCodeExecutor` (기존, 수정 대상)
- 필수 개선
  - 전역 `LatestRuntimeJson` 의존 완화
  - 차량별 JSON 주입 API 추가
- 권장 추가 API
  - `public bool LoadProgramFromJson(string runtimeJson, string sourceTag = null)`
  - `public void ResetRuntimeState()`

---

## 4) 데이터 구조 상세

```csharp
public sealed class HostParticipantSlot
{
    public string UserId;
    public string UserName;
    public int SlotIndex;
    public DateTime ApprovedAtUtc;
}

public sealed class ResolvedCodePayload
{
    public string UserId;
    public string ShareId;
    public int SavedSeq;
    public string Xml;
    public string Json;
    public bool IsSuccess;
    public string Error;
    public DateTime ResolvedAtUtc;
}

public sealed class HostCarRuntimeRefs
{
    public GameObject CarObject;
    public VirtualCarPhysics Physics;
    public BlockCodeExecutor Executor;
    public VirtualArduinoMicro Arduino;
}

public sealed class HostCarBinding
{
    public string UserId;
    public string UserName;
    public int SlotIndex;
    public string LatestShareId;
    public int LatestSavedSeq;
    public string Xml;
    public string Json;
    public bool HasCode;
    public Color AssignedColor;
    public HostCarRuntimeRefs RuntimeRefs;
    public string LastError;
    public DateTime LastUpdatedUtc;
}
```

---

## 5) 실행 알고리즘 상세

### 5-1) 순차 실행 기본 정책
- tick 간격: `slotRunSeconds = 0.2f` (Inspector 조절 가능)
- 순회 범위: `slot = 1..slotRegistry.MaxCount`
- 동작
  - slot에 user가 없으면 skip
  - user는 있으나 code 없으면 skip
  - code 있으면 해당 executor에 주입 후 run

### 5-2) 스케줄러 의사코드
```csharp
while (isRunning)
{
    for (int slot = 1; slot <= maxCount; slot++)
    {
        if (!registry.TryGetUserIdBySlot(slot, out var userId))
        {
            reporter.SetWarning($"slot={slot} user missing, skip");
            continue;
        }

        if (!bindingStore.TryGet(userId, out var binding) || !binding.HasCode)
        {
            reporter.SetWarning($"slot={slot} user={userId} code missing, skip");
            continue;
        }

        bool loaded = runtimeBinder.TryApplyJson(binding.RuntimeRefs.Executor, binding.Json);
        if (!loaded)
        {
            reporter.SetError($"slot={slot} user={userId} json load failed");
            continue;
        }

        runtimeBinder.RunFor(binding.RuntimeRefs.Physics, 0.2f);
        reporter.SetRuntimeStatus(slot, userId, "running");
        yield return new WaitForSeconds(0.2f);
    }
}
```

### 5-3) 주의
- 현재 정책은 "동시 실행"이 아니라 "시분할 순차 실행"이다.
- 경쟁/공정성보다 구현 안정성 우선.

---

## 6) 파일/폴더 작업 계획

### 신규 파일 (권장)
- `Assets/Scripts/NetworkCar/HostNetworkCarCoordinator.cs`
- `Assets/Scripts/NetworkCar/HostParticipantSlotRegistry.cs`
- `Assets/Scripts/NetworkCar/HostCarSpawner.cs`
- `Assets/Scripts/NetworkCar/HostCarColorAllocator.cs`
- `Assets/Scripts/NetworkCar/HostBlockCodeResolver.cs`
- `Assets/Scripts/NetworkCar/HostCarBindingStore.cs`
- `Assets/Scripts/NetworkCar/HostExecutionScheduler.cs`
- `Assets/Scripts/NetworkCar/HostStatusPanelReporter.cs`
- `Assets/Scripts/NetworkCar/HostRuntimeBinder.cs`

### 수정 파일 (예상)
- `Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs`
  - `LoadProgramFromJson(...)` 추가
  - 기존 `LoadProgram()`과 공존
- 필요 시:
  - `HostBlockShareSaveToMyLevelButton.cs` (save 성공 이벤트 연결 확장)
  - `HostJoinRequestMonitorGUI.cs` 또는 승인 처리 경로 클래스 (slot 등록 hook)

---

## 7) 이벤트 연결 계획

### 7-1) 승인 이벤트
- Source: `ChatRoomManager.OnJoinRequestDecisionSucceeded`
- 조건: `info.Approved == true`
- Action:
  - `slotRegistry.TryRegisterUser(...)`
  - `carSpawner.EnsureCar(...)`
  - `bindingStore.UpsertParticipant(...)`
  - `reporter.SetInfo(...)`

### 7-2) 저장 성공 이벤트
- Source: `ChatRoomManager.OnBlockShareSaveSucceeded`
- Action:
  - `resolver.ResolveBySavedSeqAsync(...)`
  - 성공 시 `bindingStore.UpsertCode(...)`
  - 실패 시 `bindingStore.LastError`, `reporter.SetError(...)`

### 7-3) 실행 버튼 이벤트
- Source: Host UI Button
- Action:
  - Start: `scheduler.Start()`
  - Stop: `scheduler.Stop()`

---

## 8) UI/상태 패널 사양

### 표시 텍스트(권장 1줄 요약 + 상세)
- Summary
  - `HostCount`, `MappedCodeCount`, `Running(true/false)`, `CurrentSlot`
- Last Action
  - 최근 성공 작업 (승인/저장/매핑/실행)
- Last Error
  - 최근 오류 1건

### 로그 포맷
- info: `[NetworkCar][INFO] ...`
- warn: `[NetworkCar][WARN] ...`
- error: `[NetworkCar][ERROR] ...`

---

## 9) 구현 순서 (실제 코딩 단계)

### Step 1: 데이터/등록 계층
- `HostParticipantSlotRegistry`, `HostCarBindingStore` 작성
- 단위 동작 검증:
  - 신규 사용자 slot 증가
  - 중복 사용자 무시

### Step 2: 차량 계층
- `HostCarSpawner`, `HostCarColorAllocator` 작성
- 검증:
  - slot 별 차량 생성
  - color 순서 확인

### Step 3: 코드 계층
- `HostBlockCodeResolver` 작성
- `BlockCodeExecutor.LoadProgramFromJson(...)` 구현
- 검증:
  - 임의 json 주입 -> executor load success

### Step 4: 스케줄러
- `HostExecutionScheduler`, `HostRuntimeBinder` 작성
- 검증:
  - 1..maxCount 순차 반복
  - code 없는 slot skip 로그

### Step 5: 코디네이터/UI 연결
- `HostNetworkCarCoordinator`, `HostStatusPanelReporter` 작성
- `ChatRoomManager` 이벤트 연동
- Run/Stop 버튼 연결

---

## 10) 테스트 계획

### 시나리오 테스트
1. 승인 3명, 코드 3명 모두 있음
- 기대: 3대 생성, 색상 3개, 0.2초 순차 반복

2. 승인 3명, 코드 1명만 있음
- 기대: 1명만 실행, 2명은 skip 로그

3. 중복 승인 이벤트 동일 user
- 기대: Count 증가 없음, 동일 slot 유지

4. save 성공 but xml/json empty
- 기대: `HasCode=false`, 패널 오류 출력

5. Run/Stop 반복 클릭
- 기대: 중복 코루틴 생성 없음, 정상 중지/재시작

### 로그 확인 포인트
- 승인 시 slot 매핑 로그
- 코드 매핑 성공/실패 로그
- 스케줄러 slot 전환 로그
- 마지막 오류 패널 반영

---

## 11) 완료 기준 (Definition of Done)
- Host에서 승인된 인원수만큼 차량이 생성된다.
- `UserId <-> Slot` 매핑이 유지되고 중복 등록이 없다.
- Host가 저장한 XML/JSON이 사용자 차량에 연결된다.
- 실행 버튼으로 `1..MaxCount` 순차 실행이 반복된다.
- 코드 없는 슬롯은 오류 없이 스킵된다.
- 상태 패널로 현재 상태/오류를 즉시 확인 가능하다.

---

## 12) 2차 확장 포인트
- 이탈/재입장 처리
- 현재 접속자 기준 Slot 재정렬
- 동시 실행/가변 타임슬라이스
- Client 동기화 UI

---

## 13) 코딩 시 체크리스트
- `FindObjectOfType` 의존을 신규 코드에서 최소화했는가
- `ChatRoomManager.IsBusy` 충돌 구간을 피했는가
- 슬롯 루프가 null/code-missing에서 안전하게 skip 하는가
- 실행 코루틴 중복 시작 방지가 있는가
- 패널 로그가 실제 디버깅에 충분한가

---

마지막 업데이트: 2026-03-26
