# Lobby Plan

## 1) 배경 및 목표
- 현재 로비는 버튼 클릭 시 즉시 씬 전환하는 단순 구조다.
- 향후에는 `_roomNameInputField` 값을 기반으로 웹 API에 방 생성을 요청하고, DB 테이블/방 리소스가 준비된 뒤에만 게임 씬으로 이동해야 한다.
- 목표는 `UI -> Flow -> Service -> Ready Event -> Scene 이동`으로 책임을 분리해, 기능 확장(필드 추가/상태 추가/오류 처리)에 견고한 구조를 확보하는 것이다.

---

## 2) 2026-03-19 현재 상태 요약
- `LobbyUIController`는 룸 정보 UI 열기/닫기만 담당하고 있다.
- `LoadCarSceneButton`는 버튼 클릭 시 `03_NetworkCarTest`를 즉시 로드한다.
- 룸 생성 API 호출, 생성 진행 상태, 완료 이벤트, 실패 이벤트, 중복 클릭 제어 계층이 아직 없다.

---

## 3) 설계 원칙
- `Command`와 `Event`를 분리한다.
- UI는 입력/버튼/표시만 담당하고 비즈니스 흐름은 `LobbyRoomFlow`에서 오케스트레이션한다.
- 씬 전환 트리거는 "버튼 클릭"이 아니라 "OnRoomReady 수신"으로 고정한다.
- 네트워크/백엔드 계약 변화에 대비해 서비스 레이어(`RoomService`)를 단일 진입점으로 둔다.
- 중복 요청, 타임아웃, 취소, 재시도 정책을 흐름 계층에서 일관 처리한다.

---

## 4) 목표 아키텍처 (요청 흐름 기준)
1. `LobbyUIController`
룸 이름 입력값을 읽고 `CreateRoom(roomName)` 요청을 보낸다.
2. `LobbyRoomFlow` (또는 `LobbyCoordinator`)
요청 유효성 검사, 로딩 상태, 중복 클릭 방지, API 호출, 성공/실패 판정을 수행한다.
3. `RoomService`
웹 API 호출을 담당한다. (DB 테이블/룸 생성 요청, 준비 상태 조회)
4. `OnRoomReady(RoomInfo)` 이벤트
룸 준비 완료 시 발행한다.
5. `LobbySceneNavigator`
`OnRoomReady`를 구독하고 `03_NetworkCarTest`로 이동한다.

---

## 5) 정식 시나리오 (확정)
1. `Create Room` 버튼 클릭
주체: 사용자
결과: `LobbyUIController.ShowRoomInfoUI()` 호출
2. `RoomInfoUI` 활성화
주체: `LobbyUIController`
결과: 방 생성 입력 패널 노출
3. `RoomName` 입력
주체: 사용자
결과: `_roomNameInputField`에 값 입력
4. `RoomInfoUI`의 `CreateRoom` 버튼 클릭
주체: 사용자
결과: `LobbyUIController`가 입력값 trim 후 `LobbyRoomFlow.CreateRoom(roomName)` 호출
5. `RoomName` 빈 값 검증
주체: `LobbyRoomFlow`
결과: 빈 문자열이면 `OnRoomCreateFailed(VALIDATION)` 발행 후 종료
6. 유효 입력이면 웹 API 요청
주체: `LobbyRoomFlow -> RoomService`
결과: `POST /rooms` 요청 전송, `Submitting` 상태 진입
7. 테이블(룸 리소스) 생성 완료 확인
주체: `RoomService` + `LobbyRoomFlow`
결과: 즉시 `READY` 또는 `PROVISIONING -> READY` 확인 (`GET /rooms/{roomId}/status` 또는 `GET /room-jobs/{jobId}`)
8. 완료 시 씬 전환 이벤트 호출
주체: `LobbyRoomFlow`
결과: `OnRoomReady(RoomInfo)` 1회 발행
9. 씬 전환
주체: `LobbySceneNavigator`
결과: `OnRoomReady` 수신 후 `03_NetworkCarTest` 로드

---

## 6) 컴포넌트 책임 상세

### 6-1. LobbyUIController
- 책임:
- `_roomNameInputField` 값 읽기
- 사용자 액션(생성 버튼 클릭) 전달
- 로딩/오류/상태 텍스트 표시
- 비책임:
- API 직접 호출
- 씬 전환 직접 수행
- 핵심 동작:
- 버튼 클릭 시 현재 입력 문자열을 정규화(trim) 후 Flow에 전달
- Flow 이벤트를 받아 버튼 활성/비활성, 스피너, 안내 문구 갱신

### 6-2. LobbyRoomFlow (LobbyCoordinator)
- 책임:
- 입력 검증(빈 문자열, 길이 제한, 허용 문자)
- 동시 요청 방지(`IsBusy`)
- `RoomService` 호출 및 상태 전이 관리
- 성공/실패 이벤트 발행
- 타임아웃/취소/재시도 정책 적용
- 비책임:
- 실제 HTTP 구현
- Unity UI 컴포넌트 직접 접근
- 씬 로딩 직접 수행

### 6-3. RoomService
- 책임:
- HTTP 요청/응답 직렬화
- 인증 헤더 부착(`AuthManager` 토큰 활용)
- 백엔드 에러 코드를 도메인 에러로 변환
- 준비 상태 확인(폴링 또는 서버 푸시 래핑)
- 비책임:
- UI 상태 변경
- 씬 전환

### 6-4. LobbySceneNavigator
- 책임:
- `OnRoomReady(RoomInfo)` 구독
- 룸 컨텍스트 저장 후 `03_NetworkCarTest` 씬 로드
- 구독/해제 생명주기 관리
- 비책임:
- 룸 생성 요청
- API 처리

---

## 7) 이벤트 계약 (초안)

### 7-1. 요청(Command)
- `CreateRoom(string roomName)`
- 호출 주체: `LobbyUIController`
- 처리 주체: `LobbyRoomFlow`

### 7-2. 알림(Event)
- `OnRoomCreateStarted(string roomName)`
- `OnRoomCreateProgress(RoomProvisioningProgress progress)` (선택)
- `OnRoomReady(RoomInfo roomInfo)`
- `OnRoomCreateFailed(RoomCreateError error)`
- `OnRoomCreateCanceled()`

### 7-3. 계약 규칙
- `CreateRoom`가 수락되면 반드시 `Ready`, `Failed`, `Canceled` 중 하나의 종료 이벤트가 1회 발생한다.
- `IsBusy == true` 상태에서는 신규 `CreateRoom` 요청을 거절하거나 무시한다.
- `OnRoomReady`는 한 요청당 최대 1회만 발행한다.

---

## 8) 도메인 모델 (초안)

### 8-1. CreateRoomRequest
- `roomName`
- `hostUserId`
- `idempotencyKey`
- `maxPlayers` (옵션)
- `mapId` (옵션)
- `mode` (옵션)

### 8-2. CreateRoomResponse
- `requestAccepted` (bool)
- `roomId`
- `jobId` (비동기 프로비저닝용)
- `status` (`READY`, `PROVISIONING`, `FAILED`)
- `message`

### 8-3. RoomInfo
- `roomId`
- `roomName`
- `hostUserId`
- `tableName` (백엔드가 노출 가능한 경우)
- `networkEndpoint` (옵션)
- `createdAtUtc`

### 8-4. RoomCreateError
- `code` (`VALIDATION`, `NETWORK`, `AUTH`, `CONFLICT`, `TIMEOUT`, `UNKNOWN`)
- `userMessage`
- `rawMessage`
- `retryable`

---

## 9) 상태 전이 (State Machine)
- `Idle`
- `Submitting`
- `Provisioning`
- `Ready`
- `Failed`
- `Canceled`

전이 규칙:
1. `Idle -> Submitting`: 유효한 `CreateRoom` 요청 수락
2. `Submitting -> Ready`: API가 즉시 준비 완료 반환
3. `Submitting -> Provisioning`: 비동기 준비 필요 반환(`jobId` 존재)
4. `Provisioning -> Ready`: 준비 완료 확인
5. `Submitting/Provisioning -> Failed`: API/검증/서버 오류
6. `Submitting/Provisioning -> Canceled`: 사용자 취소 또는 씬 종료
7. `Ready/Failed/Canceled -> Idle`: 후처리 완료 후 대기 상태 복귀

---

## 10) API 통신 전략 (가정)
- 1차 요청: `POST /rooms` (룸 생성 + 테이블 준비 요청)
- 상태 조회: `GET /rooms/{roomId}/status?jobId=...` 또는 `GET /room-jobs/{jobId}`
- 인증: `Authorization: Bearer <accessToken>`
- 멱등성: `Idempotency-Key` 헤더 권장
- 타임아웃:
- 생성 요청 타임아웃 (예: 10~15초)
- 준비 대기 타임아웃 (예: 60~120초)
- 폴링 간격:
- 1~2초 고정 또는 지수 백오프

---

## 11) 씬 전환 조건 및 컨텍스트 전달
- 씬 전환은 `LobbySceneNavigator`만 수행한다.
- 전환 조건은 `OnRoomReady(RoomInfo)` 수신 시점으로 제한한다.
- 다음 씬에서 필요한 최소 컨텍스트:
- `roomId`
- `roomName`
- `hostUserId`
- 기타 네트워크 접속 정보
- 컨텍스트 저장 방식:
- `DontDestroyOnLoad` 기반 런타임 세션 객체 또는 전용 `RoomSessionContext` 싱글톤

---

## 12) UX 정책
- 생성 버튼 클릭 시 즉시 비활성화 (`IsBusy` 반영)
- 진행 중 텍스트 표시: "룸 생성 중...", "서버 준비 중..."
- 실패 시 사용자 친화 메시지 출력 + 재시도 버튼 활성화
- 취소 기능이 있으면 `Cancel` 버튼 제공
- 입력 정책:
- 빈 값 금지
- 길이 제한
- 금지 문자 제한(백엔드 계약에 맞춤)

---

## 13) 신뢰성/운영 고려사항
- 중복 클릭 방지: `IsBusy` 게이트
- 중복 요청 방지: `Idempotency-Key`
- 로그 추적:
- `requestId`, `jobId`, `roomId`, `userId`를 연동 로그로 남김
- 민감정보 보호:
- 토큰/개인정보 원문 로그 금지
- 장애 대응:
- `retryable` 오류만 재시도 버튼 제공
- `AUTH` 오류는 로그인 재유도

---

## 14) 테스트 계획

### 14-1. 단위 테스트
- 룸 이름 검증 규칙
- 상태 전이 규칙
- Busy 상태에서 중복 요청 차단
- 오류 코드 매핑

### 14-2. 통합 테스트
- 정상 요청 -> Ready -> 씬 전환
- 요청 수락 후 Provisioning -> Ready -> 씬 전환
- 네트워크 오류 -> Failed
- 인증 만료 -> Failed + 로그인 유도
- 타임아웃 -> Failed(재시도 가능)

### 14-3. 수동 QA 체크리스트
- [ ] 빈 룸명 입력 시 요청 차단
- [ ] 생성 중 버튼 연타 무시
- [ ] 실패 후 재시도 정상 동작
- [ ] 성공 시 `03_NetworkCarTest` 정확히 1회 로드
- [ ] 씬 이동 후 룸 컨텍스트 확인

---

## 15) 단계별 개발 순서 (코드 작업 전 기준)
1. 백엔드 API 계약 확정
2. 도메인 모델(`RoomInfo`, `RoomCreateError`) 확정
3. `LobbyRoomFlow` 상태/이벤트 계약 확정
4. `RoomService` 에러 매핑 정책 확정
5. `LobbySceneNavigator` 전환 정책 확정
6. QA 시나리오/완료 기준 검토 후 구현 착수

---

## 16) 백엔드 확인 필요 항목 (Open Questions)
1. 방 생성 API가 동기 완성인지 비동기 프로비저닝(job)인지?
2. 준비 완료 판정 기준 필드명은 무엇인지? (`READY`/`ACTIVE` 등)
3. 실패 코드 표준은 무엇인지? (중복 룸명, 권한, 용량 초과 등)
4. 멱등 키 지원 여부 및 TTL 정책은?
5. 최대 룸명 길이/허용 문자 규칙은?
6. 생성 후 반환되는 필수 컨텍스트 필드는 무엇인지?

---

## 17) 완료 기준 (Definition of Ready for Implementation)
- 아키텍처 책임 경계(UI/Flow/Service/Navigator)가 팀 내 합의됨
- 이벤트 계약과 상태 전이 규칙이 확정됨
- API 스펙(요청/응답/에러 코드)이 확정됨
- QA 체크리스트가 승인됨
- 위 항목 충족 시 구현 시작
