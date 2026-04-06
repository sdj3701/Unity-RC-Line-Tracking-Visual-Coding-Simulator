# ReLobbyPlan

## 1) 목적
- 기능 추가가 아니라 **구조 재정비(리팩토링)** 를 목표로 한다.
- 기준은 `씬 단위 책임 분리 + 클래스 세분화 + 객체화 + API/Scene 상수 Define 통합`이다.
- 결과적으로 `유지보수 비용`, `버그 전파 범위`, `씬 간 결합도`를 줄인다.

---

## 2) 현재 구조 진단 (코드 기준)

### 2-1. 씬/모듈 현황
- Build Settings 등록 씬:
- `00_Login`
- `01_Lobby`
- `02_SingleCreateBlock`
- `03_NetworkCarTest`
- 스크립트 볼륨:
- `Lobby` 9 files / 5,044 lines
- `ChatRoom` 7 files / 3,734 lines
- `Core` 15 files / 5,417 lines
- 최대 클래스:
- `Assets/Scripts/Lobby/ChatRoomManager.cs` 3,440 lines

### 2-2. 구조상 리스크
- `LobbyUIController`가 `LobbyRoomFlow`와 `ChatRoomManager`를 동시에 제어하여 경계가 섞여 있음.
- `ChatRoomManager`가 생성/목록/입장요청/승인/공유/저장까지 모두 처리하는 God Class 상태.
- API URL이 여러 클래스에 분산 하드코딩됨.
- `AuthManager`, `ChatRoomManager.Instance`, `RoomSessionContext`에 전역 의존이 큼.
- 씬 이름 문자열이 여러 파일에 중복 하드코딩됨(`"03_NetworkCarTest"` 등).

---

## 3) 목표 아키텍처 (씬 기준)

### 3-1. 최상위 구조
```text
Assets/Scripts
  App
    Defines
    Config
    Session
    Networking
  Scenes
    Login
      Presentation
      Application
      Infrastructure
    Lobby
      Presentation
      Application
      Infrastructure
      Domain
    NetworkCar
      Presentation
      Application
      Infrastructure
  Shared
    Auth
    DTO
    Utils
```

### 3-2. 씬별 책임
- `Login Scene`:
- 로그인 입력/토큰 검증/초기 세션 생성만 담당
- `Lobby Scene`:
- 방 생성/목록/입장 요청/블록 공유 데이터 선택까지 담당
- `NetworkCar Scene`:
- 실행 제어, Host/Client UI 분기, 차량 스폰/실행 담당
- 공통:
- API 호출, 세션 상태, 라우트/헤더/타임아웃은 `App` 레이어에서 재사용

---

## 4) Lobby 리팩토링 핵심 설계

### 4-1. 현재 클래스 -> 목표 클래스 분해
- 기존 `ChatRoomManager` 분해:
- `ChatRoomApiClient` (HTTP 요청/응답 전담)
- `ChatRoomCreateService`
- `ChatRoomListService`
- `JoinRequestService`
- `BlockShareService`
- `ChatRoomFacade` (임시 호환 계층, 기존 호출자 점진 이전용)
- 기존 `LobbyUIController` 분해:
- `LobbyView` (버튼/입력/UI 바인딩만)
- `LobbyPresenter` (UI 상태 갱신 규칙)
- `LobbyCoordinator` (씬 흐름 오케스트레이션)
- 기존 `RoomSessionContext`:
- `RoomSessionStore`(`DontDestroyOnLoad`)로 전환하고 인터페이스로 접근

### 4-2. Lobby 객체 모델
- `RoomInfo` (도메인 모델)
- `JoinRequestInfo`
- `BlockShareInfo`
- `LobbyState` (Busy/SelectedRoom/StatusMessage 등 UI 상태)
- `OperationResult<T>` (성공/실패/코드/메시지 공통 래퍼)

---

## 5) Define + 환경설정 전략 (API/Scene 공통)

### 5-1. 왜 이렇게 해야 하는가
- 하드코딩 URL/씬 문자열이 분산되면 변경 시 누락 위험이 매우 큼.
- Dev/Stage/Prod 전환을 Inspector 수동 변경에 의존하면 배포 실수가 발생함.
- API 경로와 베이스 URL을 분리해야 백엔드 변경 대응이 빠름.

### 5-2. 권장 구현

1. `Define` 클래스(상수)로 경로/씬/헤더/기본 타임아웃 통합
2. `ScriptableObject`로 환경별 Base URL 분리
3. `Scripting Define Symbols`로 빌드 환경 선택(`API_ENV_DEV`, `API_ENV_STAGE`, `API_ENV_PROD`)

### 5-3. 예시 코드

```csharp
// Assets/Scripts/App/Defines/AppScenes.cs
public static class AppScenes
{
    public const string Login = "00_Login";
    public const string Lobby = "01_Lobby";
    public const string Block = "02_SingleCreateBlock";
    public const string Network = "03_NetworkCarTest";
}
```

```csharp
// Assets/Scripts/App/Defines/ApiRoutes.cs
public static class ApiRoutes
{
    public const string AuthLogin = "/api/auth/login";
    public const string AuthMeByToken = "/api/users/me-by-token";
    public const string ChatRooms = "/api/chat/rooms";
    public const string RoomCreate = "/api/rooms";

    public static string JoinRequest(string roomId)
        => $"/api/chat/rooms/{UnityWebRequest.EscapeURL(roomId)}/join-request";
}
```

```csharp
// Assets/Scripts/App/Config/AppApiConfig.cs
[CreateAssetMenu(menuName = "App/ApiConfig")]
public class AppApiConfig : ScriptableObject
{
    public string devBaseUrl;
    public string stageBaseUrl;
    public string prodBaseUrl;
    public int requestTimeoutSeconds = 15;

    public string CurrentBaseUrl
    {
        get
        {
#if API_ENV_PROD
            return prodBaseUrl;
#elif API_ENV_STAGE
            return stageBaseUrl;
#else
            return devBaseUrl;
#endif
        }
    }
}
```

```csharp
// Assets/Scripts/App/Networking/ApiUrlResolver.cs
public static class ApiUrlResolver
{
    public static string Build(string baseUrl, string route)
    {
        return $"{baseUrl.TrimEnd('/')}/{route.TrimStart('/')}";
    }
}
```

---

## 6) 씬 조립 방식 (Installer 패턴)
- 각 씬에 `SceneInstaller` 하나를 두고 의존성을 조립한다.
- 예: `LobbySceneInstaller`
- `LobbyView`, `LobbyPresenter`, `LobbyCoordinator` 연결
- `ChatRoomApiClient` 생성 시 `AppApiConfig` 주입
- `RoomSessionStore` 참조 주입

이 구조가 필요한 이유:
- 씬별 교체/테스트가 쉬워진다.
- 전역 싱글톤 탐색 코드(`Find/Instance`)를 줄일 수 있다.
- 런타임 Null 참조를 설치 단계에서 빠르게 발견할 수 있다.

---

## 7) 단계별 리팩토링 순서 (안전한 진행)

### Phase 1. Define/Config 추출 (저위험)
- API/Scene 문자열 상수화
- `AppApiConfig` 도입
- 기존 클래스에서 URL/씬 문자열 직접 참조 제거

### Phase 2. Lobby 경계 정리
- `LobbyUIController`를 `View/Presenter/Coordinator`로 분리
- `ChatRoomManager`에 Facade 도입 후 내부 서비스 분해 시작

### Phase 3. NetworkCar 연결부 정리
- `HostNetworkCarCoordinator`가 `ChatRoomManager.Instance` 직접 참조하지 않도록 인터페이스 주입 전환
- `RoomSessionContext` -> `RoomSessionStore` 전환

### Phase 4. 전역 의존 축소
- `AuthManager.Instance` 직접 참조 지점 축소(인터페이스/Provider 도입)
- 테스트 코드/레거시 경로 정리

### Phase 5. 삭제/정리
- 더 이상 쓰지 않는 임시 클래스, 중복 라우트 빌더, 레거시 경로 제거

---

## 8) 완료 기준 (Definition of Done)
- API URL이 코드에 하드코딩되지 않고 `Define + Config`를 통해서만 참조된다.
- 씬 이름 문자열 직접 입력 코드가 제거된다.
- `ChatRoomManager`가 단일 God Class 역할에서 분해된다.
- Lobby 씬에서 UI/흐름/통신 책임이 분리된다.
- `RoomSessionContext` 정적 의존이 제거되거나 호환 계층으로 제한된다.

---

## 9) 바로 적용할 첫 작업 (추천)
1. `App/Defines`에 `AppScenes`, `ApiRoutes` 생성
2. `App/Config/AppApiConfig` ScriptableObject 생성 및 에셋 추가
3. `LobbyUIController`, `LobbyRoomFlow`, `ChatRoomManager`, `AuthManager`에서 하드코딩 URL/씬 참조 교체
4. `ChatRoomManager` 내부를 기능별 Service 파일로 분리 시작 (Create/List/Join/BlockShare)

