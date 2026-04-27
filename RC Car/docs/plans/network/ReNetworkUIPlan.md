# ReNetworkUIPlan

## 0. Status
- 작성일: 2026-04-24
- 대상 씬: `03_NetworkCarTest`
- 목적: 기존 `Host UI` / `Client UI` 분리 구조를 "공유 시뮬레이션 + 역할별 도구 패널" 구조로 재정리한다.
- 범위: UI 재배치, RC카/맵 동기화, 공용 업로드 재구성, JoinRequest 표시 재구성, 채팅, HostMigration, 방 나가기
- 2026-04-27 추가 확인:
  - `ClientBlockShareListPanel`은 단순 GUI 표시 클래스가 아니라 로컬 파일 목록 로딩, 버튼 바인딩, 업로드 실행 진입점까지 함께 가진다.
  - 따라서 "GUI가 필요 없으니 주석 처리" 방식은 `Client DB UI`만 숨기는 것이 아니라 Update/upload 기능 자체를 같이 끊을 수 있다.
  - `But_Save`가 안 되는 것처럼 보이는 증상도 Host save 로직 자체 문제와, Update/upload 경로가 먼저 죽어서 유효한 share/selection이 사라진 2차 증상을 분리해서 봐야 한다.

## 1. 왜 새 계획이 필요한가
- 기존 [NetworkCarPlan](../network/NetworkCarPlan.md)은 Photon + API 하이브리드 방 구조 복구에 초점이 있었고, UI 통합은 범위가 아니었다.
- 기존 [NetworkCarPRD](../../documents/network/NetworkCarPRD.md)는 "Host 화면 기준 1차 목표" 문서라서 Client 실시간 시뮬레이션 반영, HostMigration, 방 나가기, 공용 채팅을 제외하거나 후순위로 뒀다.
- 현재 `03_NetworkCarTest`는 `NetworkUIManager`가 `Host UI` / `Client UI`를 역할별로 켜고 끄는 구조이며, 씬에는 `_commonUI`가 비어 있다.
- 새 요구사항은 "Host와 Client가 같은 방의 시뮬레이션을 함께 본다"가 기준이므로, 기존 Host-only 전제를 버리고 별도 UI/룸 수명주기 계획이 필요하다.

## 2. 이번 변경 요구사항
1. Host, Client 둘 다 RC카가 보여야 한다.
2. Host, Client 둘 다 맵이 보여야 한다.
3. 둘 중 어느 쪽에서 차량 상태가 바뀌어도 서로 동기화되어야 한다.
4. Host와 Client 모두 로컬 파일을 선택해서 업로드할 수 있어야 한다.
5. `But_Update`는 "업로드할 파일 선택 -> 업로드 요청" 전용 흐름이어야 하며, 업로드 이후 RC카 적용을 직접 수행하면 안 된다.
6. `But_Save`는 Host에게만 보여야 한다.
7. `But_Save`는 Update로 서버에 올라온 공유 리스트에서 현재 선택한 항목 1개만 대상으로 삼아야 한다.
8. `But_Save`의 적용 대상은 업로드한 사용자의 RC카가 아니라, Save를 누른 Host 자신의 RC카여야 한다.
9. 채팅 기능이 있어야 한다.
10. HostMigration이 필요하다.
11. 방 나가기 기능이 필요하다.
12. `But_Update`는 Host/Client 공용 버튼으로 재배치되어야 한다.
13. `HostJoinRequestMonitorUI`는 Client에서도 보여야 하지만, 참여자 수락/거절은 Host만 가능해야 한다.
14. JoinRequest 관련 Text/상태 표시는 공용으로 보여야 하고, `HostJoinRequestItemUI` 기반 `Item Prefab`은 Host만 보여야 한다.
15. Update가 참조하는 로컬 파일 리스트와 Save가 참조하는 서버 공유 리스트는 버튼, 패널, 선택 상태를 절대 공유하면 안 된다.

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
  - `Client DB UI`처럼 시작 시 비활성인 루트 아래에 기능 컴포넌트를 두면, panel 활성화 여부가 곧 기능 활성화 여부가 되는 구조적 의존성이 생긴다.
- `Assets/Scripts/NetworkCar/NetworkRCCar.cs`
  - 위치, 회전, 속도, 색상, 사용자 ID, 실행 상태 동기화 기반이 이미 있다.
  - 재사용 우선 대상이다.
- `Assets/Scripts/Map/ChangeMap.cs`
  - 로컬 맵 전환과 런타임 맵 등록은 이미 있다.
  - 하지만 현재 맵 인덱스/선택 상태의 네트워크 동기화 계층은 없다.
- `Assets/Scripts/ChatRoom/ClientBlockShareUploadButton.cs`
  - 업로드 기능은 사실상 Client 전용으로 붙어 있고, `But_Update`도 역할별 UI에 묶여 있을 가능성이 높다.
- `Assets/Scripts/ChatRoom/ClientBlockShareListPanel.cs`
  - 현재는 단순 패널 view가 아니라 로컬 파일 목록 로딩, 버튼 바인딩, 선택 상태 유지, 업로드 실행(`OnLoadClicked`)까지 함께 가진다.
  - 이 클래스를 GUI-only라고 보고 `OpenGui`, `BindButtons`, `SetPanelVisible` 같은 경로를 주석 처리하면 `But_Update`/`But_Load`/새로고침까지 같이 죽을 수 있다.
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
  - Host: 승인/거절 액션, `HostJoinRequestItemUI` 목록, 업로드된 share 목록, `But_Save`, 저장/검증, HostMigration 상태
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
- `But_Update`가 여는 패널의 데이터 소스는 서버 공유 목록이 아니라 로컬 파일 목록이어야 한다.
- 패널 내부의 실행 버튼은 `Load`보다 `Upload` 또는 `선택 파일 업로드`처럼 업로드 전용 의미가 드러나는 이름이 안전하다.
- 버튼의 표시 여부는 역할과 무관하게 같아야 하며, 권한 차이는 업로드 자체가 아니라 이후 Host 전용 저장/검증/적용 단계에서 나뉜다.
- `But_Update` 경로에서는 `save-to-my-level`, Host 슬롯 재바인딩, RC카 runtime 적용 같은 후속 동작이 절대 섞이면 안 된다.

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

### 5.4 공용 업로드와 Host 전용 적용 분리
- 이 영역의 핵심은 "업로드 소스"와 "적용 타깃"을 분리하는 것이다.
- 업로드 소스는 로컬 파일 목록이다.
  - 현재 기준 후보 UI: `ClientBlockShareListPanel`
  - 현재 기준 데이터 소스: `BE2_CodeStorageManager`가 읽는 로컬 파일
- 적용 타깃은 서버에 올라온 공유 share 목록이다.
  - 현재 기준 후보 UI: `HostBlockShareAutoRefreshPanel`
  - 현재 기준 데이터 소스: `ChatRoomManager.FetchBlockShares(...)`
- 두 목록은 이름이 비슷해도 목적이 다르므로 같은 패널, 같은 버튼, 같은 선택 상태를 절대 공유하면 안 된다.
- 업로드 API는 새로 만들지 않고 기존 `ChatRoomManager.UploadBlockShare(...)`를 재사용한다.
- Host 전용 적용 단계가 꼭 기존 `save-to-my-level` API를 거쳐야 한다면, 그것은 구현 세부사항으로 숨기고 UI 의미는 "Host 자신의 RC카에 적용"으로 유지한다.

### 5.4.1 `But_Update` 책임 재정의
- 표시 대상: Host, Client 모두.
- 입력값: 로컬 파일 1개 선택.
- 출력 결과: 선택한 파일 1개를 현재 room 기준 block share로 업로드.
- 종료 조건: `POST /api/chat/rooms/{roomId}/block-shares` 요청 성공/실패를 표시하고 끝난다.
- 금지 사항:
  - `save-to-my-level` 호출
  - `NetworkRCCar.TrySubmitCodeSelectionToHost(...)` 같은 RC카 적용 트리거
  - Host 슬롯/차량 재바인딩
  - 서버 공유 리스트의 선택 상태 변경
- 권장 UI 흐름:
1. `_commonUI`의 `But_Update` 클릭
2. 로컬 파일 선택 패널 오픈
3. 파일 1개 선택
4. 업로드 실행 버튼 클릭
5. 업로드 결과 표시
6. 필요 시 Host 전용 공유 리스트만 새로고침
- 문구 원칙:
  - 외부 진입 버튼은 `Update`를 유지해도 된다.
  - 패널 내부 실행 버튼은 `Load`보다 `Upload` 또는 `선택 파일 업로드`로 바꾸는 것이 안전하다.
  - 이유: `Load`는 차량 적용/실행으로 오해되기 쉽다.

### 5.4.2 `But_Save` 책임 재정의
- 표시 대상: Host만.
- 입력값: 서버 공유 리스트에서 현재 선택한 share 1개.
- 출력 결과: 선택한 share의 코드를 Host 자신의 RC카 runtime에 적용.
- 중요한 해석:
  - Save의 source는 업로드된 share다.
  - Save의 target은 share 소유자의 차량이 아니라 Save를 누른 Host 자신의 차량이다.
- 단일 선택 원칙:
  - `But_Save`는 항상 1개 share만 처리한다.
  - 여러 개를 체크해서 한 번에 저장하는 현재 batch 성격은 이번 요구사항과 맞지 않으므로 제거하거나 별도 관리자 기능으로 분리한다.
- 영속 저장과 실행 적용을 한 버튼에 섞지 않는다.
  - 지금처럼 `save-to-my-level`이라는 이름이 꼭 필요하다면, UI 라벨과 내부 구현 의미를 분리해야 한다.
  - 실제로 "내 계정의 보관함에 저장"까지 필요하다면 `Apply`와 `Save To My Level`을 별도 버튼으로 나누는 편이 더 명확하다.

### 5.4.3 현재 오류의 구조적 원인
- `ClientBlockShareListPanel.OnLoadClicked()`는 현재 Photon 전송(`TrySendSelectedCodeByPhoton(...)`)과 API 업로드(`TrySendSelectedCodeByApi(...)`)를 동시에 묶고 있어, Update가 "올리기만" 하는 흐름으로 유지되지 못한다.
- `ClientBlockShareListPanel`은 현재 view/controller 역할을 함께 가지고 있어, `OpenGui`, `BindButtons`, `FetchBlockShareListNow`, `OnLoadClicked`, `SetPanelVisible`을 같은 클래스 안에서 함께 다룬다.
- 따라서 `ClientBlockShareListPanel` 내부 GUI 경로를 주석 처리하는 방식은 "패널만 숨기는 수정"이 아니라 "업로드 로직과 버튼 바인딩까지 같이 죽이는 수정"이 되기 쉽다.
- `Client DB UI`처럼 시작 시 비활성인 오브젝트 아래에 `ClientBlockShareListPanel`과 `ClientBlockShareUploadButton`를 두면, panel이 열리기 전까지 `OnEnable()`과 런타임 버튼 바인딩이 일어나지 않는 수명주기 의존성이 생긴다.
- `HostBlockShareSaveToMyLevelButton.OnClickSaveToMyLevel()`은 Host 전용 적용 버튼인데, 이름과 endpoint가 "저장" 중심이라 "적용" 의미가 가려진다.
- `HostBlockShareSaveToMyLevelButton.CollectTargetShareIds()`는 여러 share를 저장할 수 있게 되어 있어 "선택된 파일만 적용" 요구사항과 충돌한다.
- `HostNetworkCarCoordinator.HandleBlockShareSaveSucceededAsync(...)`는 현재 `ResolveUserIdFromShare(...)`를 통해 share 소유자 기준으로 적용 대상을 잡고 있어, "Host 자신의 RC카에 적용" 요구사항과 충돌한다.
- `UserBlockCodeDBList` 오브젝트에 `HostBlockShareAutoRefreshPanel`, `ClientBlockShareListPanel`, `ClientBlockShareUploadButton`이 함께 붙어 있어 로컬 업로드 흐름과 Host 적용 흐름이 같은 패널처럼 보인다.
- `HostBlockShareSaveToMyLevelButton._refreshVerifyButton`과 `ClientBlockShareListPanel._refreshButton`이 동일 버튼(`But_Re`, fileID `671228116`)을 공유하고 있어, 버튼 1회 클릭 시 서로 다른 흐름이 동시에 발화할 수 있다.
- `But_Update`, `But_Load`, `But_Save` 이름이 각각 "업로드 진입", "실제 업로드", "Host 적용"을 명확히 드러내지 못해 사용자 체감상 같은 기능처럼 보이기 쉽다.
- `But_Save`가 안 되는 것처럼 보이는 현상도 항상 같은 원인이 아니다.
  - 경우 1: Host 공유 리스트에 유효한 share가 없거나 선택이 비어 있어 Save 전제조건이 성립하지 않는 경우
  - 경우 2: Save API는 성공하지만 Host 자신의 RC카 runtime 적용 단계가 실패하는 경우
  - 문서와 디버깅 기준은 이 둘을 반드시 분리해야 한다.

### 5.4.4 수정 방향
1. view / controller 책임 분리:
   `ClientBlockShareListPanel`에서 "패널 표시/숨김" 책임과 "로컬 파일 목록 로딩/선택/업로드 실행" 책임을 분리한다.
2. 수명주기 분리:
   업로드 controller는 `_commonUI` 또는 항상 활성 상태인 별도 루트에 두고, `Client DB UI` 활성화 여부에 의존하지 않게 한다.
3. 패널 분리:
   로컬 파일 업로드 패널과 서버 공유 리스트 패널을 씬 계층에서 분리한다.
4. 버튼 소유권 분리:
   `HostBlockShareSaveToMyLevelButton._refreshVerifyButton`은 Host 전용 버튼으로 분리하고, `ClientBlockShareListPanel._refreshButton`과 절대 동일 참조를 쓰지 않는다.
5. Update 경로 정리:
   `But_Update` 경로는 업로드만 수행하고, Photon 코드 선택 전송이나 RC카 적용은 분리한다.
6. GUI 제거/교체 원칙:
   GUI를 숨기거나 교체하더라도 `BindButtons`, `RefreshDbListAsync`, `OnLoadClicked`, 선택 상태 같은 기능 코어는 별도 controller에서 살아 있어야 한다. "주석 처리"는 임시 디버그일 수는 있어도 최종 해법이 되어서는 안 된다.
7. Save 경로 독립성:
   `HostBlockShareSaveToMyLevelButton`은 `HostBlockShareAutoRefreshPanel`의 선택 share만 읽고, `ClientBlockShareListPanel` 활성화 여부나 `Client DB UI` 수명주기에 절대 의존하지 않게 한다.
8. 대상 차량 해석 변경:
   Save 성공 후 적용 대상을 share owner 기준이 아니라 current host 기준으로 해석한다.
9. 문구/로그 정정:
   `But_Update`는 "파일 업로드", `But_Save`는 "선택 share를 내 RC카에 적용"처럼 UI 문구와 로그 prefix를 분리한다.
10. 검증 규칙 추가:
   Update는 upload endpoint만, Save는 host-apply 경로만 호출하는지 endpoint 로그와 runtime 적용 로그를 분리 검증한다.

### 5.4.5 GUI 주석 처리 이후의 기준 재정의
- `Client DB UI`를 열지 않게 만들고 싶다는 요구와 `ClientBlockShareListPanel`의 기능 자체를 제거하는 것은 같은 말이 아니다.
- `Client DB UI` 비활성화는 허용할 수 있지만, 그 오브젝트가 업로드 controller의 생명주기까지 함께 쥐고 있으면 안 된다.
- 현재처럼 `ClientBlockShareListPanel` 안에 GUI 메서드와 기능 메서드가 섞여 있는 상태에서는 다음 규칙을 기준으로 다시 설계해야 한다.
  - panel hidden != upload controller disabled
  - Client GUI disabled != Host Save disabled
  - Save failure != 항상 Save API failure
- 즉, 문서 기준선은 "GUI를 주석 처리해서 문제를 없앤다"가 아니라 "GUI가 꺼져 있어도 업로드/적용 controller는 독립적으로 살아 있다"여야 한다.
- 만약 `But_Update`를 더 이상 기존 `Client DB UI` 패널로 열지 않을 계획이라면, 최소한 아래 둘 중 하나는 문서 기준으로 확정해야 한다.
  - 대체 업로드 view를 `_commonUI` 아래에 새로 만든다.
  - 기존 패널 view는 제거하되, 로컬 파일 선택/업로드 controller는 별도 always-on 컴포넌트로 재구성한다.
- `But_Save` 검증도 두 단계로 나눠야 한다.
  - 1단계: Host 공유 리스트에서 선택된 share가 실제로 존재하는가
  - 2단계: 그 share가 Host 자신의 RC카 runtime에 적용되는가
  - 이 두 단계가 분리되지 않으면 "Save가 안 된다"는 현상을 정확히 분류할 수 없다.

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
- `ClientBlockShareListPanel`에서 panel view와 upload controller 책임을 분리하거나, `SharedBlockShareUploadPanel` 같은 공용 업로드 controller/view 조합으로 재구성
- 업로드 controller를 `Client DB UI` 비활성 오브젝트 바깥의 always-on 루트로 이동
- `HostBlockShareSaveToMyLevelButton`과 `ClientBlockShareListPanel`의 버튼 참조 충돌 제거 (`But_Re` 분리, 역할별 루트 분리)
- `ClientBlockShareListPanel`의 Update 경로에서 Photon 코드 선택 전송과 업로드 요청을 분리하고, Update는 업로드 전용 흐름으로 재정의
- `But_Save`는 Host 전용 단일 선택 적용만 수행하고, `But_Update/But_Load`는 업로드 경로만 호출되도록 endpoint 단위 검증
- `Client DB UI`가 비활성 또는 교체된 상태에서도 Update/upload controller가 살아 있는지 검증
- Save 성공 후 적용 대상이 share owner가 아니라 current host인지 검증
- 기존 share가 있는 상태에서 `Client DB UI` 활성화 여부와 무관하게 `But_Save`가 동작하는지 독립 검증
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
- `Assets/Scripts/ChatRoom/ClientBlockShareListPanel.cs`
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
- 리스크: `Host Save`와 `Client Update`가 같은 버튼/같은 패널 상태를 공유하면 기능 분리가 깨지고 디버깅이 어려워진다.
  - 대응: 버튼 참조뿐 아니라 데이터 소스도 분리하고, Update는 로컬 파일 목록만, Save는 서버 공유 목록만 읽도록 강제한다.
- 리스크: Update 경로가 Photon 코드 선택까지 같이 보내면 업로드만 하려던 파일이 즉시 차량 적용 흐름으로 이어질 수 있다.
  - 대응: Update 경로에서 `TrySendSelectedCodeByPhoton(...)` 같은 적용 트리거를 제거하거나 별도 액션으로 분리한다.
- 리스크: Save 성공 후 적용 대상이 share owner 기준으로 남아 있으면 Host가 선택한 파일이 Host 자신의 RC카가 아니라 다른 참가자 차량에 매핑될 수 있다.
  - 대응: Save target resolve 규칙을 current host 전용으로 바꾸고, 단일 선택만 허용한다.
- 리스크: `ClientBlockShareListPanel`처럼 GUI 메서드와 기능 메서드가 섞인 클래스를 "GUI니까 꺼도 된다"라고 보고 주석 처리하면, panel 표시만이 아니라 업로드/버튼 바인딩/선택 상태까지 함께 죽는다.
  - 대응: view/controller를 분리하고, GUI 제거는 view 레이어에서만 수행한다.
- 리스크: `Client DB UI`처럼 시작 시 비활성인 오브젝트 아래에 upload/save 관련 controller를 두면, 오브젝트 활성화 여부가 곧 기능 동작 여부가 되어 디버깅이 매우 어려워진다.
  - 대응: 기능 controller는 always-on 루트에 두고, panel은 선택적으로만 붙인다.

## 9. 완료 기준
- Host와 Client가 `03_NetworkCarTest`에서 같은 맵을 본다.
- Host와 Client가 같은 `NetworkRCCar` 집합을 본다.
- 차량 상태 변화가 양쪽 화면에서 일관되게 보인다.
- Host와 Client 모두 `But_Update`를 통해 업로드가 가능하다.
- `But_Update` 경로는 업로드 endpoint만 호출하고, Photon 코드 선택 전송이나 Host 적용 흐름을 직접 호출하지 않는다.
- `But_Save`는 Host에게만 보인다.
- `But_Save`는 서버 공유 리스트에서 선택한 항목 1개만 처리한다.
- `But_Save`를 눌렀을 때 적용 대상은 share owner가 아니라 current host의 RC카다.
- `But_Save`는 `But_Update/But_Load`의 업로드 endpoint와 혼선이 없다.
- `HostBlockShareSaveToMyLevelButton._refreshVerifyButton`과 `ClientBlockShareListPanel._refreshButton`은 서로 다른 버튼 참조를 사용한다.
- `Client DB UI`를 숨기거나 교체해도 upload controller는 계속 동작한다.
- `ClientBlockShareListPanel`가 view-only로 남는다면, 업로드 실행/선택/목록 로딩 책임은 별도 always-on controller에 있다.
- `But_Save`는 `Client DB UI` 활성화 여부와 무관하게 동작한다.
- `But_Save` 검증 시 "share 존재/선택"과 "Host RC카 runtime 적용" 결과가 분리되어 확인된다.
- JoinRequest의 Text/상태 표시는 Host와 Client 모두 볼 수 있다.
- `HostJoinRequestItemUI` 기반 `Item Prefab`과 승인/거절 버튼은 Host에서만 보이고 동작한다.
- 채팅이 `이름 : 메시지` 형식으로 같은 방 안에서만 보인다.
- HostMigration 콜백이 no-op가 아니다.
- Host와 Client 모두 방 나가기 후 Lobby로 정상 복귀한다.

## 10. 이번 문서의 결론
- 이번 작업은 `Host UI`를 조금 고치는 수준이 아니라, `03_NetworkCarTest`를 "Host 전용 실행 화면"에서 "공유 네트워크 룸 화면"으로 재정의하는 작업이다.
- 특히 `But_Update`와 `HostJoinRequestMonitorUI`는 "누가 보느냐"와 "누가 조작하느냐"를 분리해서 다시 설계해야 한다.
- `But_Update`와 `But_Save`는 더 이상 비슷한 버튼이 아니며, 각각 "로컬 파일 업로드"와 "Host 자신의 RC카 적용"이라는 완전히 다른 책임으로 나눠 설계해야 한다.
- 특히 Save 적용 대상은 share owner가 아니라 current host라는 점, 그리고 Save는 단일 선택만 허용해야 한다는 점을 기준 설계로 고정해야 한다.
- 추가로, `ClientBlockShareListPanel`을 GUI-only 클래스로 오해해서 주석 처리하는 접근은 금지해야 한다. 이 클래스 또는 그 대체 구조는 panel view와 기능 controller를 분리해서 다뤄야 한다.
- 따라서 기존 계획은 버리지 않고 재사용하되, 기준 문서는 다음처럼 나눠서 가져간다.
  - 방 식별/승인/공유: [NetworkCarPlan](../network/NetworkCarPlan.md)
  - 차량 실행 코어: [NetworkCarPRD](../../documents/network/NetworkCarPRD.md)
  - Host 승인 UI: [ReHostJoinPlan](../auth/ReHostJoinPlan.md)
  - 채팅 코어: [ServerChatPlan](../auth/ServerChatPlan.md)
  - 씬/흐름 분리: [ReLobbyPlan](../auth/ReLobbyPlan.md)
  - 맵 모델: [MapMergeintoMiroPlan](../map-miro/MapMergeintoMiroPlan.md)
- 실제 구현은 "공용 UI shell -> 맵/차량 상태 동기화 -> 공용 업로드/JoinRequest 분리 -> 채팅 -> 나가기 -> HostMigration" 순서로 가는 것이 가장 안전하다.
