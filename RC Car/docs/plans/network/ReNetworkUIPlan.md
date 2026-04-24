# ReNetworkUIPlan

## 0. Status
- 작성일: 2026-04-24
- 대상 씬: `03_NetworkCarTest`
- 목적: 기존 `Host UI` / `Client UI` 분리 구조를 "공유 시뮬레이션 + 역할별 도구 패널" 구조로 재정리한다.
- 범위: UI 재배치, RC카/맵 동기화, 공용 업로드 재구성, JoinRequest 표시 재구성, 채팅, HostMigration, 방 나가기

## 1. 왜 새 계획이 필요한가
- 기존 [NetworkCarPlan](../network/NetworkCarPlan.md)은 Photon + API 하이브리드 방 구조 복구에 초점이 있었고, UI 통합은 범위가 아니었다.
- 기존 [NetworkCarPRD](../../documents/network/NetworkCarPRD.md)는 "Host 화면 기준 1차 목표" 문서라서 Client 실시간 시뮬레이션 반영, HostMigration, 방 나가기, 공용 채팅을 제외하거나 후순위로 뒀다.
- 현재 `03_NetworkCarTest`는 `NetworkUIManager`가 `Host UI` / `Client UI`를 역할별로 켜고 끄는 구조이며, 씬에는 `_commonUI`가 비어 있다.
- 새 요구사항은 "Host와 Client가 같은 방의 시뮬레이션을 함께 본다"가 기준이므로, 기존 Host-only 전제를 버리고 별도 UI/룸 수명주기 계획이 필요하다.

## 2. 이번 변경 요구사항
1. Host, Client 둘 다 RC카가 보여야 한다.
2. Host, Client 둘 다 맵이 보여야 한다.
3. 둘 중 어느 쪽에서 차량 상태가 바뀌어도 서로 동기화되어야 한다.
4. Host에도 파일 업로드 기능이 있어야 한다.
5. 채팅 기능이 있어야 한다.
6. HostMigration이 필요하다.
7. 방 나가기 기능이 필요하다.
8. `But_Update`는 Host/Client 공용 버튼으로 재배치되어야 한다.
9. `HostJoinRequestMonitorUI`는 Client에서도 보여야 하지만, 참여자 수락/거절은 Host만 가능해야 한다.
10. JoinRequest 관련 Text/상태 표시는 공용으로 보여야 하고, `HostJoinRequestItemUI` 기반 `Item Prefab`은 Host만 보여야 한다.

## 3. 현재 기준선 정리

### 3.1 재사용 가능한 기존 문서
- [NetworkCarPlan](../network/NetworkCarPlan.md)
  - `PhotonSessionName` / `ApiRoomId` 이원화 규칙 유지
  - 생성, 입장, 승인, 공유, 저장 흐름 재사용
- [NetworkCarPRD](../../documents/network/NetworkCarPRD.md)
  - `HostNetworkCarCoordinator`, `HostParticipantSlotRegistry`, `HostCarSpawner`, `HostExecutionScheduler` 재사용
  - 단, "Host 화면 기준" 전제는 이번 계획에서 폐기
- [ReHostJoinPlan](../auth/ReHostJoinPlan.md)
  - Host 승인 UI, 새 요청 목록, 수동 승인/거절 패턴 재사용
- [ServerChatPlan](../auth/ServerChatPlan.md)
  - 패킷 규약, 소켓/버퍼 계층, 채팅 기능 단계별 확장 계획 재사용
- [ReLobbyPlan](../auth/ReLobbyPlan.md)
  - Scene 이동 책임 분리, room flow / scene navigator 분리 원칙 재사용
- [MapMergeintoMiroPlan](../map-miro/MapMergeintoMiroPlan.md)
  - `ChangeMap` 중심 맵 관리, Plane 기준 렌더링/런타임 맵 카탈로그 구조 재사용

### 3.2 현재 코드 기준선
- `Assets/Scripts/ChatRoom/NetworkUIManager.cs`
  - 역할에 따라 `Host UI`, `Client UI`만 분기한다.
  - 공용 UI 슬롯 `_commonUI`는 있으나 현재 씬에서 비어 있다.
- `Assets/Scenes/03_NetworkCarTest.unity`
  - `Host UI`, `Client UI` 오브젝트가 분리되어 있다.
  - 지금 구조는 "시뮬레이션 공용 영역"보다 "역할별 패널 표시" 쪽에 가깝다.
- `Assets/Scripts/NetworkCar/NetworkRCCar.cs`
  - 위치, 회전, 속도, 색상, 사용자 ID, 실행 상태 동기화 기반이 이미 있다.
  - 재사용 우선 대상이다.
- `Assets/Scripts/Map/ChangeMap.cs`
  - 로컬 맵 전환과 런타임 맵 등록은 이미 있다.
  - 하지만 현재 맵 인덱스/선택 상태의 네트워크 동기화 계층은 없다.
- `Assets/Scripts/ChatRoom/ClientBlockShareUploadButton.cs`
  - 업로드 기능은 사실상 Client 전용으로 붙어 있고, `But_Update`도 역할별 UI에 묶여 있을 가능성이 높다.
- `Assets/Scripts/ChatRoom/HostBlockShareAutoRefreshPanel.cs`
  - Host 목록/상세 확인 기능이 있다.
- `Assets/Scripts/ChatRoom/HostBlockShareSaveToMyLevelButton.cs`
  - Host 저장/검증 기능이 있다.
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorUI.cs`
  - 현재는 Host 중심 모니터이며, 공용 텍스트/상태와 Host 전용 액션 영역이 함께 묶여 있을 가능성이 높다.
- `Assets/Scripts/ChatRoom/HostJoinRequestItemUI.cs`
  - 승인/거절 버튼이 포함된 Host 전용 행 UI다.
- `Assets/Scripts/Network/Fusion/FusionConnectionManager.cs`
  - `ShutdownAsync()`가 이미 있다.
  - `OnHostMigration(...)` 콜백은 있지만 현재 로그만 남기고 미구현이다.
- `Assets/Scripts/Network/Fusion/FusionNetworkBootstrap.cs`
  - HostMigration 콜백이 있지만 미구현이다.
- `Assets/Scripts/NetworkCar/HostNetworkCarCoordinator.cs`
  - HostMigration 콜백이 비어 있다.
- `Assets/Scripts/Server/Packet/GenPackets.cs`
  - `C_Chat`, `S_Chat` 패킷이 이미 있다.
  - 다만 현재 스키마에는 `roomId/sessionName/senderName`이 없어서 방 단위 채팅으로 바로 쓰기 어렵다.
- `Assets/Scripts/Server/NetworkManager.cs`
  - 소켓 연결 골격은 있으나, 현재 `03_NetworkCarTest` 공용 채팅 UI에 연결된 서비스 계층은 없다.

## 4. 최종 방향

### 4.1 공용 시뮬레이션 화면 + 역할별 도구 패널
- `03_NetworkCarTest`는 Host/Client 모두 같은 맵, 같은 차량 집합, 같은 방 상태를 본다.
- `Host UI` / `Client UI`는 "무엇을 볼지"가 아니라 "무슨 도구를 조작할지"만 나눈다.
- 공용으로 보여야 하는 것:
  - 메인 시뮬레이션 뷰
  - 현재 맵
  - 참여자/차량 상태
  - `But_Update`
  - JoinRequest 상태 Text/요약
  - 채팅
  - 방 나가기
- 역할별로 달라야 하는 것:
  - Host: 승인/거절 액션, `HostJoinRequestItemUI` 목록, 저장/검증, HostMigration 상태
  - Client: 읽기 전용 JoinRequest 상태 확인, 내 코드 선택, 내 차량 상태

### 4.2 동기화 원칙
- 방 식별자는 계속 `PhotonSessionName` / `ApiRoomId` 이원화 규칙을 유지한다.
- 차량 상태는 한 차량당 하나의 authoritative writer만 가진다.
- 맵 상태도 세션 단일 소스에서 관리하고 나머지 클라이언트는 적용만 한다.
- HostMigration 시 복구해야 할 최소 상태:
  - `PhotonSessionName`
  - `ApiRoomId`
  - 참여자-슬롯 매핑
  - 차량-사용자 바인딩
  - 현재 맵 인덱스
  - 필요한 경우 최신 코드 매핑 정보

## 5. 기능별 계획

### 5.1 Host / Client 공용 시뮬레이션 화면
- `NetworkUIManager`를 유지하되, `_commonUI`를 실제 공용 HUD 루트로 사용한다.
- `Host UI`, `Client UI`는 각각 "Host 전용 툴 영역", "Client 전용 툴 영역"으로 축소한다.
- 공용 HUD 후보:
  - 방 이름 / 역할 / 인원 표시
  - 참여자 목록
  - 공용 업로드 버튼 `But_Update`
  - JoinRequest 상태 Text
  - 채팅 패널
  - 방 나가기 버튼
- 구현 원칙:
  - 시뮬레이션 카메라나 맵/차량 오브젝트를 역할별 UI 활성화에 묶지 않는다.
  - 씬 루트의 월드 영역은 양쪽 모두 항상 보이게 유지한다.

### 5.1.1 공용 업로드 버튼 배치 원칙
- `But_Update`는 `Host UI`나 `Client UI` 어느 한쪽에 두지 않고 `_commonUI` 아래 공용 버튼으로 둔다.
- Host와 Client 모두 같은 버튼을 보고 같은 업로드 진입점을 사용한다.
- 업로드 로직은 "Client 전용 버튼 스크립트"로 고정하지 않고, 공용 업로드 패널/버튼으로 재구성하거나 기존 `ClientBlockShareUploadButton`을 공용 컴포넌트처럼 쓸 수 있게 정리한다.
- 버튼의 표시 여부는 역할과 무관하게 같아야 하며, 권한 차이는 업로드 자체가 아니라 이후 Host 전용 저장/검증 단계에서 나뉜다.

### 5.1.2 JoinRequest 표시와 권한 분리 원칙
- `HostJoinRequestMonitorUI`는 Host 전용 패널이 아니라 "공용 정보 + Host 액션" 구조로 재정의한다.
- 공용으로 보여야 하는 것:
  - 대기 중 요청 수
  - 최근 요청 상태 Text
  - 비어 있음/로딩/오류 메시지
- Host 전용으로 남겨야 하는 것:
  - `HostJoinRequestItemUI` 기반 `_requestItemPrefab`
  - 승인 버튼
  - 거절 버튼
- 결론적으로 JoinRequest의 Text/상태는 `_commonUI`에서 모두 보되, `Item Prefab` 목록과 수락/거절 액션은 `Host UI`에서만 보이게 한다.
- Client에서 같은 정보를 보더라도 읽기 전용이어야 하며, 승인/거절 API 호출 경로는 비활성 또는 미노출 상태여야 한다.
- 현재 `HostJoinRequestMonitorUI`가 텍스트와 액션을 강하게 결합하고 있으면, 공용 상태 표시부와 Host 전용 아이템 리스트부를 분리하는 리팩터링을 전제로 한다.

### 5.2 맵 표시 및 맵 동기화
- `ChangeMap`은 계속 맵 적용 담당으로 재사용한다.
- 추가로 필요한 것:
  - 현재 맵 인덱스 네트워크 동기화
  - 런타임 맵 등록/선택 상태 동기화
  - Host가 맵을 바꾸면 Client도 동일 인덱스를 적용
- 맵 관련 원칙:
  - 맵 데이터 모델은 기존 `ChangeMap` + 런타임 카탈로그 구조를 재사용한다.
  - 네트워크 동기화는 "맵 렌더링"이 아니라 "현재 선택 상태"만 공유한다.
  - 맵 적용은 각 클라이언트가 로컬 `ChangeMap.ApplyMap(...)`로 수행한다.

### 5.3 RC카 표시 및 차량 상태 동기화
- `NetworkRCCar`의 기존 transform/color/userId/running sync를 우선 재사용한다.
- `HostNetworkCarCoordinator` / `HostCarSpawner` / `HostExecutionScheduler`도 최대한 유지한다.
- 이번 요구에서 명확히 정해야 할 것:
  - "차량을 누가 실제로 움직이는가"
  - "클라이언트 입력은 상태 authority에 어떻게 전달하는가"
- 권장 원칙:
  - 1차는 Host authoritative 유지
  - Client는 코드 선택/의도 전달
  - 실제 transform commit은 상태 authority가 1곳에서만 수행
- 이유:
  - `VirtualCarPhysics`와 블록 실행기가 아직 Host 중심 구조라서 이중 쓰기 시 쉽게 어긋난다.

### 5.4 공용 업로드 기능 재구성
- 업로드 API는 새로 만들지 않고 기존 `ChatRoomManager.UploadBlockShare(...)`를 그대로 재사용한다.
- `But_Update`는 공용 버튼으로 승격하고, Host/Client 모두 같은 업로드 진입 경로를 사용한다.
- `ClientBlockShareUploadButton`의 업로드 로직은 이름과 다르게 Host에서도 재사용 가능해야 하므로, 다음 둘 중 하나로 정리한다.
  - 공용 업로드 패널/버튼 컴포넌트로 추출
  - 기존 컴포넌트를 유지하되 역할 의존성을 제거해 `_commonUI`에서 사용
- 목표:
  - Host와 Client가 같은 업로드 경로를 사용
  - roomId 해석도 `NetworkRoomIdentity.ResolveApiRoomId(...)` 규칙을 공유
  - 버튼 위치는 공용, 저장/검증 후속 액션만 Host 전용
- Host 전용으로 남겨야 하는 기능:
  - 공유 목록 조회
  - save-to-my-level
  - 저장 검증

### 5.5 채팅 기능
- 채팅은 "공용 UI"에 들어가야 한다.
- 출력 형식은 `displayName : message`로 고정한다.
- 재사용 방향:
  - `ServerChatPlan`의 소켓/버퍼/패킷 계층 재사용
  - `C_Chat`, `S_Chat` 패킷 구조 확장
- 반드시 먼저 보완할 점:
  - 현재 패킷은 방 구분 정보가 없다.
  - 현재 `S_Chat`은 `playerId`와 `chat`만 있어서 표시명/방 식별/시간 정보가 부족하다.
- 권장 추가 필드:
  - `PhotonSessionName` 또는 `ApiRoomId`
  - `senderUserId`
  - `senderDisplayName`
  - `message`
  - 필요 시 timestamp
- UI/서비스 분리 원칙:
  - 채팅 패널은 `ChatView`
  - 송수신/연결은 별도 `NetworkChatService`
  - 방 입장/퇴장 시 chat connect/disconnect를 룸 수명주기에 맞춰 처리

### 5.6 HostMigration
- 현재는 `FusionConnectionManager`, `FusionNetworkBootstrap`, `HostNetworkCarCoordinator`에 콜백만 있고 구현이 없다.
- 이번 계획에서 HostMigration은 별도 하위 단계로 분리한다.
- 최소 목표:
  - Host migration 발생 감지
  - 새 host에서 runner/context 재바인딩
  - UI에 migration 중 상태 노출
- 확장 목표:
  - 차량/슬롯/코드/맵 상태 복구
  - host 전용 패널 자동 재결합
  - 이전 host 이탈 후 세션 지속
- 중요한 점:
  - HostMigration은 단순 UI 변경이 아니라 세션 상태 스냅샷 전략이 먼저 필요하다.

### 5.7 방 나가기 기능
- Host/Client 공용 `Leave Room` 버튼을 공용 HUD에 둔다.
- 기본 흐름:
1. UI polling / 업로드 대기 / 저장 대기 / 채팅 송수신 정리
2. 필요 시 실행 scheduler 정지
3. `FusionConnectionManager.ShutdownAsync()` 호출
4. `FusionRoomSessionContext`, `RoomSessionContext`, room identity 정리
5. `AppScenes.Lobby`로 복귀
- Host 나가기 특이사항:
  - HostMigration을 적용하는 경우 migration 절차를 먼저 시도
  - migration 미적용 상태면 방 종료와 같은 효과를 낼 수 있음

## 6. 권장 구현 순서

### Phase A. 씬/UI 구조 재정리
- `NetworkUIManager` 기준으로 `_commonUI`를 실제 사용하도록 변경
- `Host UI` / `Client UI`를 역할별 툴 패널로 축소
- 공용 HUD 배치
- `But_Update`를 `_commonUI`로 이동
- `HostJoinRequestMonitorUI`를 "공용 Text/상태 + Host 전용 Item Prefab/액션" 구조로 분리

### Phase B. 공용 표시 상태 동기화
- 맵 인덱스 동기화
- 차량 표시/카메라 검증
- Host/Client 모두 같은 맵과 차량을 보는지 확인

### Phase C. 업로드/승인목록/채팅 통합
- `But_Update` 공용 업로드 동작 검증
- JoinRequest 공용 Text와 Host 전용 Item Prefab 동작 검증
- 공용 채팅 패널 추가
- 업로드/채팅 모두 room scope 기준으로 검증

### Phase D. 방 수명주기 보완
- 방 나가기 구현
- 상태 정리 및 Lobby 복귀
- 비정상 종료 대비 로그/상태 메시지 정리

### Phase E. HostMigration
- migration 스냅샷 설계
- runner/context 재바인딩
- 차량/맵/코드 복구

## 7. 영향 파일 후보

### 7.1 재사용/수정 우선
- `Assets/Scripts/ChatRoom/NetworkUIManager.cs`
- `Assets/Scripts/ChatRoom/ClientBlockShareUploadButton.cs`
- `Assets/Scripts/ChatRoom/HostBlockShareAutoRefreshPanel.cs`
- `Assets/Scripts/ChatRoom/HostBlockShareSaveToMyLevelButton.cs`
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorUI.cs`
- `Assets/Scripts/ChatRoom/HostJoinRequestItemUI.cs`
- `Assets/Scripts/ChatRoom/NetworkRoomRosterPanel.cs`
- `Assets/Scripts/Network/Fusion/FusionConnectionManager.cs`
- `Assets/Scripts/Network/Fusion/FusionNetworkBootstrap.cs`
- `Assets/Scripts/Network/Fusion/FusionRoomService.cs`
- `Assets/Scripts/NetworkCar/NetworkRCCar.cs`
- `Assets/Scripts/NetworkCar/HostNetworkCarCoordinator.cs`
- `Assets/Scripts/Map/ChangeMap.cs`
- `Assets/Scenes/03_NetworkCarTest.unity`

### 7.2 신규 가능성
- `Assets/Scripts/ChatRoom/NetworkRoomCommonHud.cs`
- `Assets/Scripts/ChatRoom/SharedBlockShareUploadPanel.cs`
- `Assets/Scripts/ChatRoom/NetworkRoomLeaveButton.cs`
- `Assets/Scripts/Chat/NetworkChatService.cs`
- `Assets/Scripts/Chat/NetworkChatPanel.cs`
- `Assets/Scripts/Network/Fusion/FusionHostMigrationCoordinator.cs`
- `Assets/Scripts/Map/NetworkMapStateSync.cs`

## 8. 리스크와 대응
- 리스크: `NetworkCarPRD`의 Host-only 전제가 남아 있는 클래스에서 Client 표시/동기화가 깨질 수 있다.
  - 대응: Host-only 실행 책임과 "공용 표시 책임"을 분리한다.
- 리스크: 맵 전환이 현재 로컬 상태라 Host/Client 화면이 달라질 수 있다.
  - 대응: 맵 인덱스와 런타임 맵 선택 상태를 별도 네트워크 상태로 승격한다.
- 리스크: 채팅을 그대로 붙이면 다른 방 메시지와 섞일 수 있다.
  - 대응: room scope 필드를 패킷에 추가한다.
- 리스크: HostMigration은 콜백만 채우는 수준으로 끝나면 상태 유실이 크다.
  - 대응: 참가자/차량/맵/코드 스냅샷 항목을 먼저 확정한다.
- 리스크: Host 업로드를 Client 컴포넌트 복붙으로 만들면 이후 유지보수가 나빠진다.
  - 대응: 업로드 UI/로직을 공용 컴포넌트로 추출한다.
- 리스크: `HostJoinRequestMonitorUI`를 그대로 공용 UI에 노출하면 Client에게 승인/거절 버튼까지 보여 권한 경계가 흐려질 수 있다.
  - 대응: Text/상태 영역과 `HostJoinRequestItemUI` 액션 영역을 명시적으로 분리하고, Client에는 Host 전용 `Item Prefab`을 생성하지 않는다.

## 9. 완료 기준
- Host와 Client가 `03_NetworkCarTest`에서 같은 맵을 본다.
- Host와 Client가 같은 `NetworkRCCar` 집합을 본다.
- 차량 상태 변화가 양쪽 화면에서 일관되게 보인다.
- Host와 Client 모두 `But_Update`를 통해 업로드가 가능하다.
- JoinRequest의 Text/상태 표시는 Host와 Client 모두 볼 수 있다.
- `HostJoinRequestItemUI` 기반 `Item Prefab`과 승인/거절 버튼은 Host에서만 보이고 동작한다.
- 채팅이 `이름 : 메시지` 형식으로 같은 방 안에서만 보인다.
- HostMigration 콜백이 no-op가 아니다.
- Host와 Client 모두 방 나가기 후 Lobby로 정상 복귀한다.

## 10. 이번 문서의 결론
- 이번 작업은 `Host UI`를 조금 고치는 수준이 아니라, `03_NetworkCarTest`를 "Host 전용 실행 화면"에서 "공유 네트워크 룸 화면"으로 재정의하는 작업이다.
- 특히 `But_Update`와 `HostJoinRequestMonitorUI`는 "누가 보느냐"와 "누가 조작하느냐"를 분리해서 다시 설계해야 한다.
- 따라서 기존 계획은 버리지 않고 재사용하되, 기준 문서는 다음처럼 나눠서 가져간다.
  - 방 식별/승인/공유: [NetworkCarPlan](../network/NetworkCarPlan.md)
  - 차량 실행 코어: [NetworkCarPRD](../../documents/network/NetworkCarPRD.md)
  - Host 승인 UI: [ReHostJoinPlan](../auth/ReHostJoinPlan.md)
  - 채팅 코어: [ServerChatPlan](../auth/ServerChatPlan.md)
  - 씬/흐름 분리: [ReLobbyPlan](../auth/ReLobbyPlan.md)
  - 맵 모델: [MapMergeintoMiroPlan](../map-miro/MapMergeintoMiroPlan.md)
- 실제 구현은 "공용 UI shell -> 맵/차량 상태 동기화 -> 공용 업로드/JoinRequest 분리 -> 채팅 -> 나가기 -> HostMigration" 순서로 가는 것이 가장 안전하다.
