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

## 14) 버그

### 로그 기반 현재 결론 (2026-03-30)
- Host: `test2`
- Client: `test1`, `sdj3701`
- 런타임 상태:
  - `slot=1, user=test1, state=no-code`
  - `slot=2, user=sdj3701, state=no-code`
- 핵심 경고:
  - `save ignored: user not approved. shareId=20, user=test2, savedSeq=43`
  - `Cannot instantiate objects with a parent which is persistent. New object will be created without a parent.`

### Bug-001: Save Owner 해석 오류로 코드 매핑 실패 (`no-code`)
- 현상
  - save 성공 로그는 있는데 두 슬롯 모두 `no-code`로 실행 스킵.
- 로그 근거
  - save detail/list에서는 `senderUserId=test1`가 확인됨.
  - save 성공 처리에서는 `resolved user=test2(host)`로 해석되어 `save ignored: user not approved` 발생.
- 원인
  - `save-to-my-level` 응답의 owner 계열 값이 "공유 원본 사용자"가 아니라 "저장 수행자(host)"일 수 있음.
  - 현재 해석 우선순위가 host 값을 먼저 잡으면, 승인 슬롯 사용자(`test1/sdj3701`)와 불일치.
- 수정 방향
  - `ResolveUserIdFromShare` 우선순위를 다음으로 재정의:
    1. `_shareToUserId[shareId]` (list/detail의 `senderUserId` 기반)
    2. `OwnerUserId` (단, 승인 슬롯 사용자일 때만 허용)
    3. `ResponseBody` 파싱
  - `OwnerUserId`가 host이고 승인 슬롯에 없으면 무조건 fallback으로 재해석.
  - 재해석 후에도 실패하면 `shareId`, `senderUserId`, `ownerUserId`, `approvedUsers`를 한 줄로 로깅.

### Bug-002: 서로 다른 client 코드가 동일 코드처럼 보이는 문제
- 현상
  - `test1`, `sdj3701`의 XML/JSON이 다른데 결과가 동일하게 보이거나 같은 데이터로 반영된 것처럼 보임.
- 로그 근거
  - save 시점에 `Selection linked(detail). shareId=20`만 저장 수행됨.
  - list/detail 요청이 `ChatRoomManager is busy. detail refresh queued.`로 지연되는 구간 다수 확인.
- 원인
  - 저장 버튼 클릭 시점의 선택 정보가 최신 detail과 불일치(지연/큐잉)할 수 있음.
  - 결과적으로 의도한 share가 아닌 이전 share(예: 20)가 반복 저장될 수 있음.
- 수정 방향
  - Save 버튼 동작 전 `SelectedShareId`와 `SelectedDetailInfo.BlockShareId` 일치 검증 필수.
  - 불일치 시 강제 detail fetch 완료 후 save 진행.
  - 저장 직전 UI에 `shareId`, `senderUserId`, `sourceUserLevelSeq`를 고정 표시해 오저장 방지.

### Bug-003: Persistent Parent에 차량 생성 시 부모 이탈 경고
- 현상
  - `Cannot instantiate objects with a parent which is persistent...`
- 원인
  - `_carRoot`가 `DontDestroyOnLoad` 계층(영속 오브젝트)인 상태에서 scene 오브젝트를 자식으로 생성 시도.
- 영향
  - 차량이 의도한 루트에 붙지 않아 씬 정리/검색/참조 추적이 꼬일 수 있음.
- 수정 방향
  - `_carRoot`를 비영속(scene) 오브젝트로 고정.
  - 또는 생성 후 명시적으로 scene 루트로 재부모 지정.
  - `EnsureCarForSlot` 시작 시 `_carRoot` 유효성 검사 로그 추가.

### Bug-004: Save 처리 정책과 승인 슬롯 정책 불일치
- 현상
  - 현재 정책상 "승인 슬롯 외 user save 차단"은 맞지만, owner 해석 오류가 있으면 정상 save도 차단됨.
- 원인
  - 정책 자체는 맞고, 해석 로직 정확도가 부족한 상태.
- 수정 방향
  - 승인 슬롯 화이트리스트는 유지.
  - 다만 차단 전에 `shareId -> senderUserId` 재확인 단계를 반드시 거치도록 개선.
  - 차단 로그 메시지를 `ignored` 단일 문구가 아닌 `reason code`로 분리:
    - `NOT_APPROVED_USER`
    - `OWNER_RESOLVE_FAILED`
    - `STALE_SHARE_DETAIL`

### Bug-005: 2번째 참여자(`test1`) 정보가 누락된 것처럼 보이는 원인
- 현상
  - `shareId=22(sdj3701)`는 save/resolve 로그가 보이는데, `test1(shareId=23)`은 매핑 로그가 안 보임.
  - 실행 시 `slot=2 user=test1 state=no-code` 또는 한쪽만 코드 반영된 상태가 발생.
- 로그 근거
  - list 수신: `shareId=23 ... senderUserId=test1`, `shareId=22 ... senderUserId=sdj3701`
  - 실제 save 클릭: `Save requested. shareId=22` (1회)
  - resolver 성공: `[HostBlockCodeResolver] Code resolved. user=sdj3701, shareId=22, savedSeq=44`
  - 즉, 현재 로그에서는 `test1` share(`23`)에 대한 save/resolve 호출이 없음.

#### 원인 A: test1 share를 실제로 저장하지 않음
- 현재 구조에서 각 client 코드 반영은 `shareId`별 `save-to-my-level`을 각각 수행해야 한다.
- 로그상 Host가 저장한 대상은 `shareId=22`만 확인되며, `shareId=23(test1)` 저장 호출이 빠져 있다.
- `HostBlockShareSaveToMyLevelButton`의 기본 정책(`_disableButtonAfterSuccess=true`) 때문에 첫 save 성공 후 버튼이 비활성화되면, 다른 share 저장이 이어서 안 될 수 있다.

#### 원인 B: Detail 파싱에서 `userId`가 비어 들어오는 케이스 존재
- 로그: `<color=orange>[HostNetworkCarCoordinator][MAPPING] Share detail userId is empty. shareId=22</color>`
- 상세 API 응답에는 `senderUserId`가 있는데도, `ChatRoomManager`의 detail 변환 결과가 빈 값으로 들어오는 경우가 있다.
- 이 경우 detail 기반 매핑만 의존하면 owner 해석이 흔들린다.

#### 원인 C: Save 타이밍과 Detail 동기화 지연
- 로그 다수: `ChatRoomManager is busy. detail refresh queued.`
- 선택한 share와 실제 detail이 어긋난 상태에서 저장하면 의도한 사용자 코드가 아닌 다른 share가 저장될 수 있다.

### Bug-005 해결 가이드
1. 저장 절차 강제
   - `shareId=22(sdj3701)` 저장 후,
   - `shareId=23(test1)`도 별도로 저장한다.
   - 두 건 모두 `Code resolved`/`Code mapped` 로그가 있어야 한다.
2. 매핑 우선순위 고정
   - `share list/detail`의 `senderUserId` 캐시(`_shareToUserId`)를 1순위로 사용.
   - save 응답 owner는 2순위(승인 슬롯 사용자일 때만 허용).
3. detail stale 가드 유지
   - 저장 직전 `SelectedShareId == SelectedDetailInfo.BlockShareId` 불일치 시 save 중단 + detail 재요청.
4. detail userId 비어있음 보강
   - detail 파싱 결과 `UserId`가 비면 raw body에서 `senderUserId`를 재추출해 보정.
5. save 버튼 다중 저장 허용
   - "마지막으로 저장한 share"와 "현재 선택 share"가 같을 때만 버튼 비활성화.
   - 다른 share를 선택하면 버튼이 다시 활성화되어 2번째/3번째 참여자도 연속 저장 가능해야 함.

### Bug-005 재검증 체크리스트
1. `Save requested. shareId=22` 후 `Code mapped ... user=sdj3701` 확인
2. `Save requested. shareId=23` 후 `Code mapped ... user=test1` 확인
3. Run 시 `slot=1`, `slot=2` 모두 `state=running` 확인
4. `state=no-code`가 남아 있으면 해당 slot user의 최근 save 로그(`shareId`)가 존재하는지 먼저 확인

### Bug-006: Save 버튼 1회 성공 후 비활성화로 다중 참여자 저장 누락
- 현상
  - 첫 save 성공 이후 버튼이 비활성화되어 두 번째 참여자 share 저장이 불가능하거나 누락됨.
  - 결과적으로 `slot=1/2` 중 한쪽만 `Code mapped`가 찍히고 나머지는 `state=no-code` 유지.
- 원인
  - `_disableButtonAfterSuccess`가 전역 성공 상태(`HasSaveResult && LastSaveResult`)만 보고 버튼을 막아, "다른 share 저장" 시나리오를 고려하지 못함.
- 코드 수정
  - `HostBlockShareSaveToMyLevelButton.UpdateButtonInteractable()`
  - 비활성화 조건을 다음으로 변경:
    - `selectedShareId == lastSavedShareId`일 때만 비활성화
    - 다른 share 선택 시 다시 활성화
- 기대 로그
  - `Save requested. shareId=22` -> `Code mapped ... user=sdj3701`
  - `Save requested. shareId=23` -> `Code mapped ... user=test1`
  - 이후 Run에서 두 슬롯 모두 `state=running`

### Bug-007: Detail 부분 파싱으로 `userId` 누락되는 케이스
- 현상
  - detail API 원문에 `senderUserId`가 있어도 변환 결과 `ChatRoomBlockShareInfo.UserId`가 빈 문자열로 들어올 때가 있음.
  - 매핑 디버그: `<color=orange>[HostNetworkCarCoordinator][MAPPING] Share detail userId is empty. shareId=22</color>`
- 원인
  - `ParseBlockShareDetailResponse`에서 payload가 부분적으로만 파싱되면 `info != null`이 되어 fallback 파싱이 건너뛰어질 수 있음.
- 코드 수정
  - `ChatRoomManager.ParseBlockShareDetailResponse(...)`에 후처리(backfill) 추가:
    - `info.UserId`가 비면 raw body의 `senderUserId/userId/requesterUserId` 재추출
    - `UserLevelSeq`, `Message`, `CreatedAtUtc`도 비어 있으면 raw body로 보정
- 기대 효과
  - detail 기반 매핑 힌트 누락 감소
  - `_shareToUserId` 캐시 보강 속도 향상

### Bug-008: 신규 참여자 승인 시 기존 참여자(`sdj3701`) XML/JSON 재갱신(중복 반영)
- 현상
  - `sdj3701`가 먼저 참여/저장되면 정상적으로 XML/JSON이 1회 매핑됨.
  - 이후 `test1`이 참여(approve)하는 시점에, `sdj3701`의 XML/JSON이 다시 갱신되는 로그가 추가로 발생함.
  - 의도는 "신규 참여자 slot/car 생성"이어야 하는데, 기존 참여자 코드 매핑까지 다시 실행되는 것이 문제.
- 재현 시나리오
1. `sdj3701` join approve
2. `shareId=22` save -> `Code resolved/mapped user=sdj3701`
3. `test1` join approve
4. `sdj3701`에 대해 추가 `resolved/mapped` 또는 XML/JSON 갱신 로그 재발생
- 원인 후보
  - save 성공 이벤트 1건이 `pending retry` + `정상 성공 경로`에서 중복 처리될 수 있음.
  - block share list/detail auto-refresh가 이전 share의 pending 상태를 다시 깨우는 구조일 수 있음.
  - 동일 `shareId/savedSeq`에 대한 "이미 반영됨" idempotency 가드가 약하면 재적용이 발생함.
  - join approve 흐름과 code mapping 흐름의 경계가 흐리면, join 이벤트 타이밍에 불필요한 매핑 루틴이 섞일 수 있음.
- 상세 해결 방법
1. 흐름 분리 강제
   - `JoinApprove` 경로는 `slot/car 생성`만 수행하고, `ResolveCode/UpsertCode/TryApplyJson`은 호출하지 않도록 정책 고정.
   - 코드 매핑은 `OnBlockShareSaveSucceeded` 단일 진입점에서만 수행.
2. idempotency(중복 반영 방지) 강화
   - 중복 키를 `shareId:savedSeq`로 통일하고, 처리 시작 전에 `in-progress/completed`를 동시에 체크.
   - 이미 같은 키가 처리 완료면 resolver 진입 자체를 차단.
   - 추가로 binding 기준 보강:
     - `binding.LatestShareId == shareId`
     - `binding.LatestSavedSeq == savedSeq`
     - `json hash` 동일
     - 위 3개가 모두 같으면 `재적용 skip`.
3. pending retry 단일화
   - `owner unresolved`일 때만 pending 등록.
   - owner 매핑 완료 후 해당 `shareId` pending만 1회 재시도하고 즉시 제거.
   - retry 횟수 제한(예: 1회)과 TTL(예: 10초)을 둬 무한 재처리 방지.
4. 이벤트 중복 바인딩 점검
   - `OnEnable/OnDisable`에서 save 관련 이벤트 핸들러가 중복 등록되지 않도록 보장.
   - scene 전환/재활성화 시 동일 handler가 2번 이상 붙는지 점검.
5. 매핑 디버그(주황색) 확장
   - `<color=orange>[...][MAPPING] Reapply skipped. reason=duplicate-key, shareId=..., savedSeq=...</color>`
   - `<color=orange>[...][MAPPING] Mapping trigger. source=save-success|pending-retry|manual-save</color>`
   - 어떤 경로가 갱신을 일으켰는지 반드시 로그로 식별 가능하게 함.
6. 저장 구조 변경(`UserId` 기반 + `List` 버전 관리)
   - 현재처럼 유저당 단일 `Xml/Json` 필드 덮어쓰기 대신, 유저 버킷 내부에 버전 리스트를 유지:
   - `Dictionary<string, UserCodeBucket>` (key=`userId`)
   - `UserCodeBucket.Versions: List<CodeVersion>`
   - `CodeVersion` 필수 필드: `shareId`, `savedSeq`, `xml`, `json`, `createdAtUtc`, `jsonHash`
   - `UserCodeBucket.ActiveVersionKey`(예: `shareId:savedSeq`)를 두고 실행 시에는 활성 버전 1개만 적용
   - 저장 시 동작:
   - 같은 `shareId:savedSeq`가 이미 있으면 append 금지(중복 차단)
   - 다른 버전이면 append, 필요 시 `ActiveVersionKey`만 최신으로 교체
   - 실행 시 동작:
   - `slot -> userId` 매핑으로 유저 찾기
   - `userId -> ActiveVersionKey -> CodeVersion` 순서로 조회 후 해당 차량에만 적용
   - 장점:
   - 신규 참여자 approve가 와도 기존 유저 버전 데이터가 덮어써지지 않음
   - "누가 어떤 버전을 언제 적용했는지" 추적이 쉬움
   - 디버깅 시 `userId` 단위로 상태를 분리해 확인 가능
- 기대 결과
  - `test1` 참여 시 `sdj3701` 코드가 다시 매핑되지 않음.
  - 동일 `shareId/savedSeq`는 한 번만 반영되고, 이후는 `skip` 로그만 남음.
  - 신규 참여자 승인은 slot/car 생성 로그만 증가함.

### 우선 수정 순서 (로그 기준)
1. `HostNetworkCarCoordinator.ResolveUserIdFromShare(...)` 우선순위 재정의
2. `HostBlockShareSaveToMyLevelButton` 저장 직전 selected/detail 동기화 강제
3. `HostBlockShareSaveToMyLevelButton` 다중 share 저장 가능하도록 버튼 비활성화 조건 수정
4. `ChatRoomManager.ParseBlockShareDetailResponse(...)` userId/backfill 보강
5. `shareId:savedSeq` 기준 idempotency/pending-retry 단일화로 중복 매핑 차단
6. `UserId -> List<CodeVersion>` 구조로 저장소 변경(활성 버전 키 포함)
7. `HostCarSpawner`의 `_carRoot` 영속성 검증 및 parent 정책 고정

### 재검증 체크리스트
1. `shareId=22(save)` 시 `resolvedUserId=sdj3701`로 매핑되는지 확인
2. `shareId=23(save)` 시 `resolvedUserId=test1`로 매핑되는지 확인
3. Run 시 `slot1`, `slot2` 모두 `state=running`으로 진입하는지 확인
4. `Share detail userId is empty` 주황 로그가 감소/미발생하는지 확인
5. 두 저장 결과의 XML/JSON source가 서로 다른 shareId로 로깅되는지 확인
6. `Cannot instantiate objects with a parent which is persistent` 경고 대신 `carRoot is persistent. spawn without parent` 경고만 남는지 확인
7. `test1` join approve만 수행했을 때 `sdj3701`의 `Code mapped`/`TryApplyJson` 재실행 로그가 다시 나오지 않는지 확인
8. 동일 user(`sdj3701`)의 버전 리스트에 중복 키(`shareId:savedSeq`)가 생기지 않는지 확인
9. Run 시 `slot -> userId -> ActiveVersionKey` 조회로 정확히 해당 user 차량만 갱신되는지 확인

---

마지막 업데이트: 2026-03-30
