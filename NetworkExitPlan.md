# Network Exit Plan

작성일: 2026-05-06

## 목적

현재 방 참여와 RC카 동기화는 동작하므로, 다음 단계는 사용자가 방을 나갈 때 Photon Fusion 2 세션, 로컬 UI, RC카 런타임, 참가자 상태가 서로 어긋나지 않게 정리하는 것이다.

이 문서는 코드 변경 없이 작업 방향만 정리한다.

## 현재 구조 요약

- `FusionConnectionManager`
  - `NetworkRunner` 생성, Photon Lobby 접속, 세션 상태 플래그, `INetworkRunnerCallbacks` 수신을 담당한다.
  - 이미 `ShutdownAsync()`가 있으며 `runner.Shutdown(false, ShutdownReason.Ok, false)` 호출 후 runner 참조, 대기 중인 참가 요청, `FusionRoomSessionContext`를 정리한다.
- `FusionRoomService`
  - 방 생성/입장을 담당한다.
  - 이미 `IsInGameSession`이면 새 방 생성/입장을 막는다. 따라서 나가기 기능은 이 플래그를 확실히 해제해야 한다.
- `FusionNetworkBootstrap`
  - 네트워크 씬에서 기존 runner를 재사용하거나 fallback으로 시작한다.
  - 종료 시 `FusionConnectionManager.ShutdownAsync()`를 우선 사용한다.
- `HostNetworkCarCoordinator`
  - 호스트에서 Fusion 참가자와 RC카 슬롯/바인딩을 연결한다.
  - 현재 `OnPlayerLeft`에서는 `PlayerRef -> userId` 매핑만 제거하므로, 실제 RC카 despawn 또는 비활성화 정책을 추가로 정해야 한다.
- `NetworkRCCarSpawner`
  - RC카는 호스트/server에서만 `Runner.Spawn()`으로 생성된다.
  - 정리도 state authority가 있는 호스트/server에서 수행해야 한다.

## Photon Fusion 2 기준 원칙

- 로컬 사용자가 방을 나갈 때는 `NetworkRunner.Shutdown(...)`을 await 하는 흐름을 기준으로 한다. Fusion API에서 `Shutdown()`은 `Task`를 반환하므로, 씬 전환과 상태 정리는 이 작업 완료를 기준으로 순서화한다.
- `INetworkRunnerCallbacks.OnShutdown`은 runner 종료 후 호출되는 콜백이다. 단, 현재 `FusionConnectionManager.ShutdownAsync()`는 종료 전에 자기 콜백을 제거하므로, 자발적 나가기에서는 `finally` 정리 흐름이 사실상 source of truth가 된다.
- `INetworkRunnerCallbacks.OnPlayerLeft`는 다른 참가자가 연결을 끊었을 때 호스트와 남은 피어들이 받는 이벤트다. 원격 참가자 정리는 이 콜백을 기준으로 한다.
- `NetworkRunner.Disconnect(PlayerRef)`는 server에서 특정 player를 끊을 때 쓰는 API다. 일반 클라이언트가 자기 자신을 방에서 나가게 하는 기본 API로 쓰지 않는다.
- 현재 프로젝트는 `GameMode.Host` / `GameMode.Client` 흐름이다. 호스트가 나가면 일반적으로 세션도 종료되고 클라이언트는 서버 연결이 끊긴다. 호스트가 나가도 방을 유지하려면 별도 Host Migration 설계가 필요하며, 이 문서의 1차 범위에서는 제외한다.
- Fusion 2의 연결 유실/자동 재접속 기능은 네트워크 장애 대응용이다. 사용자가 명시적으로 “나가기”를 누른 경우에는 자동 재접속을 시도하지 않아야 한다.

## 목표 동작

### 1. 클라이언트가 방 나가기

1. 나가기 버튼을 누르면 즉시 중복 입력을 막는다.
2. 로컬 실행 중인 RC카/블록 실행/UI 인터랙션을 먼저 중지한다.
3. `FusionConnectionManager`의 단일 나가기 API를 통해 runner shutdown을 요청한다.
4. shutdown 완료 후 로컬 세션 컨텍스트를 비우고 방/로비 화면으로 이동한다.
5. 필요하면 새 runner로 Photon Lobby에 다시 연결해서 방 목록을 갱신한다.
6. 호스트는 `OnPlayerLeft`를 통해 나간 유저의 슬롯/차량/실행 상태를 정리한다.

### 2. 호스트가 방 나가기

1. 호스트가 나가면 현재 구조에서는 방 종료로 취급한다.
2. 호스트는 실행 스케줄러를 멈추고, 참가 요청 대기열과 RC카 상태를 정리한 뒤 runner shutdown을 수행한다.
3. 클라이언트는 `OnDisconnectedFromServer` 또는 `OnShutdown`을 통해 “호스트가 방을 종료함” 상태로 로비/방 목록 화면으로 돌아간다.
4. API 서버에 별도 방 상태가 있다면, 호스트는 Photon shutdown 전후로 방 종료/나가기 API를 호출해야 한다. 실패하더라도 Fusion runner 정리는 반드시 진행한다.

### 3. 비정상 연결 끊김

1. 사용자 의도 나가기와 네트워크 끊김은 상태를 분리한다.
2. `OnDisconnectedFromServer`는 경고 메시지와 로컬 정리 진입점으로 사용한다.
3. Fusion 2.0.5+ / 2.1+의 `NetworkRunner.CloudConnectionLost` 사용 여부를 별도 판단한다.
4. 자동 재접속을 사용할 경우, 재접속 중 UI와 명시적 나가기 UI가 섞이지 않도록 `Leaving`, `Reconnecting` 상태를 분리한다.

## 제안 작업 단위

### Step 1. 나가기 상태 모델 정리

`FusionConnectionManager`에 다음 개념을 둔다.

- `IsLeavingRoom`
  - 나가기 중 중복 호출 방지.
- `LastExitReason`
  - `UserLeave`, `HostClosed`, `Disconnected`, `ShutdownFailed` 등 로컬 앱 기준 이유.
- 단일 public 진입점
  - 예: `LeaveRoomAsync(...)`
  - 내부에서 기존 `ShutdownAsync()`를 호출하거나 확장한다.

중요한 점은 UI, Bootstrap, 다른 매니저가 각자 `runner.Shutdown()`을 직접 호출하지 않도록 하는 것이다. 현재 구조에서는 `FusionConnectionManager`가 runner 생명주기의 중심이므로 여기에 모으는 편이 가장 안전하다.

### Step 2. 로컬 pre-cleanup 추가

runner shutdown 전에 로컬에서 먼저 멈춰야 할 것들을 정리한다.

- 나가기 버튼/방 UI 비활성화
- 실행 중인 `HostExecutionScheduler` 정지
- 로컬 블록 실행, 입력 처리, RC카 조작 루프 정지
- 진행 중인 업로드/저장 UI가 있다면 취소 또는 완료 대기 정책 결정
- 참가 요청 승인 UI, 대기열 패널 닫기

pre-cleanup은 네트워크 객체 삭제가 아니라 “더 이상 새 작업을 시작하지 않게 막는 단계”로 제한한다.

### Step 3. Fusion shutdown 수행

현재 코드 흐름과 맞추면 다음 정책이 적절하다.

- owned runner를 직접 정리하고 있으므로 `Shutdown(destroyGameObject: false, shutdownReason: ShutdownReason.Ok, forceShutdownProcedure: false)` 형태를 유지한다.
- shutdown이 성공해도 실패해도 `finally`에서 runner 참조와 세션 컨텍스트를 정리한다.
- 자발적 나가기에서는 `ShutdownReason.Ok`를 사용한다.
- Host Migration을 구현하는 경우에만 `ShutdownReason.HostMigration`을 별도 흐름으로 사용한다.

주의할 점:

- `Shutdown()` 완료 전에 `SceneManager.LoadScene(...)`을 먼저 실행하지 않는다.
- `NetworkRunner`가 shutdown된 뒤에는 재사용하지 않는다. 다음 방 입장은 `EnsureRunner()`가 새 runner를 만들도록 한다.
- `ShutdownAsync()`와 `OnShutdown()` 양쪽에서 같은 정리를 반복할 수 있으므로 cleanup은 idempotent하게 만든다.

### Step 4. 원격 참가자 정리

클라이언트가 나갔을 때 호스트에서 `HostNetworkCarCoordinator.OnPlayerLeft`에 다음 정책을 추가한다.

- `PlayerRef`로 `userId`를 찾는다.
- 해당 유저의 실행을 중지한다.
- 해당 유저의 RC카를 어떻게 처리할지 결정한다.
  - 권장 1차 정책: host/server가 해당 `NetworkObject`를 `Runner.Despawn()`해서 모든 클라이언트에서 제거한다.
  - 대안 정책: 재입장 복구를 위해 차량을 잠시 inactive/ghost 상태로 보존한다. 이 경우 만료 시간과 재연결 식별자가 필요하다.
- 슬롯/바인딩/상태 패널에서 해당 유저를 제거하거나 “나감” 상태로 표시한다.
- 전체 실행 스케줄러가 슬롯 순회 중이면 현재 슬롯 index 보정이 필요하다.

현재 프로젝트는 학습/RC카 시뮬레이터 성격이 강하므로, 1차 구현은 “나간 유저의 차는 제거하고 슬롯도 정리”가 단순하고 안전하다.

### Step 5. 호스트 종료 전파

현재 Host Mode에서는 호스트가 서버 역할이다. 호스트가 나가면 클라이언트가 계속 같은 세션에 남는 동작을 기대하면 안 된다.

1차 구현 정책:

- 호스트 나가기 = 방 종료
- 클라이언트는 연결 종료 콜백을 받으면 로컬 상태를 정리하고 방 목록으로 이동
- UI 메시지는 “호스트가 방을 종료했습니다”로 통일

향후 확장:

- Host Migration을 도입하려면 Photon Host Migration 설정, snapshot push, `OnHostMigration`, 새 runner 시작, state 복구 순서가 필요하다.
- RC카, 슬롯, 블록 실행 상태를 migration snapshot 이후 재구성할 수 있어야 하므로 현재 1차 나가기 기능과 별도 프로젝트로 분리하는 것이 좋다.

### Step 6. 로비 복귀

방을 나간 뒤 방 목록을 보여줘야 한다면 runner shutdown 후 새 lobby 연결이 필요하다.

권장 흐름:

1. game session runner shutdown
2. `FusionRoomSessionContext.Clear()`
3. 네트워크 씬에서 로비/방 목록 씬으로 이동
4. `ConnectToPhotonLobbyAsync()`로 lobby runner 준비
5. `OnSessionListUpdated`로 방 목록 갱신

게임 세션 runner를 lobby runner로 계속 재사용하려고 하지 않는 편이 안전하다. 현재 `EnsureRunner()`와 `CleanupRunner()` 구조도 shutdown 후 새 runner 생성에 맞춰져 있다.

## 상태 전이

```text
InRoom
  -> LeaveRequested
  -> LocalPreCleanup
  -> FusionShutdownPending
  -> LocalContextCleared
  -> LobbySceneLoading
  -> LobbyReady
```

실패 시:

```text
FusionShutdownPending
  -> ShutdownFailed
  -> ForceLocalCleanup
  -> LobbySceneLoading
```

네트워크 장애 시:

```text
InRoom
  -> Disconnected
  -> Reconnecting | ForceLocalCleanup
```

## UI 정책

- 나가기 버튼은 한 번 누르면 비활성화한다.
- 나가기 진행 중 방 생성/입장/코드 공유/실행 버튼을 잠근다.
- 성공 시에는 별도 성공 팝업 없이 로비/방 목록으로 이동한다.
- 실패 시에도 로컬 runner 참조가 이미 불안정할 수 있으므로 “로컬 정리 후 로비 이동”을 우선한다.
- 호스트 종료로 클라이언트가 튕긴 경우는 에러가 아니라 안내 메시지로 처리한다.

## 구현 체크리스트

- `FusionConnectionManager`
  - 나가기 중복 방지 플래그 추가
  - 기존 `ShutdownAsync()`를 기반으로 명시적 방 나가기 API 정리
  - `MarkGameSessionEnded()`가 lobby/session/game state를 확실히 초기화하는지 확인
  - shutdown 실패 시에도 local cleanup이 보장되는지 확인
- `FusionRoomService`
  - 방 생성/입장 전에 `IsLeavingRoom`이면 막기
  - 방 나가기 후 재입장 시 새 runner가 만들어지는지 확인
- `FusionNetworkBootstrap`
  - scene disable로 의도치 않은 shutdown이 발생하지 않도록 `_shutdownOnDisable` 기본 정책 유지
  - 나가기 버튼/씬 이동은 `FusionConnectionManager` 단일 흐름을 호출
- `HostNetworkCarCoordinator`
  - `OnPlayerLeft`에서 userId, slot, car binding 정리
  - host/server에서만 network despawn 수행
  - 실행 스케줄러의 현재 슬롯 보정
- `HostCarBindingStore` / `HostParticipantSlotRegistry`
  - 특정 userId 제거 API 필요 여부 확인
  - 제거 후 slot index를 유지할지 압축할지 결정
- UI
  - 방 나가기 버튼 연결
  - 진행 중/실패/호스트 종료 메시지 연결
- API 서버 연동
  - 별도 방 참가/퇴장 API가 있다면 Photon shutdown과 순서 정의
  - API 실패가 Fusion cleanup을 막지 않게 처리

## 테스트 시나리오

1. 클라이언트 1명이 나가기
   - 호스트와 다른 클라이언트는 세션 유지
   - 나간 클라이언트의 RC카가 제거되거나 “나감” 상태로 바뀜
   - 방 인원 수와 roster가 갱신됨
   - 나간 클라이언트가 다시 같은 방에 입장 가능
2. 호스트가 나가기
   - 모든 클라이언트가 안전하게 로비/방 목록으로 복귀
   - 클라이언트 콘솔에 runner null 참조, shutdown 중복 예외가 없음
   - API 방 상태가 있다면 종료/퇴장 상태가 맞음
3. 나가기 버튼 연타
   - shutdown은 한 번만 실행
   - 씬 이동도 한 번만 실행
4. 나가기 중 씬 전환
   - shutdown 완료 전 scene load가 먼저 발생하지 않음
   - `DontDestroyOnLoad` runner가 남지 않음
5. 나간 뒤 새 방 생성/입장
   - 이전 runner, 이전 session context, 이전 player count가 남지 않음
6. 비정상 연결 끊김
   - 자발적 나가기와 다른 메시지 표시
   - 자동 재접속을 켠 경우 재접속 UI와 나가기 UI가 충돌하지 않음

## 1차 구현 범위

1차 작업에서는 다음만 구현한다.

- 명시적 “방 나가기” 버튼
- 클라이언트 자발적 나가기
- 호스트 자발적 방 종료
- 원격 참가자 `OnPlayerLeft` 정리
- runner/context/UI cleanup
- 나간 뒤 로비/방 목록 복귀

다음은 1차 범위에서 제외한다.

- Host Migration
- 네트워크 장애 자동 재접속 완성
- 나간 플레이어 RC카 장기 보존 및 재입장 복구
- API 서버 방 상태 복구 배치 작업

## 테스트 후 추가 수정 계획

작성일: 2026-05-06

03_NetworkCarTest에서 1차 나가기 기능을 테스트한 결과, Photon runner 정리만으로는 충분하지 않은 문제가 확인되었다.

추가로 해결해야 할 문제는 다음 두 가지다.

1. 사용자가 방을 나가면 참여자 DB 리스트에서도 해당 사용자를 삭제해야 한다.
2. 사용자가 방을 나간 뒤 같은 방에 다시 참여하는 플로우가 정상 동작해야 한다.

### 문제 1. 참여자 DB 리스트 삭제

현재 나가기 구현은 주로 로컬/Fusion 상태를 정리한다.

- `FusionConnectionManager.LeaveRoomAsync()`로 runner shutdown
- `FusionRoomSessionContext.Clear()`
- 호스트의 `OnPlayerLeft`에서 RC카 despawn, 슬롯/바인딩 제거
- 로비 씬 복귀 및 Photon Lobby 재접속

하지만 서버 DB에 저장된 참여자 또는 승인된 입장 요청 row는 별도로 삭제하지 않는다. 따라서 로비/방 참여자 목록이 DB 기준으로 표시되는 경우, 실제로 나간 유저가 계속 참여자로 남아 보일 수 있다. 이 상태는 재참여 실패의 원인도 될 수 있다.

#### 필요한 API

클라이언트에서 호출할 수 있는 “방 나가기” API가 필요하다. 현재 `ChatRoomManager`에는 다음 계열 API만 있다.

- 방 생성/목록 조회
- 입장 요청 생성
- 입장 요청 목록 조회
- 입장 요청 승인/거절
- 내 입장 요청 상태 조회
- 블록 공유 관련 API

나가기 또는 참여자 삭제 전용 API는 아직 코드에서 확인되지 않았다.

권장 API 형태:

```text
DELETE /api/chat/rooms/{roomId}/participants/me
```

대안 API 형태:

```text
DELETE /api/chat/rooms/{roomId}/participants/{userId}
DELETE /api/chat/rooms/{roomId}/join-requests/{requestId}
POST   /api/chat/rooms/{roomId}/leave
```

권장 우선순위는 `participants/me`다. 클라이언트가 `userId`를 직접 URL에 넣지 않아도 되고, 서버가 Authorization token으로 현재 사용자를 판별할 수 있기 때문이다.

#### 클라이언트 작업 방향

`ChatRoomManager`에 방 나가기 API를 추가한다.

- endpoint template 추가
  - 예: `_leaveRoomEndpointTemplate = "http://ioteacher.com/api/chat/rooms/{roomId}/participants/me"`
- public method 추가
  - 예: `LeaveRoom(string roomIdRaw, string accessTokenOverride = null)`
- async method 추가
  - 예: `LeaveRoomAsync(...)`
- 이벤트 추가
  - `OnLeaveRoomStarted`
  - `OnLeaveRoomSucceeded`
  - `OnLeaveRoomFailed`
  - `OnLeaveRoomCanceled`
- `ApiRoutes`에도 중앙 상수/빌더 추가
  - 예: `ChatRoomLeave(roomId)`

`NetworkRoomExitController` 또는 별도 `NetworkRoomExitFlow`는 나가기 시작 시 다음 정보를 먼저 캡처해야 한다.

- `apiRoomId`
- `photonSessionName`
- `currentUserId`
- 가능하면 `joinRequestId`
- 현재 유저가 host인지 여부

이 정보를 캡처하기 전에 `FusionRoomSessionContext.Clear()` 또는 `RoomSessionContext.Clear()`를 먼저 호출하면 API 나가기 요청에 필요한 room id를 잃을 수 있다.

권장 순서:

```text
LeaveRequested
  -> CaptureExitContext(apiRoomId, sessionName, userId, isHost)
  -> DisableLeaveButtonAndRoomActions
  -> StopLocalExecution
  -> CallChatLeaveApiWithShortTimeout
  -> FusionRunnerShutdown
  -> ClearFusionRoomSessionContext
  -> ClearRoomSessionContext
  -> LoadLobbyScene
  -> ConnectPhotonLobby
  -> RefreshRoomList
```

API 실패 시 정책:

- API 실패가 Fusion shutdown을 막으면 안 된다.
- API 실패 시에도 runner shutdown과 로컬 cleanup은 반드시 진행한다.
- 실패한 leave 요청은 로그/상태 메시지로 남기고, 가능하면 로비 복귀 후 1회 재시도한다.
- 서버가 이미 삭제된 참여자에 대해 404/409를 반환하면 성공과 동일하게 취급할지 서버 계약을 정한다.

#### 호스트 나가기 정책

호스트가 나가는 경우는 일반 참여자 삭제와 다르게 처리해야 한다.

1차 정책:

- 호스트 나가기 = 방 종료
- 서버 DB에서는 방 상태를 `closed` 또는 `ended`로 바꾼다.
- 해당 방의 참여자/대기 요청은 모두 종료 상태로 정리한다.

필요 API 예:

```text
POST /api/chat/rooms/{roomId}/close
DELETE /api/chat/rooms/{roomId}
```

현재 프로젝트에서는 Host Migration을 1차 범위에서 제외했으므로, 호스트가 나간 방에 클라이언트가 계속 남는 동작을 목표로 잡지 않는다.

### 문제 2. 나간 뒤 방 재참여 실패

재참여 실패는 하나의 원인보다 여러 상태가 동시에 남아서 발생할 가능성이 높다.

확인해야 할 상태는 다음이다.

- Photon runner가 완전히 shutdown되고 새 runner가 만들어지는지
- `FusionConnectionManager.IsLeavingRoom`이 false로 돌아오는지
- `FusionConnectionManager.IsInGameSession`이 false인지
- `FusionRoomSessionContext.Current`가 null인지
- `RoomSessionContext.CurrentRoom`도 null로 정리되는지
- `But_RoomList`의 `_pendingJoinRequestId`, approval polling 상태가 남아 있지 않은지
- 서버 DB에 기존 approved/pending join request가 남아서 새 입장 요청을 막지 않는지
- 로비 복귀 후 Photon Lobby 접속이 완료되기 전에 방 입장 버튼이 활성화되지 않는지

#### 가장 가능성 높은 원인

현재 문서 기준 나가기 흐름은 `FusionRoomSessionContext.Clear()`를 포함하지만, `RoomSessionContext.Clear()`까지 명시되어 있지 않다.

프로젝트의 방 식별은 `NetworkRoomIdentity.ResolveApiRoomId()`가 다음 순서로 찾는다.

1. override room id
2. `RoomSessionContext.CurrentRoom.ApiRoomId`
3. `FusionRoomSessionContext.Current.ApiRoomId`
4. `RoomSessionContext.CurrentRoom.RoomId`

즉, Fusion context만 지워도 `RoomSessionContext.CurrentRoom`이 남아 있으면 이전 방 정보가 계속 참조될 수 있다. 이 상태에서 로비 재입장, join request 생성, block share 조회 등이 이전 방 id를 사용할 수 있다.

또한 `But_RoomList`는 입장 요청 후 `_pendingJoinRequestId`를 저장하고 approval polling을 수행한다. 나갔다가 다시 입장할 때 이 값과 polling 상태가 정리되지 않으면 새 입장 요청 또는 승인 대기 플로우가 꼬일 수 있다.

#### 재참여 해결 방향

방 나가기 완료 시 로컬 room state를 모두 초기화한다.

필수 cleanup:

- `FusionRoomSessionContext.Clear()`
- `RoomSessionContext.Clear()`
- `FusionConnectionManager`의 session/lobby/game 플래그 초기화
- `FusionLobbyService` 방 목록 cache refresh
- `But_RoomList`의 pending join request 상태 초기화 API 추가
- `ChatRoomManager.CancelCurrentRequest()` 호출로 이전 API 요청 취소
- `NetworkRoomRosterPanel`, join request monitor의 cached list refresh 또는 clear

권장 public API:

```text
But_RoomList.ResetJoinFlowState()
NetworkRoomRosterPanel.ClearSnapshots()
HostJoinRequestMonitorUI.ClearPendingDecisionState()
```

이 API들은 씬을 다시 열 때 자동 초기화되더라도, `DontDestroyOnLoad` 객체나 static context가 남을 수 있으므로 명시적으로 제공하는 편이 안전하다.

#### 재참여 순서

로비 복귀 직후 바로 방 입장을 허용하지 않는다.

권장 상태 전이:

```text
RoomExitCompleted
  -> LobbySceneLoaded
  -> PhotonLobbyConnecting
  -> PhotonLobbyReady
  -> RoomListRefreshed
  -> JoinButtonsEnabled
```

입장 버튼 활성화 조건:

- `FusionConnectionManager.IsLeavingRoom == false`
- `FusionConnectionManager.IsInGameSession == false`
- `FusionConnectionManager.IsInSessionLobby == true`
- `FusionConnectionManager.Runner != null`
- `Runner.IsShutdown == false`
- 최신 방 목록 fetch 완료

#### 서버 DB 중복 정책

재참여가 DB에서 막히는 경우도 고려해야 한다.

예상 케이스:

- 같은 `roomId + userId`의 approved join request가 이미 있어서 새 요청이 conflict 처리됨
- 참여자 table에 기존 row가 남아서 “이미 참여 중”으로 판단됨
- pending request가 남아서 새 요청이 중복으로 막힘

서버 정책은 다음 중 하나로 정해야 한다.

권장:

- 나가기 API가 참여자 row와 기존 pending/approved request를 함께 정리한다.
- 같은 유저가 재참여 요청을 보내면 서버는 기존 closed/left 상태 row를 재사용하거나 새 row를 생성한다.
- active participant가 아닌 기존 기록 때문에 재참여를 막지 않는다.

대안:

- 클라이언트가 새 입장 요청 전 `my join request status`를 조회한다.
- 기존 요청이 `LEFT`, `CANCELED`, `REJECTED`, `EXPIRED`면 새 요청을 허용한다.
- 기존 요청이 `APPROVED`지만 참여자 row가 inactive면 재입장용으로 다시 Photon join만 수행한다.

### 후속 구현 체크리스트

- `ApiRoutes`
  - `ChatRoomLeave(roomId)` 또는 `ChatRoomParticipantMe(roomId)` 추가
  - 서버 실제 endpoint 확정 필요
- `ChatRoomManager`
  - Leave API 메서드/이벤트 추가
  - DELETE 또는 POST 방식 서버 계약 반영
  - 404/409 처리 정책 추가
- `NetworkRoomExitController`
  - 나가기 시작 전에 `ExitContext` 캡처
  - Fusion shutdown 전에 DB leave 요청 수행
  - API 실패와 Fusion cleanup 분리
  - 완료 시 `RoomSessionContext.Clear()` 추가
  - 로비 복귀 후 `ConnectToPhotonLobbyAsync()` 완료 전 join UI 비활성화 상태 유지
- `But_RoomList`
  - pending join request id/polling 상태 reset API 추가
  - 로비 진입 시 reset 호출
  - `IsLeavingRoom`, `IsInSessionLobby` 기준으로 입장 버튼 enable 조건 강화
- `NetworkRoomRosterPanel`
  - 나간 유저 DB 삭제 후 refresh
  - 방 나가기 완료 시 cached join request list clear
- 서버
  - 참여자 삭제/leave endpoint 구현 또는 기존 endpoint 확인
  - host leave 시 방 종료 정책 구현
  - 재참여 시 기존 left/closed record 처리 정책 확정

### 추가 테스트 시나리오

1. 클라이언트 나가기 후 DB 참여자 목록 확인
   - 나간 유저가 참여자 DB 리스트에서 삭제됨
   - 호스트 UI, 로비 UI, API 조회 결과가 동일함
2. 클라이언트 나가기 후 같은 방 재참여
   - 새 입장 요청 생성 가능
   - 호스트 승인 가능
   - Photon join 성공
   - RC카가 새로 spawn되고 이전 RC카가 남지 않음
3. 나가기 API 실패 상황
   - Fusion runner는 정상 shutdown
   - 로비 복귀는 진행
   - DB 삭제 실패 메시지 또는 재시도 로그가 남음
4. 호스트 나가기
   - 방 DB 상태가 closed/ended로 변경됨
   - 클라이언트는 로비로 복귀
   - 닫힌 방에 재참여 버튼이 뜨지 않음
5. 나가기 직후 빠른 재입장 클릭
   - LobbyReady 전에는 버튼 비활성화
   - LobbyReady 이후에는 정상 입장 가능
6. 같은 유저 반복 입장/나가기 3회
   - DB 참여자 row가 중복 생성되지 않음
   - Photon runner가 매번 새로 준비됨
   - static context가 이전 방 id를 들고 있지 않음

## 참고 문서

- Photon Fusion 2 `NetworkRunner` API: https://doc-api.photonengine.com/en/fusion/current/class_fusion_1_1_network_runner.html
- Photon Fusion 2 `INetworkRunnerCallbacks` API: https://doc-api.photonengine.com/en/fusion/current/interface_fusion_1_1_i_network_runner_callbacks.html
- Photon Fusion 2 PlayerRef / Player Object: https://doc.photonengine.com/fusion/current/manual/playerref
- Photon Fusion 2 Connection Lost & Quick Reconnect: https://doc.photonengine.com/fusion/current/manual/connection-and-matchmaking/lost-connection-handling
- Photon Fusion 2 Disconnect & Reconnect sample: https://doc.photonengine.com/fusion/current/technical-samples/fusion-disconnect-reconnect
