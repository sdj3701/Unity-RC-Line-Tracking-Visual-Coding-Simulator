# Chat Integration Plan (Lobby -> 03_NetworkCarTest)

작성일: 2026-03-23  
범위: Unity 클라이언트(로비/네트워크카 씬)에서 웹 채팅 API 연동을 위한 분석 및 구현 계획 문서  
주의: 이 문서는 계획 문서이며, 코드 구현은 포함하지 않는다.

## 1) 목표와 현재 전제

- 목표: `Lobby`에서 룸 준비 완료 후 `03_NetworkCarTest`로 이동하는 흐름에 채팅 기능을 안정적으로 통합한다.
- 입력 데이터: 웹 개발자가 전달한 MySQL 스키마(`chat_rooms`, `chat_room_users`, `chat_messages`, `chat_room_logs`).
- 현재 Unity 구조 전제:
- `LobbyRoomFlow`가 룸 생성/상태 폴링 후 `OnRoomReady(RoomInfo)` 이벤트 발행.
- `LobbySceneNavigator`가 `OnRoomReady` 수신 시 `03_NetworkCarTest` 로드.
- `RoomSessionContext`로 룸 컨텍스트를 씬 간 전달.
- `AuthManager`에 사용자 정보(`CurrentUser.userId`)와 `accessToken`이 있음.

## 1-1) Unity에서 바로 구현 가능한 기능 (간단 요약)

- `chat_rooms` 기준:
- 채팅방 생성, 채팅방 목록/상태 표시, 방 제목/최대 인원 UI 표시.
- `chat_room_users` 기준:
- 방 입장/퇴장 처리, 멤버 목록 UI, 권한(OWNER/MEMBER) 및 상태(ACTIVE/LEFT/KICKED) 반영.
- `chat_messages` 기준:
- 메시지 보내기/불러오기, 타입별 렌더링(TEXT/IMAGE/FILE/SYSTEM), 삭제 메시지(`is_deleted`) 처리.
- `chat_room_logs` 기준:
- 입장/퇴장/강퇴 같은 방 이벤트를 시스템 로그 또는 시스템 메시지로 표시.

즉시 가능한 MVP 범위:
- "방 생성 -> 입장 -> 텍스트 메시지 송수신 -> 퇴장" 플로우를 Unity UI에서 완성 가능.

## 2) DB 스키마 상세 분석

### 2-1. `chat_rooms`

- 목적: 채팅방 메타 정보 저장.
- 핵심 컬럼:
- `room_id` (BIGINT UNSIGNED, PK): 채팅방 고유 식별자.
- `title` (VARCHAR 255, NOT NULL): 채팅방 제목.
- `user_id` (VARCHAR 32, NOT NULL, FK -> users.user_id): 방장.
- `max_user_count` (INT, default 100): 최대 인원.
- `status` (VARCHAR 20, default ACTIVE): 방 상태.
- `created_at`, `updated_at`: 생성/수정 시간.
- 제약/의미:
- 방장 유저 삭제 시 `chat_rooms`도 `ON DELETE CASCADE`로 삭제됨.
- 상태값은 문자열(enum 유사)이며 DB 레벨 CHECK 제약은 없음.

Unity 영향:
- `room_id`는 숫자형이지만, Unity 쪽 모델은 문자열로 보관하는 편이 API 포맷 변화(문자열 ID 응답) 대응에 유리.
- `RoomInfo.RoomId`와 타입이 다를 수 있으므로 "게임 룸 ID == 채팅 룸 ID" 여부를 계약으로 확정해야 한다.

### 2-2. `chat_room_users`

- 목적: 채팅방 참여자/권한/상태 관리.
- 핵심 컬럼:
- `room_id`, `user_id` FK.
- `role` (OWNER/MEMBER).
- `status` (ACTIVE/LEFT/KICKED).
- `UNIQUE(room_id, user_id)`: 한 유저는 같은 방에 중복 row 불가.
- 제약/의미:
- 유저가 재입장할 때 `INSERT`가 아닌 기존 row의 `status` 변경(upsert) 전략이 필요할 수 있음.
- `LEFT`와 `KICKED`를 분리 저장하므로 UI 정책도 분리 가능.

Unity 영향:
- 단순 "참여중 여부(bool)"가 아닌 상태 기반 처리 필요.
- 메시지 송신 전에 서버가 `ACTIVE` 멤버인지 검사할 가능성이 높음.

### 2-3. `chat_messages`

- 목적: 메시지 본문/파일 메타 저장.
- 핵심 컬럼:
- `message_id` (BIGINT UNSIGNED, PK).
- `room_id` FK.
- `user_id` (nullable, FK -> users.user_id, ON DELETE SET NULL).
- `message` (TEXT).
- `message_type` (TEXT/IMAGE/FILE/SYSTEM).
- `file_url`, `original_file_name`.
- `is_deleted` (soft delete 플래그).
- `created_at`.
- `INDEX(room_id, created_at)`: 방별 시간순 조회 최적화.
- 제약/의미:
- 유저 삭제 시 메시지는 남고 `user_id`만 NULL 처리됨.
- soft delete 구조이므로 "삭제된 메시지" 렌더링 정책 필요.

Unity 영향:
- 작성자 정보가 없는 메시지(`user_id == null`)를 렌더링할 수 있어야 함.
- 페이징/최신 동기화는 `room_id + created_at` 혹은 `message_id` cursor 전략이 필요.

### 2-4. `chat_room_logs`

- 목적: 방 이벤트 감사 로그.
- 핵심 컬럼:
- `action_type` (CREATE/JOIN/LEAVE/KICK).
- `user_id`(행동 주체), `target_user_id`(대상).
- `message` (로그 설명).
- 제약/의미:
- 운영/분석/관리 기능에 유용.
- 일반 채팅 타임라인에 보여줄지 별도 관리자 뷰로 둘지 정책 결정 필요.

Unity 영향:
- MVP 채팅 화면에서는 필수 아님.
- 추후 "시스템 메시지"로 변환 표시할지 결정 가능.

## 3) 스키마 기반 도메인 규칙(추론)

- 룸과 멤버는 분리 테이블이므로 "방 생성"과 "참여 처리"가 내부적으로 별도 트랜잭션일 가능성 있음.
- 메시지는 soft delete 지원이므로 하드 삭제 전제를 두면 안 됨.
- 상태 컬럼이 문자열이라 백엔드가 허용값을 엄격히 검증해야 하며, 클라이언트도 예상 외 값을 방어적으로 처리해야 함.
- `chat_messages`에 read/unread, edit 이력 컬럼이 없음:
- MVP는 "실시간 메시지 표시"에 집중하고, 읽음/수정 기능은 범위 밖으로 둔다.

## 4) Unity 현재 구조와 충돌 가능 지점

### 4-1. 룸 ID 의미 충돌 리스크

- 현재 `LobbyRoomFlow`는 `/api/rooms` 기반 `RoomInfo.RoomId`를 전달.
- 채팅 스키마는 `chat_rooms.room_id`를 별도로 가정.
- 리스크:
- 게임 룸 ID와 채팅 룸 ID가 다르면 `RoomSessionContext`만으로는 채팅 초기화 불가.

대응 방침:
- 계약 우선순위 1: "게임 룸 생성 시 채팅 룸도 자동 생성/매핑되는지" 웹 개발자에게 확정.
- 미확정 시: `ChatSessionContext`를 별도로 두고 `chatRoomId`를 명시적으로 전달.

### 4-2. 기존 소켓 채팅 코드와 중복

- `Assets/Scripts/Server/Packet/*`에 기존 TCP 패킷 채팅 코드가 있으나 웹 API 스키마와는 별도 체계.
- 리스크:
- 같은 UI에서 두 채널(TCP/Web API) 혼용 시 상태 불일치 가능.

대응 방침:
- 이번 범위에서는 웹 API 채팅을 기준 경로로 확정.
- 기존 TCP 채팅은 디버그/레거시 경로로 분리(기능 플래그)하는 전략 권장.

## 5) 필요한 API 계약(스키마 기반 최소안)

아래는 스키마에서 역으로 도출한 최소 API 계약안이다. 실제 경로/필드명은 백엔드와 고정 필요.

### 5-1. Room API

- `POST /api/chat/rooms`
- 입력: `title`, `maxUserCount`(optional).
- 출력: `roomId`, `title`, `ownerUserId`, `status`, `createdAt`.
- `GET /api/chat/rooms/{roomId}`
- 출력: 룸 메타 + 내 멤버 상태(optional).
- `POST /api/chat/rooms/{roomId}/join`
- 출력: 내 멤버십(`role`, `status`).
- `POST /api/chat/rooms/{roomId}/leave`
- 출력: 성공 여부, 최종 status(`LEFT`).

### 5-2. Message API

- `GET /api/chat/rooms/{roomId}/messages?limit=50&beforeMessageId=...`
- 출력: 메시지 배열(오래된 순/최신 순 정책 확정 필요).
- `POST /api/chat/rooms/{roomId}/messages`
- 입력: `messageType`, `message`, `fileUrl`, `originalFileName`.
- 출력: 저장된 메시지 단건.
- `DELETE /api/chat/messages/{messageId}` (soft delete)
- 출력: `isDeleted=true` 반영 결과.

### 5-3. Realtime API (선택)

- `GET /api/chat/rooms/{roomId}/events` (SSE) 또는 WebSocket endpoint.
- 미지원 시 폴링 fallback:
- `GET /api/chat/rooms/{roomId}/messages?afterMessageId=...`

### 5-4. 공통 규약

- 인증: `Authorization: Bearer <accessToken>`.
- 권한 실패 코드: `401`, `403` 구분.
- 멱등키: 메시지 중복 전송 방지를 위한 `Idempotency-Key` 헤더 권장.
- 시간 포맷: UTC ISO-8601 권장(현 스키마 DATETIME은 타임존 정보 없음).

## 6) 클라이언트 설계 계획 (코드 작성 전 확정 항목)

### 6-1. 컨텍스트 모델

- `RoomSessionContext`는 게임 룸 정보를 유지.
- 채팅용 별도 컨텍스트 설계:
- `ChatSessionContext` (예: `ChatRoomId`, `Title`, `MyUserId`, `JoinedAt`, `LastReadMessageId`).
- 이유: 게임 룸과 채팅 룸의 식별자가 1:1이 아닐 수 있음.

### 6-2. 계층 분리

- UI 계층: 채팅 패널 열기/닫기, 메시지 입력/렌더링.
- Flow 계층: 초기화, join/leave, send, sync 상태 관리.
- Service 계층: HTTP/SSE/WebSocket 통신.
- Repository 계층(선택): 메모리 캐시/중복 제거/정렬.

### 6-3. 상태 머신 초안

- `Idle`
- `Initializing` (room resolve + join)
- `SyncingHistory`
- `Live`
- `Error`
- `Leaving`

종료 규칙:
- `Live` 진입 전 메시지 송신 버튼 비활성.
- `Error` 시 재시도 버튼/자동 백오프 정책 적용.

## 7) Lobby -> 03_NetworkCarTest 연계 시나리오

### 7-1. 권장 시퀀스

1. Lobby에서 `OnRoomReady(RoomInfo)` 수신.
2. `RoomSessionContext.Set(roomInfo)` 저장.
3. `03_NetworkCarTest` 로드.
4. 씬 진입 시 Chat Bootstrap 수행:
- A안: `roomInfo.RoomId`로 채팅 룸 조회/생성.
- B안: 백엔드가 매핑된 `chatRoomId`를 함께 주면 바로 join.
5. 최근 메시지 로드 후 `Live` 전환.

### 7-2. 실패 시 UX 원칙

- 채팅 초기화 실패가 "게임 씬 진입"을 막지 않도록 분리.
- 채팅 패널에만 에러/재시도 제공.
- 인증 만료는 전역 인증 정책(AuthManager)과 일관되게 처리.

## 8) 메시지/멤버 렌더링 정책

- `message_type == SYSTEM`: 일반 사용자 발화와 다른 스타일.
- `user_id == null`: `"알 수 없음"` 또는 `"탈퇴한 사용자"`로 표시.
- `is_deleted == 1`: 본문 대체 문구(`삭제된 메시지입니다`) 사용.
- 멤버 status:
- `ACTIVE`: 목록 표시.
- `LEFT`: 퇴장 표시(선택).
- `KICKED`: 재입장 차단 안내.

## 9) 성능 및 데이터 정합성 계획

- 초기 로드: 최근 30~50개 메시지.
- 이전 메시지: 스크롤 상단 도달 시 cursor pagination.
- 실시간 동기화:
- WebSocket/SSE 없으면 1~2초 폴링 + 백그라운드에서만 수행.
- 중복 제거 키: `message_id`.
- 정렬 기준: `created_at`, 동률 시 `message_id`.

## 10) 보안 및 운영 고려사항

- 토큰은 `AuthManager.Instance.GetAccessToken()` 사용.
- 민감정보 로그 금지:
- access token, file_url 서명값, 개인식별정보 raw 출력 금지.
- 업로드 파일은 직접 multipart보다 presigned URL 방식 권장.
- 서버에서 반드시 멤버십(`chat_room_users.status=ACTIVE`) 검증 후 송신 허용.

## 11) QA 계획

### 11-1. 기능 시나리오

- 방 생성자 입장 후 메시지 송수신.
- 일반 멤버 입장/퇴장 후 재입장.
- 강퇴 유저 송신 거부.
- 유저 삭제 후 기존 메시지 렌더링(`user_id = null`).
- 메시지 soft delete 반영.

### 11-2. 경계 시나리오

- 최대 인원 초과 시 join 실패 처리.
- 네트워크 단절 후 재연결 시 중복 메시지 없이 복구.
- 토큰 만료 후 재인증/실패 처리.
- 동일 메시지 중복 클릭 전송 시 멱등 처리.

### 11-3. 관측 포인트

- 클라이언트 로그 키: `chatRoomId`, `messageId`, `requestId`, `errorCode`.
- 서버/DB 검증:
- `chat_room_users` 상태 전이(ACTIVE->LEFT/KICKED).
- `chat_messages` 삽입 순서/인덱스 활용.

## 12) 단계별 실행 계획 (구현 순서)

### Phase 0. 계약 확정

- 채팅 API 경로/응답 스키마/에러코드 확정.
- 게임 룸 ID와 채팅 룸 ID 관계 확정.
- 실시간 방식(WebSocket/SSE/폴링) 확정.

완료 기준:
- Swagger 또는 계약 문서 1건.
- Unity 쪽에서 필요한 필수 필드 목록 동결.

### Phase 1. 클라이언트 도메인 설계

- Chat 모델/에러 모델/상태 머신 확정.
- `RoomSessionContext`와 별개 `ChatSessionContext` 필요 여부 결정.

완료 기준:
- 모델 명세 문서 + 상태 전이 다이어그램 확정.

### Phase 2. 씬 연계 설계

- `03_NetworkCarTest` 진입 시점 Bootstrap 위치 결정.
- 채팅 실패가 주행/게임 진입에 영향 주지 않도록 분리.

완료 기준:
- 씬 진입 시퀀스/실패 UX 와이어 확정.

### Phase 3. 메시지 동기화/실시간

- 히스토리 로드, 신규 수신, 중복 제거 정책 확정.
- 폴링일 경우 interval/backoff 기준 확정.

완료 기준:
- 메시지 순서/중복/복구 정책 테스트 케이스 정의.

### Phase 4. QA 및 배포 점검

- 기능/경계/장애 시나리오 테스트.
- 로그/모니터링 키 점검.

완료 기준:
- 릴리즈 체크리스트 통과.

## 13) 웹 개발자 확인 필요 질문 (필수)

1. 게임 룸(`/api/rooms`)과 채팅 룸(`chat_rooms`)은 1:1 매핑인가? 매핑 키는 무엇인가?
2. 채팅 API endpoint/응답 JSON 샘플(성공/실패)을 받을 수 있는가?
3. 실시간 수신 방식은 무엇인가(WebSocket, SSE, Long Polling)?
4. 메시지 삭제 정책은 soft delete만 허용인가, 누가 삭제 가능한가?
5. `LEFT` 사용자가 재입장할 때 row 업데이트인가 신규 row 생성인가?
6. `max_user_count` 초과 시 에러코드/메시지 표준은 무엇인가?
7. `created_at`/`updated_at`의 타임존 기준은 UTC인가 서버 로컬타임인가?
8. 파일 메시지 업로드 절차는 무엇인가(직접 업로드 vs presigned URL)?

## 14) 결론

- 전달된 스키마 기준으로 채팅의 핵심 도메인(방/멤버/메시지/로그)은 충분히 구성되어 있다.
- 현재 Unity 구조에서 가장 중요한 선결 과제는 "게임 룸과 채팅 룸 식별자 관계"와 "실시간 수신 방식" 확정이다.
- 위 두 항목이 고정되면, Lobby -> `03_NetworkCarTest` 전환 흐름에 채팅을 안전하게 붙일 수 있다.

## 15) 현재까지 구현 체크 (2026-03-25)

### 15-1. 완료

- [x] `ChatRoomManager` 채팅방 생성 API 연동 (`POST /api/chat/rooms`)
- [x] `ChatRoomManager` 채팅방 목록 조회 API 연동 (`GET /api/chat/rooms`)
- [x] `ChatRoomManager` 입장 요청 생성 API 연동 (`POST /api/chat/rooms/{roomId}/join-request`)
- [x] `ChatRoomManager` 입장 요청 목록 조회 API 연동 (`GET /api/chat/rooms/{roomId}/join-requests`)
- [x] `ChatRoomManager` 호스트 수락/거절 API 연동  
  (`POST /api/chat/join-requests/{requestId}/decision`, `Authorization: Bearer <token>`, body: `decision`, `reviewComment`)
- [x] `ChatRoomManager` 클라이언트 본인 입장요청 상태 조회 API 연동  
  (`GET /api/chat/my/join-request/{requestId}`, `Authorization: Bearer <token>`)
- [x] `HostJoinRequestMonitorGUI` 폴링 기반 입장 요청 모니터링 UI 구현
- [x] `HostJoinRequestMonitorGUI`에서 요청별 수락/거절 버튼 및 결과 상태 반영
- [x] `But_RoomList`에서 입장요청 승인 대기 폴링 후 `APPROVED` 시 씬 전환 처리
- [x] `03_NetworkCarTest` 진입 시 Host 모니터 GUI 자동 부트스트랩

### 15-2. 미완료

- [ ] 메시지 API 연동 (`GET/POST/DELETE /messages`)
- [ ] 실시간 수신(WebSocket/SSE) 또는 폴링 표준화 확정
- [ ] `ChatSessionContext` 도입 여부 및 게임 룸 ID/채팅 룸 ID 매핑 계약 확정
- [ ] 채팅 전용 QA 시나리오 실행 및 릴리즈 체크리스트 완료
