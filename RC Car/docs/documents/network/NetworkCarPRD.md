# NetworkCarPRD

## 0) 문서 버전
- 버전: v1.1 (ADD 반영)
- 기준일: 2026-03-26
- 목적: 구현 전에 개발 방향/우선순위/프로토타입 가정을 고정해서, 작업 중 의사결정 흔들림을 줄인다.

## 1) 문서 목적
- `03_NetworkCarTest`에서 **Host 화면 기준으로만** 다중 차량을 생성/실행한다.
- Client가 선택한 블록코드(XML/JSON)를 공유하면, Host가 확인/저장 후 사용자 차량과 매핑하여 실행한다.
- 기존 ChatRoom API + VirtualArduino 코드를 최대 재활용하고, 부족한 부분만 신규 클래스로 추가한다.

## 2) 현재 구현 상태 (코드 기준)
### 2-1. 이미 구현된 항목
- Client -> Host 블록 공유 업로드
  - `ClientBlockShareUploadButton` -> `ChatRoomManager.UploadBlockShare(...)`
- Host 블록 공유 목록/상세 조회
  - `HostBlockShareAutoRefreshPanel`
- Host save-to-my-level
  - `HostBlockShareSaveToMyLevelButton` -> `ChatRoomManager.SaveBlockShareToMyLevel(...)`
- save API 템플릿 적용
  - `POST /api/chat/block-shares/{shareId}/save-to-my-level`
- 저장 검증 디버그
  - `savedSeq` 기준 `GET /api/user-level/{seq}`로 XML/JSON 길이 확인

### 2-2. 현재 한계
- 참여자 목록을 차량 생성용으로 확정 관리하는 계층이 없음
- `Car <-> User <-> Code(XML/JSON)` 매핑 관리 계층 없음
- `BlockCodeExecutor`가 전역 `LatestRuntimeJson` 기반이라 다중 독립 실행에 불리함
- 일부 컴포넌트가 `FindObjectOfType` 기반이라 단일 차량 전제 코드가 남아 있음

## 3) ADD 반영 확정 정책 (프로토타입)
1. Host 전용 우선
- Client 화면 동기화는 1차 범위 제외
- Host가 보이고 동작하면 1차 목표 달성

2. Count/Slot 정책
- Host가 참가 승인할 때마다 `Count++`
- `UserId <-> SlotIndex(=Count)`를 Host 메모리에 매핑
- 프로토타입에서는 사용자 이탈/재입장으로 Count를 줄이지 않음

3. 코드 데이터 파이프라인
- Client가 선택한 코드 공유 -> Host 상세 확인 -> Host 저장 -> Host 차량 매핑
- 매핑 소스는 최종적으로 Host가 확보한 XML/JSON

4. 실행 정책 (핵심)
- 순차 실행(Time Slice) 방식 사용
- 예: 참여자 3명일 때 `1번 0.2초 -> 2번 0.2초 -> 3번 0.2초` 반복
- 공정성보다 안정성이 우선인 프로토타입 정책

5. 실행 트리거
- 방 입장/승인으로 Slot 준비
- Host가 실행 버튼을 누르면 `1..MaxCount` 순회 시작

6. 지연/성능 가정
- DB/API 지연은 프로토타입에서 일부 감수
- 초기 목표는 "정확히 돌게 만들기"이며 고성능 최적화는 후순위

7. 오류 노출
- Panel Text를 활성화해 Host에게 문제 지점을 즉시 표시
- 예: 코드 미매핑, 저장 실패, 파싱 실패, 실행 스킵 슬롯

## 4) 범위
### 4-1. In Scope (1차)
- Host 전용 차량 생성/매핑/순차 실행
- 승인된 사용자 기준 Slot 생성
- 무지개 색상 순번 할당
- Host 상태 패널(실행/오류)

### 4-2. Out of Scope (1차)
- Client 쪽 실시간 시뮬레이션 반영
- 사용자 이탈/재입장 정교 처리
- 네트워크 물리 동기화
- 성능 고도화(프레임 최적화/병렬 실행)

## 5) 기능 요구사항 (FR)
### FR-1. Host 전용 실행 게이트
- Host 계정일 때만 NetworkCar 실행 로직 활성화
- Client 계정은 관찰 대상에서 제외

### FR-2. 승인 기반 Slot 등록
- 승인 이벤트 성공 시 Slot 생성
- `SlotIndex`는 1부터 증가
- 동일 UserId 중복 등록 방지

### FR-3. 차량 생성/색상
- Slot마다 차량 1대 생성
- 색상은 Slot 순서 기준 무지개 팔레트 할당
- 팔레트 초과 시 순환

### FR-4. 코드 매핑
- 사용자별 최신 XML/JSON을 Host가 보관
- 기본 키: `UserId`
- 보조 키: `ShareId`, `SavedSeq`, `UpdatedAtUtc`

### FR-5. 순차 실행 스케줄러
- Host 실행 버튼 클릭 시 루프 시작
- `for slot=1..maxCount` 순회
- 각 슬롯 실행 시간 기본값 `0.2초`
- 코드 없는 슬롯은 스킵하고 상태 로그 남김

### FR-6. 상태/오류 패널
- 필수 표시 항목
  - 승인된 총 인원/현재 슬롯
  - 매핑 완료 사용자 수
  - 마지막 오류 메시지
  - 현재 실행중 사용자/슬롯

## 6) 아키텍처 제안
### 6-1. 신규 클래스 (권장)
1. `HostNetworkCarCoordinator`
- 전체 오케스트레이션 진입점
- 승인/코드수신/실행 상태를 한 곳에서 제어

2. `HostParticipantSlotRegistry`
- `Count`, `UserId<->Slot` 관리
- 중복 사용자 처리

3. `HostCarSpawner`
- Slot 기반 차량 생성/회수
- 차량 컴포넌트 참조 캐싱

4. `HostBlockCodeResolver`
- Share/Save 결과를 사용자 코드로 정규화
- XML/JSON 캐시 관리

5. `HostExecutionScheduler`
- `0.2초` 순차 실행 루프 담당
- `Start/Stop/Pause` 제어

6. `HostStatusPanelReporter`
- Text UI 갱신 전담
- 오류/경고/진행 상태 표준 포맷 적용

### 6-2. 기존 코드 재활용 전략
- `ChatRoomManager`: API 입출력 재활용
- `HostBlockShareAutoRefreshPanel`: Host 선택/상세 재활용
- `HostBlockShareSaveToMyLevelButton`: 저장/검증 로직 재활용
- `VirtualArduino` 계열: 차량 실행 코어 재활용

### 6-3. 보완 필요 포인트
- `BlockCodeExecutor`
  - 권장 API 추가: `LoadProgramFromJson(string json)`
  - 전역 static 의존을 줄여 차량별 독립 실행 가능하게 변경
- `VirtualCarPhysics`/`VirtualArduinoMicro`
  - `FindObjectOfType` 의존 최소화
  - 생성 시 명시 주입 방식으로 전환

## 7) 데이터 모델
```csharp
Dictionary<string, HostParticipantSlot> _slotByUserId;
SortedDictionary<int, string> _userIdBySlot;
Dictionary<string, HostCarBinding> _carByUserId;

sealed class HostParticipantSlot
{
    public string UserId;
    public string UserName;
    public int SlotIndex;
    public DateTime ApprovedAtUtc;
}

sealed class HostCarBinding
{
    public string UserId;
    public int SlotIndex;
    public string LatestShareId;
    public int LatestSavedSeq;
    public string Xml;
    public string Json;
    public bool HasCode;
    public Color AssignedColor;
    public GameObject CarObject;
    public VirtualCarPhysics Physics;
    public BlockCodeExecutor Executor;
    public string LastError;
    public DateTime LastUpdatedUtc;
}
```

## 8) 실행 플로우 (ADD 반영)
### 8-1. 승인/차량 준비
1. Host가 Join Request 승인
2. `Count++`, Slot 생성
3. Slot에 대응되는 차량 생성 및 색상 부여
4. 상태 패널 업데이트

### 8-2. 코드 매핑
1. Client가 share 업로드
2. Host가 목록/상세 확인
3. Host가 save-to-my-level 수행
4. Host가 XML/JSON 확보 후 `UserId` 기준 매핑
5. `HasCode=true`로 전환

### 8-3. 순차 실행
1. Host 실행 버튼 클릭
2. `slot=1..maxCount` 순회
3. 각 슬롯 `0.2초` 실행
4. 코드 없음/오류 슬롯은 스킵
5. 반복 루프

## 9) API 사용 범위
### 9-1. 현재 사용 API
- `GET /api/chat/rooms/{roomId}/join-requests`
- `POST/PATCH /api/chat/join-requests/{requestId}/decision`
- `GET /api/chat/rooms/{roomId}/block-shares`
- `GET /api/chat/rooms/{roomId}/block-shares/{shareId}`
- `POST /api/chat/block-shares/{shareId}/save-to-my-level`
- `GET /api/user-level/{seq}` (검증/디버그)

### 9-2. 백엔드 추가 요구 정책
- 1차 프로토타입은 **추가 백엔드 API 없이** 진행
- Host가 승인 시점 Count/Slot을 직접 관리
- Host가 코드 수신 시 UserId와 코드를 다시 매핑해 실행

## 10) 구현 단계 (상세)
### Phase A. Slot/상태 계층
- `HostParticipantSlotRegistry` 구현
- 승인 이벤트 수신 연결
- 중복 UserId 방지

### Phase B. 차량 계층
- Slot 기반 차량 생성
- 무지개 색상 할당 적용
- 차량 참조 캐시 구축

### Phase C. 코드 계층
- save 결과를 사용자 코드에 연결
- XML/JSON 없음/파싱 실패 처리
- `HasCode` 상태 관리

### Phase D. 실행 계층
- `HostExecutionScheduler` 구현
- `0.2초` 슬롯 순차 루프
- Start/Stop 버튼 연동

### Phase E. 운영 가시성
- `HostStatusPanelReporter` 구현
- 상태/오류 Text 표준화

## 11) 리스크와 완화
1. 전역 런타임 JSON 충돌
- 완화: 차량별 JSON 로드 API 추가

2. `IsBusy`로 인한 요청 스킵
- 완화: 요청 큐 또는 HostCar 전용 lightweight API 클라이언트 분리 고려

3. DB/API 지연
- 완화: 상태 패널에 "대기/재시도" 명확히 표시

4. 이탈 미처리(의도된 제약)
- 완화: 문서/코드에 "프로토타입 가정" 명시

## 12) 수용 기준 (Acceptance Criteria)
1. 승인된 사용자 수만큼 Host에 차량이 생성된다.
2. 각 차량은 Slot 순서 무지개 색상을 가진다.
3. Host가 저장한 사용자 코드가 해당 사용자 차량에 매핑된다.
4. 실행 버튼 클릭 시 `1..MaxCount` 순서로 `0.2초`씩 반복 실행된다.
5. 코드 미존재/오류 슬롯은 스킵되고 상태 패널에 원인이 표시된다.
6. Client 쪽 화면 갱신 없이도 Host 단독 실행이 완료된다.

## 13) 다음 단계에서 확장할 항목 (2차)
- 사용자 이탈/재입장 처리
- 실시간 접속자 기준 Count 재계산
- 동시 실행 또는 가변 스케줄러
- Client 화면 동기화

---
마지막 업데이트: 2026-03-26
