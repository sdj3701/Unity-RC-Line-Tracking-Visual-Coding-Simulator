# Login Plan

## 1) 배경 및 목표
- 웹 개발자 기준 로그인 API는 `http://ioteacher.com/api/auth/login` 이다.
- 요청 방식은 `POST + raw/json` 이고 요청 본문 키는 `userId`, `password` 이다.
- 앱에서는 `AuthManager`를 인증 오케스트레이터로 유지하면서, ID/PW 로그인 계약을 웹 API와 정확히 맞춘다.
- 딥링크/자동로그인 우선순위 구조는 유지하고, ID/PW 경로만 안정적으로 보강한다.

---

## 2) 2026-03-19 현재 코드 분석 결과

### 2-1. 강점 (유지할 구조)
- `AuthManager`가 인증 흐름을 일원화하고 있음.
- 시작 우선순위(에디터 테스트 -> 딥링크 -> 저장 토큰 -> 수동 로그인)가 이미 구현되어 있음.
- 수동 로그인 UI(`AuthManualTokenFallbackUI`)는 `AuthManager.LoginWithCredentialsAsync(...)`로 진입하고 있어 책임 분리가 좋음.
- 로그인 성공 후 토큰 검증(`/api/users/me-by-token`)까지 거쳐서 최종 인증 처리하는 구조가 안전함.

### 2-2. 핵심 불일치/리스크
- `LoginRequest`가 현재 `id` 필드를 사용하고 있음 (`userId` 아님).
- `AuthApiClient` 성공 판정이 `success == true && accessToken 존재`에 의존하여 응답 포맷 변경 시 실패 가능성이 큼.
- `AuthManager` 파라미터/로컬 변수명이 `id` 중심이라 API 계약(`userId`) 관점에서 혼동 가능.
- 서버 응답이 변형될 경우(예: `token` 키 사용) 실패로 처리될 위험이 있음.

### 2-3. 참고 관찰
- 로컬에서 동일 엔드포인트 직접 호출 시 `HttpMessageNotReadableException`(HTTP 500)이 관찰되었음.
- 따라서 실제 적용 전/중에 백엔드와 JSON 계약(요청/응답 샘플)을 최종 확정해야 함.

---

## 3) 수정 원칙
- 인증 진입점은 계속 `AuthManager` 하나로 유지한다.
- ID/PW 요청 모델은 백엔드 계약(`userId`, `password`)을 우선으로 맞춘다.
- 응답 파서는 단일 형식만 가정하지 않고, 토큰 필드명 변형에 대응 가능한 형태로 보강한다.
- 토큰/비밀번호는 로그에 남기지 않는다(마스킹 또는 미출력).
- 딥링크/자동로그인 동작을 깨지 않는 범위에서 변경한다.

---

## 4) 수정 범위

### 포함 (이번 작업)
- `Assets/Scripts/Auth/Models/AuthModels.cs`
- `Assets/Scripts/Auth/AuthApiClient.cs`
- `Assets/Scripts/Auth/AuthManager.cs`
- `Assets/Scripts/Auth/AuthManualTokenFallbackUI.cs` (변수명 정리 수준)
- `docs/documents/auth/AuthFunctionGuide.md` (문서 동기화)

### 제외 (이번 작업 아님)
- 토큰 refresh API 신규 구현
- 암호화 저장소 전환
- 테스트 전용 `TestAuthManager` 계열 대규모 리팩터링

---

## 5) 상세 실행 계획 (파일별)

### 5-1. AuthModels.cs
- `LoginRequest`를 `userId`, `password` 계약에 맞춘다.
- `LoginResponse`는 현재 필드 유지 + 토큰 키 변형 가능성 대응 필드를 추가 검토한다.
- 모델 변경 후 `JsonUtility` 직렬화/역직렬화 영향 범위를 확인한다.

### 5-2. AuthApiClient.cs
- `LoginWithIdPasswordAsync(string id, string password)`의 내부 처리 기준을 `userId`로 정리한다.
- 요청 JSON이 정확히 `{"userId":"...","password":"..."}`로 생성되도록 고정한다.
- 성공 판정을 아래 기준으로 보강한다.
  - HTTP 성공
  - 토큰 문자열 존재 (`accessToken` 우선, 필요시 대체 키 fallback)
- 실패 판정을 아래 우선순위로 정리한다.
  - 네트워크/데이터 처리 오류
  - 서버 `errorCode`/`message`
  - HTTP status code 매핑
- 사용자 메시지는 기존 `AuthErrorMapper` 체계를 재사용한다.

### 5-3. AuthManager.cs
- `LoginWithCredentialsAsync` 파라미터/로컬명 의미를 `userId` 기준으로 정리한다.
- 로그인 API 성공인데 토큰이 비어 있는 경우를 명시적으로 실패 처리한다.
- 실패 메시지 전달(`LastAuthErrorMessage`, `OnLoginFailed`) 규칙을 현재 구조 안에서 일관되게 유지한다.
- 인증 성공 시 기존과 동일하게:
  - 토큰 저장
  - 사용자 상태 반영
  - 성공 이벤트 발행
  - 게임 씬 이동
- 딥링크/자동로그인 우선순위 로직은 변경하지 않는다.

### 5-4. AuthManualTokenFallbackUI.cs
- 입력 변수명 `id`를 `userId` 의미로 정리해 코드 가독성을 맞춘다.
- 호출 흐름은 계속 `AuthManager.LoginWithCredentialsAsync(...)`를 사용한다.

### 5-5. AuthFunctionGuide.md
- 요청 모델 키 변경(`id` -> `userId`) 및 파싱 전략 변경 내용을 반영한다.
- 팀원이 문서만 보고도 현재 계약을 이해할 수 있게 예시 JSON을 추가한다.

---

## 6) 인증 시작점 우선순위 (유지)
1. Editor Test Token (옵션)
2. Deep Link Token (옵션, 시작 시 1회)
3. Saved Token Auto Login (옵션)
4. Manual ID/PW Login (사용자 버튼 클릭)

정책:
- 상위 단계 성공 시 하위 단계는 실행하지 않는다.
- 실패 시 로그인 씬에서 수동 로그인으로 복귀한다.

---

## 7) 완료 기준 (Definition of Done)
- 요청 JSON이 `userId/password`로 전송됨이 로그 또는 캡처로 확인된다.
- 정상 계정 입력 시 로그인 API 성공 후 토큰 검증까지 통과하여 `01_Lobby`로 이동한다.
- 비정상 계정 입력 시 한국어 오류 메시지가 노출되고 로그인 씬 유지된다.
- 딥링크 토큰 로그인, 저장 토큰 자동로그인 기존 동작이 회귀 없이 유지된다.
- 토큰/비밀번호 민감정보가 로그에 노출되지 않는다.

---

## 8) 테스트 체크리스트
- [ ] `_useTestTokenInEditor=false`에서 즉시 에러 로그 없이 로그인 화면 진입
- [ ] 유효한 딥링크 토큰으로 앱 시작 시 자동 로그인 성공
- [ ] 만료 딥링크 토큰으로 시작 시 로그인 화면 유지
- [ ] 저장 토큰 유효 시 자동 로그인 성공
- [ ] 저장 토큰 만료 시 로그인 화면 유지
- [ ] ID/PW 정상 입력 시 로그인 성공 (`01_Lobby` 이동)
- [ ] ID/PW 오류 입력 시 정확한 에러 메시지 표시
- [ ] 로그인 연타 시 `AUTHENTICATION_BUSY` 동작 확인
- [ ] 네트워크 차단 시 `NETWORK_ERROR` 메시지 확인
- [ ] 토큰 검증 실패 시 로그인 씬 복귀/유지 확인

---

## 9) 인스펙터 설정 가이드 (운영 권장)
- `00_Login`의 `AuthManager`
  - `_loginEndpoint = "http://ioteacher.com/api/auth/login"`
  - `_tokenValidationUrl = "http://ioteacher.com/api/users/me-by-token"`
  - `_useAutoLogin = true` (정책에 따라 조정)
  - `_useDeepLinkLogin = true` (딥링크 채택 시)
  - `_useTestTokenInEditor = false`
  - `_testToken = ""`
- `00_Login`의 `AuthTokenReceiver`
  - 딥링크 사용 시 `_enableDeepLinkLogin = true`
  - ID/PW 전용 모드면 `false`

---

## 10) 후속 개선 (권장)
1. `AuthBootstrapConfig`(ScriptableObject)로 Dev/Stage/Prod URL 분리
2. refresh token 재발급 경로 추가
3. 인증 로깅 정책(민감정보 마스킹) 팀 표준화
