# Auth 함수 상세 설명서

이 문서는 `Assets/Scripts/Auth` 폴더(하위 `Test`, `Models` 포함)의 **함수별 역할**을 한글로 정리한 문서입니다.

## 1) AuthManager.cs

### Awake()
- 역할: `AuthManager` 싱글톤을 초기화합니다.
- 주요 동작: 중복 인스턴스가 있으면 현재 오브젝트를 제거하고, 최초 인스턴스면 `DontDestroyOnLoad`로 유지합니다.
- 추가 처리: API 클라이언트 준비(`EnsureApiClient`)와 딥링크 토큰 이벤트 구독(`AuthTokenReceiver.OnTokenReceived`)을 수행합니다.

### Start()
- 역할: 앱 시작 직후 인증 시작 경로를 선택합니다.
- 우선순위:
  1. 에디터 테스트 토큰 인증
  2. 딥링크 토큰 인증
  3. 저장 토큰 자동 로그인
- 특징: 테스트/딥링크 인증은 실패 시 사용자 에러 팝업 대신 경고 로그 중심으로 동작합니다.

### OnDestroy()
- 역할: 이벤트 구독 해제.
- 주요 동작: 현재 인스턴스가 싱글톤일 때만 `OnTokenReceived` 핸들러를 해제하여 메모리 누수/중복 호출을 방지합니다.

### LoginWithCredentialsAsync(string id, string password)
- 역할: ID/PW 로그인 전체 흐름의 진입점.
- 입력: 사용자 ID, 비밀번호.
- 출력: `LoginResult`.
- 상세 동작:
  - 중복 요청 여부 검사
  - 입력값 유효성 검사
  - `AuthApiClient`를 통해 로그인 API 호출
  - 성공 시 받은 토큰으로 `AuthenticateWithTokenAsync` 수행
- 실패 처리: 사용자 메시지(`OnLoginFailed`)와 내부 상태를 함께 갱신합니다.

### AuthenticateWithToken(string accessToken, string refreshToken = null)
- 역할: 토큰 인증의 간편 호출용 래퍼(`async void`).
- 주요 동작: 내부적으로 `AuthenticateWithTokenAsync`를 호출합니다.

### AuthenticateWithTokenAsync(string accessToken, string refreshToken = null, bool suppressFailureFeedback = false)
- 역할: 토큰 검증 기반 최종 인증 처리의 핵심 함수.
- 입력: access token, refresh token, 실패 피드백 억제 여부.
- 출력: 인증 성공 여부(`bool`).
- 상세 동작:
  - 토큰/중복 요청 검증
  - 서버 검증(`ValidateTokenWithServer`)
  - 성공 시 사용자 상태 저장 + 로컬 토큰 저장 + 성공 이벤트 + 게임 씬 이동
  - 실패 시 인증 상태 초기화 + 세션 스토어 삭제 + (옵션) 실패 이벤트/로그인 씬 이동

### TryAutoLogin()
- 역할: 로컬 저장 토큰으로 자동 로그인 시도.
- 주요 동작: `AuthSessionStore`에서 토큰을 읽고 있으면 조용히(`suppressFailureFeedback=true`) 재검증합니다.

### TryAuthenticateDeepLinkTokenAtStartupAsync()
- 역할: 앱 시작 시점에 이미 수신된 딥링크 토큰을 1회 처리.
- 출력: 딥링크 인증 성공 여부.
- 주요 동작: `_useDeepLinkLogin` 설정, `AuthTokenReceiver.Instance`, 토큰 존재 여부를 확인한 뒤 인증을 시도합니다.

### OnDeepLinkTokenReceived(string accessToken, string refreshToken)
- 역할: 런타임 중 새로 수신된 딥링크 토큰 처리.
- 주요 동작: 인증 중복 상태를 피하고, 딥링크 토큰으로 인증 시도 후 실패 시 경고 로그를 남깁니다.

### ValidateTokenWithServer(string token)
- 역할: 토큰 검증 API 호출.
- 입력: access token.
- 출력: `AuthResult`.
- 주요 동작:
  - GET 요청 생성
  - `Authorization: Bearer <token>` 헤더 설정
  - 응답 성공 시 본문 파싱
  - 실패 시 HTTP/네트워크 상태를 내부 에러 코드로 매핑

### ParseValidationResponse(string responseBody)
- 역할: 토큰 검증 응답 JSON 파싱.
- 출력: 성공/실패가 반영된 `AuthResult`.
- 파싱 전략:
  1. `AuthResponse` 래퍼 구조 파싱 시도
  2. 실패하면 `UserInfo` 직접 파싱 시도
  3. 둘 다 실패하면 `UnknownError`

### MapTokenValidationErrorCode(UnityWebRequest request)
- 역할: UnityWebRequest 상태를 내부 인증 에러 코드로 변환.
- 매핑 예시:
  - 네트워크/데이터 처리 오류 -> `NETWORK_ERROR`
  - 401 -> `TOKEN_EXPIRED_OR_INVALID`
  - 500 -> `INTERNAL_ERROR`

### SaveTokenLocally(string accessToken, string refreshToken)
- 역할: 토큰 저장을 `AuthSessionStore`로 위임.

### LoadLoginSceneIfNeeded()
- 역할: 현재 씬이 로그인 씬이 아니면 로그인 씬으로 이동.
- 목적: 인증 실패/로그아웃 시 항상 로그인 진입점 복구.

### EnsureApiClient()
- 역할: `AuthApiClient` 지연 생성(필요할 때 1회 생성).

### GetAccessToken()
- 역할: 메모리에 보관 중인 현재 access token 반환.

### Logout()
- 역할: 인증 상태 완전 초기화 + 저장 토큰 삭제 + 로그인 씬 복귀.

---

## 2) AuthTokenReceiver.cs

### Awake()
- 역할: 딥링크 수신기 초기화.
- 주요 동작:
  - 딥링크 기능 비활성 시 즉시 종료
  - 싱글톤 보장
  - 프로토콜 등록(`ProtocolRegistrar.RegisterProtocol`)
  - 커맨드라인 파싱 수행

### HasTokenInCommandLine()
- 역할: 실행 인자 중 `rccar://...` 형태가 포함되는지 확인.

### ProcessCommandLineArgs()
- 역할: 커맨드라인에서 딥링크 URL을 찾아 파싱.
- 특징: `_hasProcessedCommandLineArgs`로 1회 처리만 허용.

### ParseProtocolUrl(string url)
- 역할: 프로토콜 URL 쿼리에서 `token`, `refresh`를 추출.
- 주요 동작:
  - URL 파싱
  - 쿼리 문자열 key/value 딕셔너리화
  - access token이 유효하면 `OnTokenReceived` 이벤트 발행

### GetAccessToken()
- 역할: 내부에 저장된 access token 반환.

### GetRefreshToken()
- 역할: 내부에 저장된 refresh token 반환.

---

## 3) AuthApiClient.cs

### AuthApiClient(string loginUrl, int timeoutSeconds = 15)
- 역할: 로그인 API 클라이언트 생성자.
- 주요 동작: URL/타임아웃을 안전한 값으로 초기화.

### LoginWithIdPasswordAsync(string id, string password)
- 역할: ID/PW 로그인 API 요청 실행.
- 출력: `LoginResult`.
- 주요 동작:
  - 빈 입력 사전 검증
  - JSON 직렬화 후 POST 전송
  - 응답을 `ParseLoginResult`로 해석

### ParseLoginResult(UnityWebRequest request)
- 역할: 로그인 응답을 `LoginResult`로 변환.
- 동작 포인트:
  - HTTP 성공 + JSON success + accessToken 존재 시 로그인 성공 반환
  - 그 외에는 에러 코드/메시지/재시도 가능 여부 계산

### TryParseResponse(string responseBody)
- 역할: 응답 문자열을 `LoginResponse`로 파싱 시도.
- 실패 시 `null` 반환.

### ResolveErrorCode(UnityWebRequest request, LoginResponse parsedResponse)
- 역할: 에러 코드 우선순위를 결정.
- 우선순위:
  1. 네트워크/데이터 오류
  2. 서버가 내려준 `errorCode`
  3. HTTP 상태코드 기반 기본 매핑

### CreateFailedResult(string errorCode, long statusCode, string fallbackMessage, bool retryable)
- 역할: 공통 실패 결과 객체 생성 헬퍼.

---

## 4) AuthSessionStore.cs

### Save(string accessToken, string refreshToken)
- 역할: 토큰을 PlayerPrefs에 저장.
- 특징: refresh token이 비어 있으면 refresh 키를 삭제합니다.

### GetAccessToken()
- 역할: 저장된 access token 조회.

### GetRefreshToken()
- 역할: 저장된 refresh token 조회.

### Clear()
- 역할: 저장된 access/refresh token 완전 삭제.

---

## 5) AuthErrorMapper.cs

### ToUserMessage(string errorCode, string fallbackMessage = null)
- 역할: 내부/서버 에러 코드를 사용자 친화 메시지로 변환.
- 특징: 코드가 없으면 `UNKNOWN_ERROR`로 처리하고, fallback 메시지를 보조로 사용합니다.

### FromStatusCode(long statusCode)
- 역할: HTTP 상태코드를 내부 인증 에러 코드로 변환.

---

## 6) AuthManualTokenFallbackUI.cs

### Bootstrap()
- 역할: 런타임 시작 후 로그인 UI 오브젝트를 자동 생성.
- 조건: 테스트 플로우가 아닐 때만 동작.

### ShouldSkipForTestFlow()
- 역할: 현재 실행이 테스트 인증 플로우인지 판별.

### OnEnable()
- 역할: 씬 로드 이벤트 구독.

### OnDisable()
- 역할: 씬 로드 이벤트 구독 해제.

### OnSceneLoaded(Scene scene, LoadSceneMode mode)
- 역할: 씬 전환 시 UI 상태(로딩 플래그/상태 메시지) 초기화.

### OnGUI()
- 역할: ID/PW 로그인 패널 렌더링 및 버튼 이벤트 처리.

### ShouldShow()
- 역할: 현재 프레임에서 로그인 UI를 보여줄지 판단.
- 기준: 로그인 씬 여부, AuthManager 존재 여부, 이미 인증되었는지 여부.

### SubmitLogin()
- 역할: UI 입력값으로 로그인 요청을 수행.
- 흐름:
  - 입력 검증
  - 인증 중복 검사
  - `AuthManager.LoginWithCredentialsAsync` 호출
  - 실패 메시지 반영

### QuitApp()
- 역할: 앱 종료(에디터에서는 Play 모드 종료).

### EnsureStyles()
- 역할: IMGUI 스타일 1회 생성/캐싱.

---

## 7) ProtocolRegistrar.cs

### RegisterProtocol()
- 역할: Windows 레지스트리에 `rccar://` URL 스킴 등록.
- 효과: 브라우저에서 `rccar://...` 클릭 시 Unity 앱 실행 가능.

### UnregisterProtocol()
- 역할: 등록된 URL 스킴 제거.

### GetCurrentUserRegistryKey()
- 역할: 런타임 반사(Reflection)로 HKCU 루트 키 핸들 획득.

### CreateSubKey(object parentKey, string subKeyPath)
- 역할: 레지스트리 하위 키 생성.

### SetRegistryValue(object key, string name, string value)
- 역할: 레지스트리 값 쓰기.

### DeleteSubKeyTree(object parentKey, string subKeyPath)
- 역할: 레지스트리 하위 트리 삭제(런타임 메서드 시그니처 차이 대응).

---

## 8) Test/TestAuthManager.cs

### Awake()
- 역할: 테스트 인증 매니저 싱글톤 초기화.

### Start()
- 역할: 테스트 모드 시작 처리.
- 현재 구현 상태: 테스트 토큰/자동로그인 호출 코드가 주석 처리되어 실사용 흐름에서는 직접 인증 호출 중심.

### TryAutoLogin()
- 역할: PlayerPrefs 저장 토큰 기반 자동 로그인 시도(테스트용).

### AuthenticateWithToken(string accessToken, string refreshToken = null)
- 역할: 테스트용 토큰 인증 래퍼.

### AuthenticateWithTokenAsync(string accessToken, string refreshToken = null)
- 역할: 테스트용 토큰 인증 핵심 처리.
- 동작: 서버 검증 -> 성공 시 씬 전환/저장, 실패 시 로그인 씬 복귀.

### ValidateTokenWithServer(string token)
- 역할: 테스트 서버로 토큰 검증 요청.
- 특징: 성공 시 응답 JSON을 `UserInfo`로 직접 파싱.

### SaveTokenLocally(string accessToken, string refreshToken)
- 역할: 테스트용 토큰 저장(PlayerPrefs).

### GetAccessToken()
- 역할: 현재 테스트 매니저의 access token 반환.

### Logout()
- 역할: 테스트 인증 상태/저장 토큰 초기화 + 로그인 씬 이동.

---

## 9) Test/TestAuthManualTokenFallbackUI.cs

### Bootstrap()
- 역할: 테스트 로그인 씬용 수동 토큰 입력 UI를 런타임에 자동 생성.

### ShouldSkipForPrimaryFlow()
- 역할: 메인 인증 플로우(AuthManager 사용)일 때 테스트 UI 생성을 건너뜀.

### OnEnable()
- 역할: 씬 이벤트 구독 및 최초 로드 시간 기록.

### OnDisable()
- 역할: 씬 이벤트 구독 해제.

### OnSceneLoaded(Scene scene, LoadSceneMode mode)
- 역할: 씬 전환 시 UI 상태와 지연 표시 타이머를 초기화.

### OnGUI()
- 역할: Access/Refresh 토큰 입력 UI 렌더링 + 버튼 처리.

### ShouldShow()
- 역할: 테스트 토큰 수동 입력 UI 표시 여부 판단.
- 상세 조건:
  - 테스트 로그인 씬인지
  - 이미 인증/인증중인지
  - 딥링크 자동 로그인 유예시간이 지났는지
  - 이미 토큰이 수신되었는지

### SubmitToken()
- 역할: 수동 입력 토큰으로 테스트 인증 실행.

### QuitApp()
- 역할: 앱 종료(에디터는 Play 모드 종료).

### EnsureStyles()
- 역할: 테스트 수동 토큰 UI의 IMGUI 스타일 초기화.

---

## 10) Models/AuthModels.cs
- 이 파일은 데이터 모델(`AuthResponse`, `LoginRequest`, `LoginResponse`, `UserInfo`, `AuthResult`, `LoginResult`) 정의 전용입니다.
- 함수(메서드)는 포함되어 있지 않습니다.
