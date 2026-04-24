# ReLoginPlan

## 1) 문서 목적
- 로그인 기능은 이미 동작하므로, 이번 목표는 기능 추가가 아니라 **구조 리팩토링**이다.
- 리팩토링 기준은 다음 3가지다.
- API 주소/경로를 `Define`으로 통합한다.
- 클래스는 **하나의 책임**만 갖도록 분해한다.
- 인터페이스를 도입해 교체 가능한 경계를 만든다.

---

## 2) 현재 로그인 구조 진단

### 2-1. 현재 강점
- `AuthManager`가 인증 흐름을 한 곳에서 관리하고 있어 진입점이 명확하다.
- 딥링크, 자동 로그인, 수동 로그인 흐름이 이미 존재한다.
- 로그인 성공 후 토큰 검증까지 거치는 안전한 흐름이 있다.

### 2-2. 현재 문제
- API 주소가 `AuthManager` 내부 필드에 직접 들어가 있어 환경 전환(Dev/Stage/Prod)이 어렵다.
- `AuthManager`가 너무 많은 책임을 가진다.
- 상태 관리
- API 호출 의존성 생성
- 토큰 검증
- 씬 전환
- 이벤트 발행
- 싱글톤 직접 참조(`AuthManager.Instance`)가 많아 테스트/교체가 어렵다.
- 씬 이름 문자열도 흩어져 있어 변경 누락 위험이 있다.

---

## 3) 왜 구조를 바꿔야 하는가 (핵심 이유)

### 3-1. Define 통합이 필요한 이유
- 하드코딩 URL 분산은 운영 실수의 가장 큰 원인이다.
- 주소가 바뀔 때 파일 여러 개를 직접 수정하면 누락이 발생한다.
- 경로/헤더/씬 이름을 공통 상수화하면 변경 지점이 1곳으로 줄어든다.

### 3-2. 단일 책임(SRP)이 필요한 이유
- 큰 클래스는 수정 시 사이드이펙트 범위가 넓다.
- 로그인 UI 변경과 토큰 검증 로직이 같은 클래스에 있으면 회귀 위험이 커진다.
- 책임을 분리하면 문제가 생겼을 때 원인 위치를 빠르게 찾을 수 있다.

### 3-3. 인터페이스 분리가 필요한 이유
- 네트워크 계층을 교체하거나 Mock으로 테스트할 수 있다.
- 딥링크 소스/저장소/씬 네비게이션 정책을 독립적으로 교체할 수 있다.
- 기능은 유지하면서 구현만 바꾸는 리팩토링이 가능해진다.

---

## 4) 리팩토링 목표 아키텍처

```text
Assets/Scripts
  App
    Defines
      AppScenes.cs
      ApiRoutes.cs
      HttpHeaders.cs
    Config
      AppApiConfig.cs
    Networking
      ApiUrlResolver.cs
  Scenes
    Login
      Domain
      Application
      Infrastructure
      Presentation
  Shared
    Auth
      Models
      Contracts
```

---

## 5) Define 설계 (API/Scene 통합)

### 5-1. 파일 설계
- `AppScenes.cs`
- 로그인/로비 씬 이름 상수
- `ApiRoutes.cs`
- 로그인, 토큰 검증 경로 상수
- `HttpHeaders.cs`
- `Authorization`, `Content-Type`, `Accept` 등 공통 헤더 키

### 5-2. 환경 분리
- `AppApiConfig`(`ScriptableObject`)에 `devBaseUrl`, `stageBaseUrl`, `prodBaseUrl`를 둔다.
- 빌드 심볼(`API_ENV_DEV`, `API_ENV_STAGE`, `API_ENV_PROD`)로 현재 환경을 선택한다.
- 최종 URL은 `ApiUrlResolver.Build(baseUrl, route)`로만 만든다.

### 5-3. 이 방식의 효과
- 로그인 API 주소 변경 시 `Define/Config`만 수정하면 된다.
- 씬 이름 변경 시 런타임 문자열 오타를 줄일 수 있다.
- 운영 빌드와 개발 빌드의 주소 혼선을 방지한다.

---

## 6) 클래스 분해 계획 (SRP 적용)

### 6-1. 기존 `AuthManager` 분해 대상
- `AuthBootstrap`
- 시작 시 우선순위 결정(에디터 토큰/딥링크/저장 토큰/수동 로그인)
- `CredentialLoginUseCase`
- ID/PW 로그인 요청 및 결과 처리
- `TokenValidationUseCase`
- access token 검증 전담
- `AuthStateStore`
- 현재 사용자/토큰/인증 상태 보관
- `LoginSceneNavigator`
- 로그인 성공/실패 시 씬 전환만 담당
- `AuthEventBus` 또는 이벤트 포워더
- 성공/실패 이벤트 브로커

### 6-2. 인프라 계층 분해
- `AuthApiClient`는 HTTP 전송/응답 파싱만 담당
- `AuthSessionStore`는 저장소(PlayerPrefs) 책임만 담당
- `AuthTokenReceiver`는 딥링크 토큰 추출만 담당

---

## 7) 인터페이스 설계 (의존성 역전)

### 7-1. 제안 인터페이스
- `IAuthApiClient`
- `Task<LoginResult> LoginWithCredentialsAsync(string userId, string password, CancellationToken ct)`
- `ITokenValidationClient`
- `Task<AuthResult> ValidateTokenAsync(string accessToken, CancellationToken ct)`
- `IAuthSessionStore`
- `Save`, `Load`, `Clear`
- `ITokenSource`
- `TryGetStartupToken(out string accessToken, out string refreshToken)`
- 구현체: `DeepLinkTokenSource`, `EditorTokenSource`, `SavedTokenSource`
- `ISceneNavigator`
- `GoToLogin()`, `GoToLobby()`
- `IAuthState`
- 현재 사용자/토큰/인증 여부 접근

### 7-2. 인터페이스를 쓰는 이유
- 단위 테스트에서 실제 네트워크 호출 없이 로그인 흐름 검증 가능
- UI와 인증 로직을 느슨하게 결합할 수 있음
- 향후 서버 SDK 교체 시 상위 로직 수정 없이 구현체만 교체 가능

---

## 8) 로그인 흐름 재구성

### 8-1. 시작 시 인증 흐름
1. `AuthBootstrap.Initialize()`
2. `ITokenSource` 체인 순서대로 토큰 탐색
3. 토큰이 있으면 `TokenValidationUseCase` 실행
4. 성공 시 상태 저장 후 `ISceneNavigator.GoToLobby()`
5. 실패 시 로그인 화면 유지

### 8-2. 수동 로그인 흐름
1. `LoginView`가 `userId/password` 제출
2. `CredentialLoginUseCase`가 `IAuthApiClient` 호출
3. access token 획득 후 `ITokenValidationClient` 검증
4. 성공 시 `IAuthSessionStore.Save()` + `GoToLobby()`
5. 실패 시 오류 코드 매핑 메시지 반환

---

## 9) 파일 단위 적용 계획

### Phase 1. Define/Config 도입
- `AppScenes`, `ApiRoutes`, `HttpHeaders`, `AppApiConfig`, `ApiUrlResolver` 추가
- 기존 `AuthManager` 하드코딩 URL/씬 이름 참조를 Define로 교체

### Phase 2. 인터페이스 도입
- `IAuthApiClient`, `ITokenValidationClient`, `IAuthSessionStore`, `ISceneNavigator` 추가
- `AuthManager` 내부 직접 생성(`new`) 코드 제거, 주입 방식으로 변경

### Phase 3. AuthManager 슬림화
- 오케스트레이션만 남기고 네트워크/저장/씬 이동 로직 분리
- `UseCase` 클래스로 이동

### Phase 4. 호출부 정리
- `AuthManualTokenFallbackUI`에서 `AuthManager.Instance` 직접 의존 축소
- 가능한 범위에서 인터페이스 기반 호출로 변경

### Phase 5. 테스트/정리
- 회귀 테스트 후 레거시 경로 삭제

---

## 10) 완료 기준 (Definition of Done)
- 로그인/검증 API 주소가 클래스 내부 문자열이 아니라 Define+Config로만 관리된다.
- 인증 관련 핵심 클래스가 단일 책임을 갖는다.
- 네트워크/저장/씬 전환이 인터페이스 경계로 분리된다.
- 기존 로그인 기능(딥링크, 자동 로그인, 수동 로그인)은 동작을 유지한다.
- 씬 전환 문자열 하드코딩이 제거된다.

---

## 11) 리스크와 대응
- 리스크: 의존성 주입 전환 중 초기화 순서 문제
- 대응: `LoginSceneInstaller`를 두고 생성 순서 고정
- 리스크: 기존 싱글톤 기반 코드와 충돌
- 대응: 단계적으로 어댑터를 두고 점진 전환
- 리스크: 테스트 부족으로 회귀 발생
- 대응: 로그인 성공/실패/토큰만료/네트워크오류 시나리오를 우선 자동화

---

## 12) 즉시 실행 권장 순서
1. `App/Defines`와 `App/Config` 먼저 만든다.
2. `AuthManager`의 URL/Scene 문자열을 Define 참조로 교체한다.
3. `IAuthApiClient`, `IAuthSessionStore`, `ISceneNavigator` 인터페이스를 먼저 도입한다.
4. `AuthManager`에서 네트워크/저장/씬 이동 책임을 분리한다.
5. 마지막에 `AuthManualTokenFallbackUI` 의존성을 정리한다.


---

## 13) 세분화 실행 순서 (문제 최소화 버전)

### 13-1. 작업 원칙
- 한 번에 하나의 책임만 변경한다.
- 한 Task가 끝나기 전에는 다음 Task를 시작하지 않는다.
- 권장 단위는 `1 Task = 1 Commit`이다.
- 각 Task 종료 시 최소 스모크 테스트를 수행한다.
- 테스트 실패 시 바로 직전 Task 상태로 되돌린다.

### 13-2. 전체 순서도
1. `T0` 기준선 고정(백업/브랜치/동작 확인)
2. `T1` Define/Config 파일만 추가
3. `T2` 기존 Auth 코드에서 URL/Scene 참조만 교체
4. `T3` 인터페이스 껍데기만 추가(아직 로직 이동 금지)
5. `T4` AuthManager의 네트워크 생성 책임 분리
6. `T5` AuthManager의 저장소 책임 분리
7. `T6` AuthManager의 씬 이동 책임 분리
8. `T7` UseCase로 흐름 분리(로그인/검증)
9. `T8` UI 호출부(AuthManualTokenFallbackUI) 의존 정리
10. `T9` 레거시 경로 정리 + 회귀 테스트

---

## 14) Task 단위 상세 실행 계획

### T0. 기준선 고정
- 작업: 현재 동작을 기준선으로 확정한다.
- 변경 파일: 없음
- 확인 항목: 수동 로그인 성공, 실패 메시지, 토큰 검증 후 `01_Lobby` 이동
- 완료 기준: 기준선 체크리스트 결과를 텍스트로 남긴다.
- 롤백 포인트: 현재 브랜치 HEAD

### T1. Define/Config 추가 (코드 연결 없음)
- 작업: `AppScenes`, `ApiRoutes`, `HttpHeaders`, `AppApiConfig`, `ApiUrlResolver` 파일만 생성한다.
- 변경 파일: 신규 파일만
- 확인 항목: 컴파일 에러 0
- 완료 기준: 기존 로그인 동작 100% 동일
- 롤백 포인트: `T1` 커밋

### T2. 문자열 참조 교체 (AuthManager 내부)
- 작업: `AuthManager`의 `_loginEndpoint`, `_tokenValidationUrl`, 씬 이름 직접 문자열 참조를 Define/Config 참조로 치환한다.
- 변경 파일: `AuthManager.cs` 중심
- 확인 항목: 로그인/검증/씬 이동 동작 동일
- 완료 기준: AuthManager 내부 하드코딩 URL 제거
- 롤백 포인트: `T2` 커밋

### T3. 인터페이스 스켈레톤 도입
- 작업: `IAuthApiClient`, `ITokenValidationClient`, `IAuthSessionStore`, `ISceneNavigator`를 추가하고 기존 구현체에 연결만 한다.
- 변경 파일: `Contracts` + 기존 구현체 일부
- 확인 항목: 컴파일 에러 0, 런타임 동작 동일
- 완료 기준: 코드가 인터페이스 타입을 받을 수 있는 상태
- 롤백 포인트: `T3` 커밋

### T4. 네트워크 책임 분리
- 작업: AuthManager 내부 `new AuthApiClient(...)` 생성과 HTTP 의존 로직을 외부 주입/팩토리로 분리한다.
- 변경 파일: `AuthManager.cs`, `AuthApiClient.cs`(필요 시)
- 확인 항목: ID/PW 로그인 성공/실패 분기 정상
- 완료 기준: AuthManager가 직접 네트워크 구현 생성하지 않음
- 롤백 포인트: `T4` 커밋

### T5. 세션 저장 책임 분리
- 작업: 토큰 저장/로드/클리어는 `IAuthSessionStore` 경계로만 접근하게 정리한다.
- 변경 파일: `AuthManager.cs`, `AuthSessionStore.cs`
- 확인 항목: 자동 로그인 성공, 로그아웃 후 세션 제거 정상
- 완료 기준: AuthManager에서 PlayerPrefs 직접 접근 제거
- 롤백 포인트: `T5` 커밋

### T6. 씬 전환 책임 분리
- 작업: `SceneManager.LoadScene` 직접 호출을 `ISceneNavigator`로 옮긴다.
- 변경 파일: `AuthManager.cs`, `LoginSceneNavigator.cs`(신규)
- 확인 항목: 성공 시 Lobby, 실패 시 Login 유지 동작 동일
- 완료 기준: AuthManager에서 SceneManager 직접 호출 제거
- 롤백 포인트: `T6` 커밋

### T7. UseCase 분리
- 작업: `CredentialLoginUseCase`, `TokenValidationUseCase`, `AuthBootstrap`로 흐름을 분리한다.
- 변경 파일: 신규 UseCase + AuthManager 축소
- 확인 항목: 딥링크/자동/수동 로그인 전 경로 회귀 테스트
- 완료 기준: AuthManager는 오케스트레이션만 담당
- 롤백 포인트: `T7` 커밋

### T8. UI 호출부 의존 정리
- 작업: `AuthManualTokenFallbackUI`가 구체 싱글톤(`AuthManager.Instance`) 의존을 줄이고 계약 기반으로 접근하도록 조정한다.
- 변경 파일: `AuthManualTokenFallbackUI.cs`
- 확인 항목: 버튼 입력/오류 표시/중복 클릭 방지 정상
- 완료 기준: UI 계층과 인증 구현 결합도 감소
- 롤백 포인트: `T8` 커밋

### T9. 정리 및 안정화
- 작업: 미사용 코드 삭제, 주석 정리, 문서 갱신
- 변경 파일: 관련 전반
- 확인 항목: 전체 로그인 회귀 테스트 + 콘솔 에러 0
- 완료 기준: ReLoginPlan의 DoD 충족
- 롤백 포인트: `T9` 커밋

---

## 15) 권장 테스트 게이트 (각 Task 종료 시)
1. 컴파일 에러/경고 급증 여부 확인
2. `00_Login` 진입 확인
3. 잘못된 계정 로그인 실패 메시지 확인
4. 올바른 계정 로그인 + 토큰 검증 + `01_Lobby` 이동 확인
5. 자동 로그인/딥링크 경로 중 현재 활성 옵션 최소 1개 확인

---

## 16) 실제 작업 시작 추천
1. 바로 `T1`부터 시작한다. (가장 안전하고 영향도 낮음)
2. `T2` 완료 전에는 인터페이스 분리(T3 이상)로 넘어가지 않는다.
3. `T4~T7`은 반드시 순서대로 진행한다. (의존성 방향이 맞아야 회귀가 줄어듦)
4. `T8`은 마지막에 수행한다. (UI는 가장 마지막에 얇게 정리)

---

## 17) 현재 코드 기준 진행 상태 업데이트 (2026-04-14)

- 실제 저장소 상태는 위 계획 순서와 100% 일치하지 않는다.
- 현재 코드는 `T3` 인터페이스 도입 전에 `T4`, `T5` 성격의 일부 분리가 먼저 반영되어 있다.
- 아래 상태 표시는 "계획상 순서"가 아니라 "현재 코드에 실제로 반영된 정도" 기준으로 기록한다.

### 17-1. Task 진행 요약

- `T0`: 미기록
- 기준선 체크리스트(수동 로그인 성공/실패, 토큰 검증 후 로비 이동)를 문서에 남긴 기록은 아직 없다.

- `T1`: 완료
- 반영 파일
- `Assets/Scripts/App/Defines/AppScenes.cs`
- `Assets/Scripts/App/Defines/ApiRoutes.cs`
- `Assets/Scripts/App/Defines/HttpHeaders.cs`
- `Assets/Scripts/App/Config/AppApiConfig.cs`
- `Assets/Scripts/App/Networking/ApiUrlResolver.cs`
- 반영 내용
- 로그인/검증 경로, 씬 이름, 공통 헤더 키를 Define로 모았다.
- 환경별 base URL과 timeout을 `AppApiConfig`로 분리했다.
- 최종 URL 조합을 `ApiUrlResolver.Build(...)` 경로로 통일했다.

- `T2`: 완료
- 반영 파일
- `Assets/Scripts/Auth/AuthManager.cs`
- 반영 내용
- `_apiConfig`, `_baseUrlOverride`, `_loginRoute`, `_tokenValidationRoute` 필드가 들어갔다.
- `ResolveLoginEndpoint()`, `ResolveTokenValidationEndpoint()`, `ResolveLoginSceneName()`, `ResolveLobbySceneName()`가 추가되었다.
- `AuthManager` 내부 하드코딩 URL/씬 문자열을 Define + Config 조합 방식으로 바꿨다.

- `T3`: 미착수
- 현재 상태
- `IAuthApiClient`, `ITokenValidationClient`, `IAuthSessionStore`, `ISceneNavigator` 같은 인터페이스 파일이 아직 없다.
- 구현 교체 경계와 주입 구조는 아직 만들어지지 않았다.

- `T4`: 부분 완료
- 반영 파일
- `Assets/Scripts/Auth/AuthApiClient.cs`
- `Assets/Scripts/Auth/AuthManager.cs`
- 반영 내용
- 로그인 HTTP 요청/응답 파싱 책임은 `AuthApiClient`로 분리되어 있다.
- 다만 `AuthManager`가 `EnsureApiClient()` 내부에서 직접 생성하고 있어 DI/주입 구조는 아직 아니다.

- `T5`: 부분 완료
- 반영 파일
- `Assets/Scripts/Auth/AuthSessionStore.cs`
- `Assets/Scripts/Auth/AuthManager.cs`
- 반영 내용
- PlayerPrefs 저장/조회/삭제 책임은 `AuthSessionStore`로 분리되어 있다.
- 다만 정적 호출 방식이므로 인터페이스 경계 분리까지는 아직 진행되지 않았다.

- `T6`: 미착수
- 현재 상태
- 씬 이동은 아직 `AuthManager` 내부에서 `SceneManager.LoadScene(...)`로 직접 처리한다.

- `T7`: 미착수
- 현재 상태
- `CredentialLoginUseCase`, `TokenValidationUseCase`, `AuthBootstrap`, `LoginSceneNavigator` 등 목표 클래스는 아직 없다.
- 로그인/검증 오케스트레이션은 여전히 `AuthManager`가 들고 있다.

- `T8`: 진행 1차 완료
- 반영 파일
- `Assets/Scripts/Auth/AuthManualTokenFallbackUI.cs`
- 반영 내용
- 기존 `OnGUI()` 기반 IMGUI 로그인 입력 UI를 제거했다.
- 로그인 씬에 이미 배치된 UGUI/TMP 오브젝트를 런타임에서 찾아 바인딩하도록 변경했다.
- 우선 연결 대상은 `Panel Login`, `InputField ID`, `InputField Passward`, `ButConfirm`, `ButCancel`이다.
- 새 버튼이 없을 경우 `Login Button`, `Exit Button `도 fallback 대상으로 찾도록 했다.
- 비밀번호 입력 필드는 런타임에서 `TMP_InputField.ContentType.Password`로 강제해 씬 설정 누락을 보정한다.
- 상태 메시지용 `TMP_Text`가 씬에 없으면 `Status Text`를 런타임에서 생성한다.
- 로그인 진행 중에는 입력창/버튼을 비활성화하고 로딩 패널을 사용할 수 있도록 했다.
- 씬 전환 시 기존 이벤트를 해제하고 로그인 씬 UI를 다시 찾아 재바인딩하도록 정리했다.
- 남은 항목
- `AuthManager.Instance` 직접 의존은 아직 남아 있다.
- 따라서 `T8`은 "GUI -> UI 전환"은 완료했지만 "인터페이스 기반 호출부 정리"까지 완료된 상태는 아니다.

- `T9`: 미착수
- 현재 상태
- 레거시 경로 삭제, 클래스명 정리, 전체 회귀 테스트는 아직 남아 있다.

### 17-2. 이번 작업 상세 (2026-04-14)

- 변경 파일
- `Assets/Scripts/Auth/AuthManualTokenFallbackUI.cs`

- 이번에 실제로 한 일
- `RuntimeInitializeOnLoadMethod` 기반 자동 생성 방식은 유지했다.
- 즉, 씬에 별도 스크립트 연결이 없어도 기존처럼 런타임에서 로그인 UI 컨트롤러가 살아난다.
- `OnGUI`, `GUI.TextField`, `GUI.PasswordField`, `GUI.Button`, `GUIStyle` 기반 코드를 전부 제거했다.
- 현재 씬 루트를 순회하면서 이름 기준으로 UI 오브젝트를 찾는 헬퍼를 추가했다.
- 찾은 UI에 `onClick`, `onValueChanged`, `onSubmit` 이벤트를 연결했다.
- 입력값이 바뀌면 이전 오류 메시지를 지우도록 처리했다.
- 로그인 시작 시 `Logging in...` 메시지를 표시하고, 완료 시 UI 상태를 다시 복구하도록 했다.
- 로그인 실패 시 `AuthErrorMapper`가 반환한 사용자 메시지를 상태 텍스트에 노출하도록 했다.
- 테스트 인증 경로(`TestAuthManager`, `00_TestLogin`)는 기존과 동일하게 본 바인더 생성 대상에서 제외했다.

### 17-3. 검증 결과

- 실행 명령
- `dotnet build Assembly-CSharp.csproj -nologo`

- 결과
- `0 Error`
- `4 Warning`
- 이번 변경으로 새 컴파일 에러는 발생하지 않았다.
- 경고는 기존 프로젝트의 다른 파일에서 발생한 기존 경고다.
- `Assets/Scripts/Map/CreateMap.cs`
- `Assets/Scripts/Auth/Test/TestAuthManager.cs`
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorGUI.cs`

## 18) 다음 작업 권장 순서 (현재 상태 기준)

1. `T3` 인터페이스 추가
2. `IAuthApiClient`, `IAuthSessionStore`, `ISceneNavigator`부터 최소 단위로 도입한다.
3. `T6` 씬 이동 분리
4. `AuthManager` 내부 `SceneManager.LoadScene(...)`를 `LoginSceneNavigator`로 이동한다.
5. `T7` UseCase 분리
6. `LoginWithCredentialsAsync`와 `ValidateTokenWithServer(...)`를 UseCase/Client 경계로 이동한다.
7. `T8` 2차 정리
8. 현재 UI 바인더에서 `AuthManager.Instance` 직접 참조를 제거하고 인터페이스 기반 호출 구조로 바꾼다.
9. `T9` 레거시 정리
10. 이름이 혼동되는 `AuthManualTokenFallbackUI` 클래스명/파일명을 정리할지 검토하고, 전체 로그인 회귀 테스트를 수행한다.
