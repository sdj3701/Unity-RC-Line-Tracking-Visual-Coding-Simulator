# BlockCode Network Logic Document

## 1. 문서 목적
이 문서는 현재 프로젝트 코드 기준으로 HTTP API가 어떤 기능을 수행하는지, 어떤 사용자 행동에서 어떤 API가 호출되는지, 그리고 결과가 런타임 동작(블록 실행/네트워크카 제어)으로 어떻게 이어지는지를 정리한다.

작성 기준 코드:
- `Assets/Scripts/**`
- `Assets/BlocksEngine2/Scripts/**`

제외 범위:
- `Assets/Scripts/Server/Network/*` 소켓 계층(TCP/세션)은 HTTP API가 아니므로 본 문서 범위에서 제외

---

## 2. 네트워크 API 도메인 요약
현재 API 연동은 아래 6개 도메인으로 구성된다.

1. 인증(Auth)
- ID/PW 로그인
- Access Token 검증

2. 로비 방 생성/상태(Lobby Room)
- 방 생성
- 프로비저닝 상태 폴링

3. 채팅방/참가요청/블록공유(Chat Room)
- 채팅방 생성/목록
- 참가 요청/승인/상태 조회
- 블록 공유 목록/업로드/상세 조회
- 공유 블록을 내 레벨로 저장

4. 사용자 레벨 저장소(User Level)
- BlocksEngine2 XML/JSON 원격 저장/로드/목록/삭제

5. Miro 미로 원격 저장소
- 미로 저장/최신 로드/헬스체크

6. 저장 결과 검증 디버그 API
- `seq` 기반 `user-level` 상세 조회(호스트 코드 매핑/검증에 사용)

---

## 3. URL/인증/요청 공통 규칙

### 3.1 Base URL 결정 방식
1. 인증(`AuthManager`)
- `baseUrlOverride` -> `AppApiConfig.CurrentBaseUrl` -> fallback `http://ioteacher.com`
- 라우트는 `ApiRoutes` 상수 사용 (`/api/auth/login`, `/api/users/me-by-token`)

2. 채팅/로비(`ChatRoomManager`, `LobbyRoomFlow`)
- 인스펙터 기본값이 이미 완전 URL로 들어있음
- 기본값 대부분 `http://ioteacher.com/...`

3. BlocksEngine2 저장소(`DatabaseStorageProvider`)
- 기본 `http://ioteacher.com` + 경로(`/api/user-level`, `/api/user-level/me`)
- `BE2_CodeStorageManager` 인스펙터 값으로 변경 가능

4. Miro 저장소(`MiroMazeRemoteRepository`)
- 기본 `http://localhost:5000` + 엔드포인트(`/api/miro/mazes`, `/api/miro/mazes/latest`, `/health`)

### 3.2 인증 헤더(Bearer)
- 인증 필요 API는 `Authorization: Bearer <token>` 사용
- 토큰 소스 우선순위: UI override 입력값 -> `AuthManager.Instance.GetAccessToken()`

### 3.3 동시성/취소
- `ChatRoomManager`와 `LobbyRoomFlow`는 `IsBusy`로 단일 요청 직렬 처리
- 요청 중 `CancelCurrentRequest()` 호출 시 `CancellationToken` + `UnityWebRequest.Abort()`로 중단

### 3.4 응답 파싱 전략
- 백엔드 응답 스키마 변화 대응을 위해 snake_case/camelCase/중첩 `data`를 폭넓게 허용
- 성공 판단은 `HTTP 2xx` + 필요 시 `success/isSuccess` 플래그를 함께 확인
- 다수 API에서 `success:false` 문자열 플래그를 추가 탐지하여 실패 처리

---

## 4. API 엔드포인트 상세

## 4.1 인증(Auth)

| Method | Endpoint | 주요 호출 메서드 | 인증 | 요청/응답 핵심 |
|---|---|---|---|---|
| POST | `/api/auth/login` | `AuthApiClient.LoginWithIdPasswordAsync` | 없음 | 요청: `{ userId, password }` / 응답: `accessToken|token|access_token` 및 optional `refreshToken` |
| GET | `/api/users/me-by-token` | `AuthManager.ValidateTokenWithServer` | Bearer 필수 | 토큰 유효성 및 사용자 정보 확인 |

기능 포인트:
- 로그인 응답에서 토큰 키 이름이 달라도 추출 가능하도록 다중 키 파싱
- 로그인 성공 후에도 반드시 토큰 검증 API를 한 번 더 호출해 최종 인증 상태 확정

## 4.2 로비 방 생성/상태(Lobby Room)

| Method | Endpoint | 주요 호출 메서드 | 인증 | 요청/응답 핵심 |
|---|---|---|---|---|
| POST | `/api/rooms` | `LobbyRoomService.CreateRoomAsync` | 선택(Bearer) | 요청: `{ roomName, hostUserId }`, 헤더 `Idempotency-Key` 포함 |
| GET | `/api/room-jobs/{jobId}` | `LobbyRoomService.GetRoomStatusAsync` | 선택(Bearer) | job 기반 프로비저닝 상태 조회 |
| GET | `/api/rooms/{roomId}/status?jobId={jobId}` | `LobbyRoomService.GetRoomStatusAsync` | 선택(Bearer) | room 기반 상태 조회(또는 fallback) |

기능 포인트:
- 상태 문자열(`READY/ACTIVE/COMPLETED`, `PROVISIONING/CREATING/PENDING`, `FAILED/ERROR`)을 내부 enum으로 통일
- `LobbyRoomFlow`가 timeout/interval 기반 폴링으로 Ready까지 추적

## 4.3 채팅방/참가요청/블록공유(Chat Room)

| Method | Endpoint | 주요 호출 메서드 | 인증 | 요청/응답 핵심 |
|---|---|---|---|---|
| POST | `/api/chat/rooms` | `ChatRoomManager.CreateRoomAsync` | 선택(Bearer) | 요청: `title`, `roomName`, `maxUserCount/max_user_count`, `userId/hostUserId` |
| GET | `/api/chat/rooms` | `ChatRoomManager.FetchRoomListAsync` | 선택(Bearer) | 채팅방 목록 조회 |
| POST | `/api/chat/rooms/{roomId}/join-request` | `ChatRoomManager.RequestJoinRequestAsync` | 선택(Bearer) | 참가 요청 생성 |
| GET | `/api/chat/rooms/{roomId}/join-requests` | `ChatRoomManager.FetchJoinRequestsAsync` | 선택(Bearer) | 방 참가요청 목록(주로 host) |
| POST | `/api/chat/join-requests/{requestId}/decision` | `ChatRoomManager.DecideJoinRequestAsync` | Bearer 필수 | 요청: `{ decision: APPROVED|REJECTED, reviewComment }` |
| GET | `/api/chat/my/join-request/{requestId}` | `ChatRoomManager.FetchMyJoinRequestStatusAsync` | Bearer 필수 | 내 참가요청 상태 폴링 |
| GET | `/api/chat/rooms/{roomId}/block-shares?page={p}&size={s}` | `ChatRoomManager.FetchBlockSharesAsync` | Bearer 필수 | 블록 공유 목록 |
| POST | `/api/chat/rooms/{roomId}/block-shares` | `ChatRoomManager.UploadBlockShareAsync` | Bearer 필수 | 요청: `{ userLevelSeq, message }` |
| GET | `/api/chat/rooms/{roomId}/block-shares/{shareId}` | `ChatRoomManager.FetchBlockShareDetailAsync` | Bearer 필수 | 공유 상세(XML/JSON 포함 가능) |
| POST | `/api/chat/block-shares/{shareId}/save-to-my-level` | `ChatRoomManager.SaveBlockShareToMyLevelAsync` | Bearer 필수 | body `{}` 전송, 응답에서 저장된 `seq` 추출 |

기능 포인트:
- endpoint template에 `roomId/shareId/requestId`를 URL-escape 후 치환
- 목록/상세 응답이 `data/items/list/content` 등 어떤 래퍼로 와도 파싱 시도
- 블록 상세 응답에서 JSON/XML payload 후보 키를 다중 탐색

## 4.4 사용자 레벨 저장소(User Level, BlocksEngine2)

| Method | Endpoint | 주요 호출 메서드 | 인증 | 요청/응답 핵심 |
|---|---|---|---|---|
| GET | `/api/user-level/me` | `DatabaseStorageProvider.GetMyEntriesAsync` | Bearer 필수 | 내 레벨 목록/메타 조회 |
| GET | `/api/user-level/{seq}` | `DatabaseStorageProvider.GetEntryBySeqAsync` | Bearer 필수 | 단건 상세(XML/JSON) |
| POST | `/api/user-level` | `DatabaseStorageProvider.SaveOrUpdateRemoteAsync` | Bearer 필수 | multipart 저장(신규) |
| PATCH | `/api/user-level/{seq}` | `DatabaseStorageProvider.SaveOrUpdateRemoteAsync` | Bearer 필수 | multipart 갱신 1차 시도 |
| PUT | `/api/user-level/{seq}` | `DatabaseStorageProvider.SaveOrUpdateRemoteAsync` | Bearer 필수 | multipart 갱신 2차 fallback |
| DELETE | `/api/user-level/{seq}` | `DatabaseStorageProvider.DeleteCodeAsync` | Bearer 필수 | 레벨 삭제 |

multipart 필드:
- `level`, `xml`, `json`, `xmlLongText`, `jsonLongText`

기능 포인트:
- 저장은 먼저 `level(fileName)`로 기존 항목 검색 후 update/create 분기
- DB 응답 파싱은 정규식 기반으로 다양한 JSON 형식 대응
- `TryGetAuthInfo`에서 `userId + accessToken` 둘 다 있어야 원격 동작
- save/load/list/exists의 local fallback은 의도적으로 비활성화(원격 우선 강제), delete만 fallback provider 사용 가능

## 4.5 Miro 미로 원격 API

| Method | Endpoint | 주요 호출 메서드 | 인증 | 요청/응답 핵심 |
|---|---|---|---|---|
| POST | `/api/miro/mazes` | `MiroMazeRemoteRepository.SaveMaze` | 선택(Bearer) | 요청: `{ clientSavedAtUtc, mazeData }` |
| GET | `/api/miro/mazes/latest` | `MiroMazeRemoteRepository.TryLoadLatest` | 선택(Bearer) | 최신 미로 로드 |
| GET | `/health` | `MiroMazeRemoteRepository.TestConnection` | 선택(Bearer) | 서버 연결 점검 |

기능 포인트:
- `MiroTestSceneController`에서 remote 실패 시 local fallback 옵션 제공

## 4.6 디버그/검증 API (호스트 런타임 매핑용)

| Method | Endpoint | 주요 호출 메서드 | 인증 | 요청/응답 핵심 |
|---|---|---|---|---|
| GET | `/api/user-level/{seq}` | `ChatUserLevelDebugApi.FetchBySeqAsync` | Bearer 필수 | 저장 직후 XML/JSON 검증 및 런타임 코드 해석 |

사용 위치:
- `HostBlockShareSaveToMyLevelButton.DebugLogSavedBlockCodeDataAsync`
- `HostBlockCodeResolver.ResolveBySavedSeqAsync`

---

## 5. 사용자 행동 기준 흐름

## 5.1 사용자가 ID/PW로 로그인 버튼 클릭
1. `AuthManager.LoginWithCredentialsAsync(userId, password)` 호출
2. `AuthApiClient`가 `POST /api/auth/login` 수행
3. 응답에서 access token 추출
4. `AuthenticateWithTokenAsync`가 `GET /api/users/me-by-token`으로 토큰 검증
5. 성공 시 `AuthSessionStore.Save`로 토큰 저장 후 로비 씬 전환

실패 시:
- 네트워크/서버 코드 매핑 후 `OnLoginFailed` 이벤트 발생

## 5.2 사용자가 로비에서 방 생성

경로 A: ChatRoom 기준(현재 UI 기본)
1. `LobbyUIController.OnCreateRoomButtonClicked`
2. `ChatRoomManager.CreateRoom` 호출
3. `POST /api/chat/rooms`
4. 성공 이벤트 `OnCreateSucceeded`
5. `LobbyUIController.HandleChatRoomCreateSucceeded`에서 `RoomSessionContext.Set` 후 `03_NetworkCarTest` 씬 이동

경로 B: Legacy LobbyRoom 기준
1. `LobbyRoomFlow.CreateRoom`
2. `POST /api/rooms`
3. 상태가 provisioning이면 `GET /api/room-jobs/{jobId}` 또는 `GET /api/rooms/{roomId}/status?...` 폴링
4. Ready 시 `OnRoomReady` 이벤트로 씬 이동

## 5.3 클라이언트가 방 목록에서 방 선택 후 참가
1. `But_RoomList.OnClickFetchRoomList` -> `GET /api/chat/rooms`
2. 방 선택 후 Confirm -> `POST /api/chat/rooms/{roomId}/join-request`
3. `requestId`를 받은 뒤 주기적으로 `GET /api/chat/my/join-request/{requestId}`
4. 상태가 `APPROVED/ACCEPTED`가 되면 `RoomSessionContext.Set` 후 네트워크 씬 이동

## 5.4 호스트가 참가요청 승인
1. `HostJoinRequestMonitorGUI`가 `GET /api/chat/rooms/{roomId}/join-requests` 폴링
2. Host가 Accept 클릭 -> `POST /api/chat/join-requests/{requestId}/decision`
3. `HostNetworkCarCoordinator`가 승인 이벤트를 받아 참가자 slot/car 생성

## 5.5 클라이언트가 블록 코드 공유 업로드
1. `ClientBlockShareListPanel`에서 자신의 코드 항목 선택
2. 선택 항목의 `userLevelSeq`를 `ClientBlockShareUploadButton`에 전달
3. 업로드 버튼 클릭 -> `POST /api/chat/rooms/{roomId}/block-shares` with `{ userLevelSeq, message }`
4. Host 측 패널이 공유 목록/상세 API로 해당 공유를 확인 가능

## 5.6 호스트가 공유 블록을 “내 레벨 저장” 후 바로 실행 매핑
1. `HostBlockShareSaveToMyLevelButton`에서 share 선택 후 저장
2. `POST /api/chat/block-shares/{shareId}/save-to-my-level`
3. 응답에서 `SavedUserLevelSeq` 확보
4. 검증 단계: `GET /api/user-level/{seq}`로 XML/JSON 확인
5. `HostNetworkCarCoordinator`가 저장 성공 이벤트 처리
6. share owner(userId) 해석(캐시/상세조회/응답 body)
7. `HostBlockCodeResolver.ResolveBySavedSeqAsync` -> 다시 `GET /api/user-level/{seq}`
8. JSON이 비어 있으면 XML->JSON 변환(`BE2XmlToRuntimeJson.ExportToString`)
9. `HostRuntimeBinder.TryApplyJson` -> `BlockCodeExecutor.LoadProgramFromJson`
10. `HostExecutionScheduler`가 슬롯 순회 실행에서 해당 차량 코드를 실제로 구동

핵심:
- 공유 저장 API는 “저장 완료(seq 반환)”까지 책임
- 실제 실행 연결은 후속 로직(조회/해석/바인딩)이 담당

## 5.7 사용자가 BlocksEngine2 저장/불러오기 수행
1. Save panel에서 DB 연결 상태에 따라 `SetRemoteStorageEnabled(true/false)`
2. 저장 시 XML/JSON 생성 후 `DatabaseStorageProvider` 경유
3. 내부적으로 `/api/user-level/me` 조회 -> 기존 level 있으면 PATCH/PUT, 없으면 POST
4. 불러오기/목록/존재확인은 기본적으로 `/api/user-level/me` 중심
5. 필요 시 `seq` 상세 `/api/user-level/{seq}` 추가 조회

## 5.8 사용자가 Miro 원격 저장/로드/연결테스트 수행
1. `MiroTestSceneController.SaveCurrent` -> `POST /api/miro/mazes`
2. `LoadLatest` -> `GET /api/miro/mazes/latest`
3. `TestRemoteDbConnection` -> `GET /health`
4. remote 실패 시 옵션에 따라 local fallback 수행

---

## 6. 실패/복구 정책 정리

1. 네트워크 실패
- 대부분 `ConnectionError/DataProcessingError`를 별도 처리
- 사용자 메시지/로그 메시지 분리

2. 인증 실패
- 인증 필요 API에서 토큰 없으면 호출 전 즉시 실패 처리(특히 join decision, my join status, block share 계열)

3. Busy 충돌
- `ChatRoomManager.IsBusy`일 때 새 요청은 스킵/지연
- 일부 UI(`HostBlockShareAutoRefreshPanel`)는 detail 요청을 큐잉 후 idle 시 재시도

4. 중복 이벤트
- `HostNetworkCarCoordinator`는 `shareId + savedSeq` 키로 dedupe하여 같은 저장 성공 이벤트 중복 반영 방지

5. 저장소 fallback
- `DatabaseStorageProvider`는 save/load/list/exists에서 local fallback 비활성화
- delete만 fallback provider 삭제를 허용

---

## 7. 현재 코드 기준 주의사항

1. 라우트 관리 일관성
- `ApiRoutes`/`ApiUrlResolver`는 인증 도메인에서 주로 사용
- 채팅/로비/디버그 API는 여전히 하드코딩 full URL 기본값 다수 존재

2. Base URL 이원화
- 인증은 환경별 설정(`AppApiConfig`)을 타지만
- 채팅/로비/debug는 `http://ioteacher.com` 기본값 중심
- Miro는 `http://localhost:5000` 기본값

3. 응답 포맷 유연성 의존
- 파서가 매우 유연한 대신, 백엔드가 필수 키를 지나치게 바꾸면 일부 경로에서 fallback 값으로만 동작할 수 있음

---

## 8. 문서 작성 시 바로 쓰기 좋은 핵심 문장

- “현재 구조는 `채팅 API로 협업 데이터(방/참가/공유)를 처리하고`, `user-level API로 실제 블록 XML/JSON 저장소를 관리`하며, 저장된 `seq`를 기반으로 호스트 런타임 실행까지 연결한다.”
- “사용자가 공유 블록을 저장하면 `save-to-my-level` API가 저장 대상 `seq`를 반환하고, 이후 호스트가 `user-level/{seq}`를 조회해 코드 payload를 해석/적용한다.”
- “API 호출 실패는 대부분 UI 이벤트(`...Failed`, `...Canceled`)로 전파되어 재시도 가능한 형태로 처리된다.”
