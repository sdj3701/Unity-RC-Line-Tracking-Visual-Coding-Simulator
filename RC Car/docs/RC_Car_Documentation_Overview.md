# RC Car Documentation Overview

작성일: 2026-04-28
최근 갱신: 2026-05-14

이 문서는 `RC Car/docs` 아래에 흩어져 있는 문서와 계획서를 하나의 진입 문서로 정리한 통합 가이드다.
원본 문서를 삭제하거나 대체하지 않고, 빠르게 전체 구조를 파악하고 필요한 원문으로 이동하기 위한 인덱스 역할을 한다.

범위:
- `documents` 폴더의 기술 문서 10개
- `plans` 폴더의 계획 문서 18개
- 총 28개 원본 Markdown 문서와 이 개요 문서 1개

## 1. 프로젝트 한눈에 보기

이 프로젝트는 `BlocksEngine2`로 작성한 블록 코드를 저장하고, 이를 런타임 JSON으로 변환한 뒤 `VirtualArduino` 계열 런타임을 통해 RC카 라인트레이싱을 시뮬레이션하는 Unity 프로젝트다.

핵심 씬:
- `Assets/Scenes/00_Login.unity`: 현재 로그인 진입 씬
- `Assets/Scenes/01_Lobby.unity`: 방 목록, 생성, 입장 흐름을 담당하는 로비 씬
- `Assets/Scenes/02_SingleCreateBlock.unity`: 블록 작성, 저장/불러오기, 단일 주행 테스트 씬
- `Assets/Scenes/03_NetworkCarTest.unity`: 네트워크 차량, 공유, 채팅 실험 씬
- `Assets/Scenes/Miro Test Scene.unity`: 미로 생성/저장/맵 통합 테스트 씬

핵심 실행 체인:
1. 사용자가 BlocksEngine2에서 블록을 작성한다.
2. `BE2_UI_ContextMenuManager`가 XML을 생성하고 JSON으로 변환한다.
3. `BlockCodeExecutor`가 런타임 JSON을 로드한다.
4. `VirtualArduinoMicro`가 핀 매핑과 I/O 라우팅을 담당한다.
5. `VirtualMotorDriver`와 `VirtualLineSensor`가 모터/센서 동작을 제공한다.
6. `VirtualCarPhysics`가 물리 이동을 적용한다.

## 2. 이 문서를 읽는 방법

추천 독자별 시작 순서:

### 2.1 처음 인수인계받은 경우
1. `documents/handover/HANDOVER_RC_CAR.md`
2. `plans/hardware/ArduinoPlan.md`
3. `documents/block-runtime/BlockCodeLogicDocument.md`
4. `documents/auth/AuthFunctionGuide.md`

### 2.2 블록 저장/실행 흐름을 고치려는 경우
1. `documents/block-runtime/BlockCodeDocument.md`
2. `documents/block-runtime/BlockCodeLogicDocument.md`
3. `documents/block-runtime/BlockOpNotDesign.md`
4. `plans/pin-binding/PinPlan.md`
5. `plans/pin-binding/BindPinRuntimeImplementationPlan.md`

### 2.3 로그인/로비/채팅을 고치려는 경우
1. `documents/auth/AuthFunctionGuide.md`
2. `plans/auth/LoginPlan.md`
3. `plans/auth/ReLoginPlan.md`
4. `plans/auth/LobbyPlan.md`
5. `plans/auth/ChatPlan.md`
6. `plans/auth/ServerChatPlan.md`

### 2.4 네트워크 차량과 공유 흐름을 고치려는 경우
1. `documents/network/NetworkCarPRD.md`
2. `plans/network/NetworkCarPlan.md`
3. `plans/network/ReNetworkUIPlan.md`
4. `plans/network/RCCarSyncPlan.md`
5. `plans/network/ReShareUIIPlan.md`
6. `documents/block-runtime/BlockCodeNetworkLogicDocument.md`

### 2.5 맵/미로 저장과 센서 연동을 고치려는 경우
1. `plans/map-miro/MiroAlgorithmPlan.md`
2. `plans/map-miro/MapMergeintoMiroPlan.md`
3. `plans/hardware/ArduinoPlan.md`

## 3. 현재 기준 핵심 아키텍처

### 3.1 인증
- 로그인은 ID/PW 로그인과 딥링크 토큰 인증을 함께 지원한다.
- `AuthManager`가 전체 인증 흐름의 중심이며, `AuthApiClient`, `AuthSessionStore`, `AuthTokenReceiver`가 보조 역할을 가진다.
- 토큰 검증 성공 시 사용자 상태 저장 후 메인 씬으로 이동한다.

### 3.2 블록 코드 저장/불러오기
- 저장 시 XML과 런타임 JSON을 함께 만든다.
- `BE2_UI_ContextMenuManager.LatestRuntimeJson`이 현재 실행기의 가장 가까운 메모리 기준점이다.
- 로컬 저장소는 `persistentDataPath/SavedCodes/*.xml`, `*.json`, 그리고 현재 실행용 `BlocksRuntime.xml/json`을 사용한다.

### 3.3 런타임 실행
- 현재 표준 실행 경로는 `BlockCodeExecutor -> VirtualArduinoMicro -> VirtualMotorDriver / VirtualLineSensor -> VirtualCarPhysics`다.
- 문서 전반에서 구형 `RCCar`, `RCCarSensor`, `BlocksGenerated` 계열은 레거시 또는 미사용 후보로 정리되고 있다.

### 3.4 핀 바인딩
- 최신 설계 기준은 "숫자값이 핀 번호와 같으냐"가 아니라 "블록의 pin 슬롯에 실제로 연결되어 사용되었느냐"다.
- 핀 변수 여부와 RC카 역할명(`leftSensor`, `rightMotorF` 등)은 분리해서 다룬다.

### 3.5 네트워크 차량
- 현재 네트워크 구조는 Photon과 API 방 시스템을 동시에 쓰는 하이브리드 방식이다.
- Photon은 실시간 세션과 차량 동기화를 맡고, API 방은 참가 요청, 블록 공유, 저장을 맡는다.
- `PhotonSessionName`, `ApiRoomId`, `UserLevelSeq`는 서로 다른 식별자이며 혼용하면 안 된다.

### 3.6 맵/미로
- 미로와 맵 통합 방향은 `Plane` 중심이다.
- 권장 방향은 라인 오브젝트를 많이 두는 방식보다, 미로를 텍스처/머티리얼로 베이크해 `ChangeMap` 흐름에 합치는 방식이다.

## 4. 문서 전체에서 반복되는 주요 결론

1. 표준 주행 파이프라인은 `VirtualArduino` 계열로 고정하는 쪽이 맞다.
2. 저장/로드와 실행 연결은 `OnCodeGenerated` 이벤트와 `LatestRuntimeJson`을 중심으로 보는 것이 가장 이해하기 쉽다.
3. 핀 매핑을 변수값만으로 추론하는 방식은 오탐 위험이 크며, 핀 슬롯 사용 이력 기반 설계가 더 안전하다.
4. 네트워크는 "Photon만" 또는 "API만"으로 단순화되지 않았고, 현재 문서 기준 정답은 하이브리드 구조다.
5. `03_NetworkCarTest`는 단순 Host 전용 실험에서, "공유 시뮬레이션 + 역할별 도구 패널" 구조로 진화하는 중이다.
6. 맵/미로/센서 연동은 시각 렌더링보다 센서 판정 안정성을 우선으로 설계하는 경향이 강하다.

## 5. 도메인별 요약

### 5.1 인수인계와 운영 관점
- 인수인계 문서는 프로젝트 개요, 씬 구조, 저장 위치, 현재 리스크, 다음 담당자 체크리스트를 제공한다.
- 하드웨어 계획 문서는 실제 주행에 연결된 코드와 미사용 레거시 코드를 구분해준다.

### 5.2 인증, 로그인, 로비, 채팅
- `documents/auth`는 이미 존재하는 인증 코드의 함수별 설명서다.
- `plans/auth`는 로그인 구조 개선, 로비 분리, 채팅 연계, 참가 요청 UI 개편까지 포함한 설계/리팩토링 계획 묶음이다.
- 현재 소스 기준 채팅은 더 이상 계획만 있는 상태가 아니다. `Assets/Scripts/ChatRoom/NetworkChat` 아래에 Fusion RPC 기반 `NetworkChatManager`, 메시지 모델, 메시지 아이템, UI 컨트롤러가 실제 구현되어 있다.

### 5.3 블록 런타임
- `documents/block-runtime`는 블록 편집기 동작, XML 저장 구조, XML -> JSON 변환, JSON 실행, 네트워크 API 연동, `not` 연산 설계까지 포함한다.
- 블록 실행 경로를 수정할 때는 이 폴더가 가장 우선순위가 높다.

### 5.4 핀 바인딩
- `documents/pin-binding`은 BindPin 블록의 개념, UI, 충돌 규칙, XML/JSON 스키마 초안을 정리한다.
- `plans/pin-binding`은 "핀 변수 판별 기준"과 실제 런타임 반영 순서를 더 구체적으로 제시한다.

### 5.5 네트워크 차량과 코드 공유
- `documents/network/NetworkCarPRD.md`는 Host 중심 1차 프로토타입 요구사항을 고정한다.
- `plans/network`는 Photon/API 하이브리드 복구, 공유 UI 리팩토링, Save 버튼 책임 재정의, 공용 시뮬레이션 UI, HostMigration 대응까지 단계별로 확장한다.

### 5.6 맵과 Miro 미로
- `plans/map-miro`는 Miro 알고리즘 분석과 `ChangeMap` 중심 통합 방식을 정리한다.
- 핵심은 동적 미로를 맵 카탈로그에 안전하게 편입하고 센서 판정 품질을 유지하는 것이다.

### 5.7 외부 엔진 참고
- `documents/engine/ChangeLog.md`는 프로젝트 자체 문서라기보다 BlocksEngine2 계열 변경 이력 참고 자료에 가깝다.
- 버전 업그레이드나 호환성 점검이 필요할 때만 선택적으로 보면 된다.

## 6. 현재 소스 폴더 구성 (2026-05-14 기준)

이 문서의 앞부분이 "무슨 문서를 읽어야 하는가"에 초점을 맞췄다면, 이 섹션은 "현재 소스가 실제로 어디에 어떻게 놓여 있는가"를 빠르게 파악하기 위한 요약이다.

### 6.1 `Assets` 루트 구성
- `Assets/Scenes`: 현재 작업 씬이 모여 있다. 확인된 씬은 `00_Login`, `00_TestLogin`, `01_Lobby`, `02_SingleCreateBlock`, `03_NetworkCarTest`, `Miro Test Scene`, `Test Server`다.
- `Assets/Scripts`: 실제 프로젝트 로직이 가장 많이 모여 있는 핵심 폴더다.
- `Assets/BlocksEngine2`: 블록 에디터 엔진 원본/기반 코드다.
- `Assets/BlocksEngine2Custom`: BlocksEngine2를 프로젝트에 맞게 붙이는 커스텀 리소스/확장 영역이다.
- `Assets/Generated`: 생성 코드 보관 영역이며 현재 `BlocksGenerated.cs`가 있다.
- `Assets/Photon`: Photon/Fusion 관련 에셋 영역이다.
- `Assets/AddressableAssetsData`, `Assets/Resources`, `Assets/Settings`, `Assets/TextMesh Pro`, `Assets/TutorialInfo`: Unity 설정/리소스 보조 영역이다.

### 6.2 `Assets/Scripts` 현재 구성
- `App` (5개): API URL, 공통 헤더, 씬 이름, 라우트 상수 같은 전역 설정 계층이다.
- `Auth` (10개): 로그인, 토큰 검증, 세션 저장, 딥링크 수신, 프로토콜 등록과 테스트용 인증 진입점을 가진다.
- `ChatRoom` (31개): 방 목록, 참가 요청, 공용 UI, 블록 공유, 채팅 UI를 담당한다.
- `Core` (15개): XML -> 런타임 JSON 변환과 RC카 실행 코어가 있다. 이 안에 `VirtualArduino`와 `Legacy`가 같이 존재한다.
- `Lobby` (9개): 로비 화면 흐름, 방 생성/입장 서비스, 씬 전환, 룸 컨텍스트를 담당한다.
- `Map` (11개): 맵 전환, 코스 종료, Miro 미로 생성/베이크/저장/원격 저장소 관련 코드가 모여 있다.
- `Network` (11개): Photon Fusion 연결, 로비, 방 세션, 부트스트랩, 방 나가기 처리 계층이다.
- `NetworkCar` (11개): Host 기준 차량 생성, 슬롯 관리, 코드 바인딩, 실행 스케줄링, 네트워크 RC카 동기화 계층이다.
- `Player` (4개): 리스타트, 플레이어 조작, UI 토글 같은 플레이어 보조 스크립트다.
- `Server` (9개): 소켓 패킷, 세션, 버퍼, 네트워크 매니저 등 커스텀 서버 클라이언트 계층이다.
- `UI` (11개): 블록 드래그, 핀 시각화, 줌, 레이아웃 제어 같은 범용 UI 스크립트다.
- `Localization` (1개): 현지화 보조 스크립트다.

### 6.3 현재 구현 기준으로 눈에 띄는 구조
- 표준 RC카 실행 경로는 여전히 `Assets/Scripts/Core/VirtualArduino` 아래 7개 파일을 중심으로 유지된다.
- 레거시 경로도 완전히 제거된 것은 아니다. `Assets/Scripts/Core/Legacy`에 5개 파일이 있고, `Assets/Scripts/Car/Legacy`에 `RCCar.cs`, `RCCarSensor.cs`가 남아 있다.
- 씬 이름도 상수로 정리되어 있다. `Assets/Scripts/App/Defines/AppScenes.cs`에서 현재 기준 씬 이름을 `00_Login`, `01_Lobby`, `02_SingleCreateBlock`, `03_NetworkCarTest`로 관리한다.
- `ChatRoom`은 실제 코드 기준으로도 분리 작업이 진행된 상태다.
- `Assets/Scripts/ChatRoom/BlockShare`에 21개 파일이 있어 업로드, 로컬 목록, 원격 목록, 저장 버튼, 서비스/리포지토리/프로바이더 인터페이스까지 분리되어 있다.
- `Assets/Scripts/ChatRoom/NetworkChat`에 4개 파일이 있어 네트워크 채팅 모델, 매니저, 메시지 아이템, UI 컨트롤러가 따로 존재한다.
- 현재 채팅 구현은 단순 텍스트 전달 수준이 아니라, 로컬 히스토리 보관, 송신 쿨다운, StateAuthority 수신 제한, Runner fallback RPC, 메시지 버블 UI 자동 바인딩과 런타임 템플릿 생성까지 포함한다.
- `Assets/Scripts/Network/Fusion`에 11개 파일이 있어 Photon 연결과 방 수명주기 계층이 별도 모듈로 정리되어 있다.
- `Assets/Scripts/Map`에는 문서에서 계획 단계로 보이던 `MiroMapBaker`, `MiroMazeRemoteRepository`, `MiroRuntimeMapCatalogPersistence`가 실제 코드로 존재한다. 즉 맵-미로 통합은 계획만 있는 상태가 아니라 일부 구현이 이미 들어와 있다.

### 6.4 최근 작업 흔적과 반영 메모
- 최근 수정 시각 기준으로 `Assets/Scenes/03_NetworkCarTest.unity`가 2026-05-11에 다시 저장되어 있다. 현재 네트워크 씬이 가장 활발하게 손대는 영역으로 보는 것이 맞다.
- `Assets/Scripts/ChatRoom/NetworkChat/NetworkChatManager.cs`, `NetworkChatUIController.cs`, `NetworkChatMessageItem.cs`, `NetworkChatMessage.cs`는 2026-05-14에 다시 수정되었다. 즉 지난 문서 갱신 이후에도 채팅 계층이 추가 작업 중이다.
- 이 채팅 계층은 `NetworkChatManager`가 메시지 길이 제한, 로컬 쿨다운, 발신자별 accept cooldown, 로컬 히스토리, RPC 브로드캐스트를 담당하고, `NetworkChatUIController`가 입력창/버튼/스크롤/메시지 프리팹을 자동 연결하는 구조다.
- `Assets/Scripts/Map/ChangeMap.cs`, `RCCarCourseController.cs`, `RCCarEndTrigger.cs`와 `Assets/Scripts/NetworkCar/HostNetworkCarCoordinator.cs`, `HostExecutionScheduler.cs`, `HostRuntimeBinder.cs`는 2026-05-06에 수정되어, 맵 진행 흐름과 Host 실행 체인도 최근 보강된 상태다.
- `Assets/Scripts/Network/Fusion/NetworkRoomExitController.cs`도 2026-05-06 수정 기록이 있어, 방 종료/이탈 흐름까지 실제 코드 작업이 이어진 것으로 보인다.
- 문서 기준으로는 `plans/network/ReNetworkUIPlan.md`, `ReSavelButtonPlan.md`, `ReShareUIIPlan.md`, `RCCarSyncPlan.md`가 2026-04-27~2026-04-28에 최신 상태였고, 현재 소스는 그 이후 채팅/맵/Host 실행 쪽이 추가 진행된 흔적을 보인다.
- 현재 워킹트리에는 미커밋 변경 흔적도 있다.
- 현재 확인된 변경은 `Assets/AddressableAssetsData/Windows/addressables_content_state.bin` 수정 1건이다.

## 7. 원본 문서 인덱스

### 7.1 documents
- [documents/handover/HANDOVER_RC_CAR.md](documents/handover/HANDOVER_RC_CAR.md): 프로젝트 인수인계, 씬 구조, 저장 위치, 운영 리스크, 빠른 체크리스트.
- [documents/auth/AuthFunctionGuide.md](documents/auth/AuthFunctionGuide.md): 인증 관련 주요 클래스와 함수의 역할 설명.
- [documents/block-runtime/BlockCodeDocument.md](documents/block-runtime/BlockCodeDocument.md): BlocksEngine2 편집기 동작과 사용자 행동 기준 실행 흐름 설명.
- [documents/block-runtime/BlockCodeLogicDocument.md](documents/block-runtime/BlockCodeLogicDocument.md): 저장, XML 구조, JSON 변환, 실행기 로딩 우선순위, 핀 라우팅 정리.
- [documents/block-runtime/BlockCodeNetworkLogicDocument.md](documents/block-runtime/BlockCodeNetworkLogicDocument.md): 인증, 로비, 채팅, 블록 공유, Miro API까지 포함한 네트워크 API 도메인 문서.
- [documents/block-runtime/BlockOpNotDesign.md](documents/block-runtime/BlockOpNotDesign.md): `Block Op Not`의 의미, JSON 설계, 실행 규칙, 테스트 케이스.
- [documents/network/NetworkCarPRD.md](documents/network/NetworkCarPRD.md): Host 기준 네트워크 RC카 1차 프로토타입 요구사항과 실행 정책.
- [documents/pin-binding/BindPinBlockDesign.md](documents/pin-binding/BindPinBlockDesign.md): BindPin 블록의 UI, 배치 규칙, 충돌 규칙, 최소 기능 범위.
- [documents/pin-binding/BindPinSchemaDraft.md](documents/pin-binding/BindPinSchemaDraft.md): BindPin XML/JSON 스키마 초안과 backward compatibility 방향.
- [documents/engine/ChangeLog.md](documents/engine/ChangeLog.md): 엔진 변경 이력 참고 자료.

### 7.2 plans/auth
- [plans/auth/LoginPlan.md](plans/auth/LoginPlan.md): 현재 로그인 구조 분석과 수정 범위, 테스트 체크리스트.
- [plans/auth/ReLoginPlan.md](plans/auth/ReLoginPlan.md): 로그인 구조를 SRP/인터페이스 중심으로 재구성하는 장기 리팩토링 계획.
- [plans/auth/LobbyPlan.md](plans/auth/LobbyPlan.md): 로비 생성 흐름, 컴포넌트 책임, 상태 전이, 테스트 계획.
- [plans/auth/ReLobbyPlan.md](plans/auth/ReLobbyPlan.md): 로비 구조를 씬/모듈 책임 기준으로 다시 나누는 리팩토링 계획.
- [plans/auth/ChatPlan.md](plans/auth/ChatPlan.md): 로비에서 네트워크 씬으로 이어지는 채팅/룸 연동 계획.
- [plans/auth/ServerChatPlan.md](plans/auth/ServerChatPlan.md): 소켓 서버 계층, 패킷 규약, AWS 전환 체크리스트까지 포함한 서버 채팅 계획.
- [plans/auth/ReHostJoinPlan.md](plans/auth/ReHostJoinPlan.md): Host 참가 요청 목록 UI와 승인/거절 흐름 재설계.

### 7.3 plans/hardware
- [plans/hardware/ArduinoPlan.md](plans/hardware/ArduinoPlan.md): 실제 주행 파이프라인, 미사용 레거시 코드, 정리 우선순위.

### 7.4 plans/map-miro
- [plans/map-miro/MiroAlgorithmPlan.md](plans/map-miro/MiroAlgorithmPlan.md): Lua 미로 알고리즘 분석과 Unity 이식 기준, 저장/로드 설계.
- [plans/map-miro/MapMergeintoMiroPlan.md](plans/map-miro/MapMergeintoMiroPlan.md): `ChangeMap`과 Miro 미로를 Plane 기준으로 통합하는 전략.

### 7.5 plans/network
- [plans/network/NewNetworkPlan.md](plans/network/NewNetworkPlan.md): Photon Fusion 2 전환 초반 설계와 단계별 구현 계획.
- [plans/network/NetworkCarPlan.md](plans/network/NetworkCarPlan.md): Photon + API 하이브리드 방/공유 구조의 최종 복구 방향과 반영 결과.
- [plans/network/RCCarSyncPlan.md](plans/network/RCCarSyncPlan.md): Save 버튼 의미 재정의, 사용자별 코드 적용, 자동 실행 연결 계획.
- [plans/network/ReNetworkUIPlan.md](plans/network/ReNetworkUIPlan.md): 공용 시뮬레이션 화면과 역할별 도구 패널로 UI를 재구성하는 계획.
- [plans/network/ReSavelButtonPlan.md](plans/network/ReSavelButtonPlan.md): `But_Save` 책임 축소와 디버그/UI 분리 계획.
- [plans/network/ReShareUIIPlan.md](plans/network/ReShareUIIPlan.md): 업로드/리스트/호스트 패널 책임 분리와 공유 UI 리팩토링 계획.

### 7.6 plans/pin-binding
- [plans/pin-binding/PinPlan.md](plans/pin-binding/PinPlan.md): 핀 변수 정의, 판별 규칙, 역할 바인딩 원칙, 2026-04-24 런타임 수정 반영.
- [plans/pin-binding/BindPinRuntimeImplementationPlan.md](plans/pin-binding/BindPinRuntimeImplementationPlan.md): BindPin을 XML, JSON, 실행기, 가상 아두이노에 반영하는 실제 구현 단계.

## 8. 지금 기준으로 가장 중요한 문서 묶음

빠르게 우선순위를 정리하면 아래 8개가 핵심이다.

1. `documents/handover/HANDOVER_RC_CAR.md`
2. `plans/hardware/ArduinoPlan.md`
3. `documents/block-runtime/BlockCodeLogicDocument.md`
4. `documents/auth/AuthFunctionGuide.md`
5. `documents/network/NetworkCarPRD.md`
6. `plans/network/NetworkCarPlan.md`
7. `plans/network/ReNetworkUIPlan.md`
8. `plans/pin-binding/PinPlan.md`

이 8개만 읽어도 프로젝트의 현재 구조, 실행 흐름, 네트워크 방향, 핀 설계 기준을 대부분 파악할 수 있다.

## 9. 다음 문서 정리 규칙 제안

문서가 계속 늘어나는 상황이라면 아래 규칙을 유지하는 편이 좋다.

1. 확정된 구조와 운영 기준은 `documents`에 둔다.
2. 변경 중이거나 단계별 실행 순서가 필요한 문서는 `plans`에 둔다.
3. 새 문서를 추가하면 이 개요 문서의 인덱스도 같이 갱신한다.
4. 같은 주제에서 "현재 결론"이 바뀌면 예전 문서를 삭제하기보다, 최신 결론 문서에 선후관계를 명시한다.
5. 네트워크와 핀 바인딩처럼 설계 변화가 잦은 영역은 문서 상단에 기준일과 현재 상태를 꼭 적는다.

## 10. 현재 문서 묶음에서 보이는 우선 정리 포인트

- 인증 토큰/URL의 하드코딩 제거와 환경 분리
- Build Settings 정리와 실제 진입 씬 재점검
- `VirtualArduino` 표준 경로 외 레거시 주행 코드 정리
- `03_NetworkCarTest`의 공용 UI와 역할별 UI 책임 분리
- 핀 변수 판별 규칙과 BindPin 스키마를 런타임 구현과 계속 동기화
- 맵/미로 저장 포맷과 센서 판정 규칙의 일관성 유지

이 문서는 빠른 진입을 위한 지도다.
세부 구현이나 수정 작업 전에는 반드시 각 원본 문서를 직접 확인하는 것을 기준으로 삼는다.
