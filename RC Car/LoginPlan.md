# Login Plan (ID/PW Direct Login)

## 0) 목적
이 문서는 Unity 클라이언트에 `ID/PW 로그인`을 도입하고, 인증 성공 시 `01_Lobby`로 이동, 실패 시 로그인 재요청이 가능하도록 만드는 상세 계획서다.

## 1) 범위 고정 (Fix 반영)
- 기존 목표였던 웹 딥링크 기반 자동 로그인(`rccar://...`)은 이번 범위에서 제외한다.
- 즉, 인증 시작점은 `00_Login` 씬의 `ID/PW 입력`이다.
- 기존 토큰 검증 로직(`AuthManager.AuthenticateWithTokenAsync`)은 재사용한다.

## 2) 요구사항 매핑
1. 로그인 기능 제공
2. `id`, `password`를 웹 서버로 전송
3. 서버에서 계정 검증
4. `success=true/false` 및 오류값 수신
5. 성공 시 `01_Lobby`, 실패 시 재요청

## 3) 권장 로그인 플로우
1. 사용자: `00_Login` 씬에서 ID/PW 입력
2. 클라이언트: 입력값 1차 검증(빈 값, 최소 길이)
3. 클라이언트: 로그인 API `POST` 요청
4. 서버: 계정/비밀번호 검증
5. 서버 응답:
   - 성공: `success=true`, `accessToken`(필수), `refreshToken`(권장)
   - 실패: `success=false`, `errorCode`, `message`, `retryable`
6. 클라이언트: 성공 시 기존 `AuthenticateWithTokenAsync(accessToken, refreshToken)` 호출
7. 토큰 검증 성공 시 `01_Lobby` 이동
8. 실패 시 로그인 화면 유지 + 에러메시지 표시 + 즉시 재시도 허용

## 4) 성공/실패 반환값 설계

### 4.1 요청 예시
```http
POST /api/auth/login
Content-Type: application/json

{
  "id": "user01",
  "password": "password123"
}
```

### 4.2 성공 응답 예시
```json
{
  "success": true,
  "accessToken": "...",
  "refreshToken": "...",
  "user": {
    "userId": "123",
    "username": "user01",
    "role": "USER"
  }
}
```

### 4.3 실패 응답 예시
```json
{
  "success": false,
  "errorCode": "INVALID_CREDENTIALS",
  "message": "아이디 또는 비밀번호가 올바르지 않습니다.",
  "retryable": true
}
```

### 4.4 상태코드 가이드
- `200`: 로그인 성공
- `400`: 요청 형식 오류 (`VALIDATION_ERROR`)
- `401`: 자격증명 오류 (`INVALID_CREDENTIALS`)
- `403`: 계정 상태 제한 (`ACCOUNT_LOCKED`, `ACCOUNT_DISABLED`)
- `429`: 시도 제한 (`TOO_MANY_ATTEMPTS`)
- `500`: 서버 내부 오류 (`INTERNAL_ERROR`)

## 5) TODO 반영: 값별 ErrorMessage 정책
`errorCode`에 따라 사용자 문구를 고정 매핑한다.

| errorCode | 사용자 표시 메시지 | 재시도 정책 |
|---|---|---|
| `INVALID_CREDENTIALS` | 아이디 또는 비밀번호가 올바르지 않습니다. | 즉시 재시도 가능 |
| `VALIDATION_ERROR` | 입력 형식을 확인해 주세요. | 수정 후 재시도 |
| `ACCOUNT_LOCKED` | 계정이 잠겼습니다. 관리자에게 문의하세요. | 재시도 불가 |
| `ACCOUNT_DISABLED` | 비활성화된 계정입니다. | 재시도 불가 |
| `TOO_MANY_ATTEMPTS` | 시도 횟수를 초과했습니다. 잠시 후 다시 시도하세요. | 쿨다운 후 재시도 |
| `NETWORK_ERROR` | 네트워크 연결을 확인해 주세요. | 즉시 재시도 가능 |
| `INTERNAL_ERROR` | 서버 오류가 발생했습니다. 잠시 후 다시 시도하세요. | 재시도 가능 |
| `UNKNOWN_ERROR` | 알 수 없는 오류가 발생했습니다. | 재시도 가능 |

UI 정책:
- 요청 중 버튼 비활성화 (`Submitting` 상태)
- 실패 시 입력값 유지 (재입력 최소화)
- 비밀번호 필드는 실패 후 마스킹 상태 유지

## 6) Think 반영: 클래스 설계 (역할 분리)

### 6.1 기존 클래스 역할 (유지/변경)
- `AuthManager`
  - 인증 오케스트레이션(토큰 검증, 인증 상태 보관, 씬 전환)
  - 로그인 API 직접 호출 책임은 분리하는 것이 좋음
- `AuthTokenReceiver`
  - URL 스키마 전용이므로 이번 범위에서 비활성/미사용
- `ProtocolRegistrar`
  - URL 스키마 전용이므로 이번 범위에서 비활성/미사용
- `AuthManualTokenFallbackUI`
  - 필요 시 개발용 fallback으로만 유지, 운영 로그인 UI로는 사용하지 않음

### 6.2 추가 권장 클래스
- `LoginRequestModel`
  - `id`, `password` 요청 모델
- `LoginResponseModel`
  - `success`, `accessToken`, `refreshToken`, `errorCode`, `message`, `retryable`
- `AuthApiClient`
  - 로그인 API 호출 전담 (`POST /api/auth/login`)
  - HTTP 상태코드/JSON 파싱 공통 처리
- `LoginPresenter` 또는 `LoginController`
  - 로그인 화면 이벤트 처리
  - 상태 전이(`Idle/Submitting/Failed/Success`) 관리
- `AuthErrorMapper`
  - `errorCode -> 사용자 메시지` 변환 담당
- `AuthSessionStore`
  - 토큰 저장/삭제/조회(`PlayerPrefs` 캡슐화)

## 7) Think 반영: 구조/의존성 권장안
의존성 방향은 `UI -> LoginController -> AuthApiClient -> AuthManager`로 단방향 유지한다.

- `UI`
  - 입력 수집, 버튼 이벤트 전달, 상태 렌더링
- `LoginController`
  - 입력 검증
  - 중복 요청 방지
  - `AuthApiClient` 호출
  - 성공 시 `AuthManager.AuthenticateWithTokenAsync`로 연결
  - 실패 시 `AuthErrorMapper`로 문구 변환
- `AuthApiClient`
  - 네트워크 통신만 담당
  - 인증 상태/씬 전환 로직은 가지지 않음
- `AuthManager`
  - 최종 인증 성공 상태 확정
  - `01_Lobby` 전환

핵심 원칙:
- 네트워크 코드와 씬 전환 코드를 분리
- 에러 처리 정책을 중앙화(`AuthErrorMapper`)
- 토큰 저장 로직을 단일 클래스(`AuthSessionStore`)로 고정

## 8) 구현 단계 계획

### Phase A: API 계약 확정
- 로그인 URL/메서드
- 요청/응답 JSON 스키마
- 에러코드 표준
- 토큰 만료/갱신 정책

### Phase B: 데이터 모델/에러 정책 확정
- `LoginRequestModel`, `LoginResponseModel` 구조 확정
- `errorCode` 매핑표 확정

### Phase C: 로그인 모듈 구현
- `AuthApiClient` 구현
- `LoginController` 구현
- `AuthErrorMapper` 구현

### Phase D: 인증 연결/씬 전환
- 로그인 성공 -> `AuthManager.AuthenticateWithTokenAsync`
- 최종 성공 -> `01_Lobby`
- 실패 -> 로그인 화면 유지

### Phase E: 테스트
- 정상 로그인
- 비밀번호 오류
- 계정 잠금/비활성
- 429(시도 제한)
- 서버 장애/타임아웃
- 앱 재시작 후 자동 로그인 정책 점검

## 9) 필요한 정보 + 필요한 이유

| 필요한 정보 | 필요한 이유 | 없을 때 리스크 |
|---|---|---|
| 로그인 API URL (환경별) | 올바른 서버 타겟 설정 | 테스트/운영 혼선 |
| 로그인 식별자 규칙(`id`/`email`) | 입력 필드/검증 규칙 확정 | 항상 인증 실패 가능 |
| 실패 코드 사전 | 에러 메시지 정확도 확보 | 사용자 혼란 증가 |
| 토큰 정책(access/refresh 만료) | 자동 로그인/재로그인 전략 수립 | 잦은 로그아웃/세션 깨짐 |
| 계정 상태 정책(잠김/비활성) | `401` vs `403` 분기 처리 | 잘못된 안내/재시도 유도 오류 |
| 로그인 시도 제한 정책 | `429` 처리와 쿨다운 안내 | 무한 시도 및 보호 정책 위반 |
| 타임아웃 기준/재시도 정책 | UX 응답성 확보 | 멈춤처럼 보이는 화면 |
| 로그 마스킹 기준 | 보안 준수 | 비밀번호/토큰 유출 위험 |

## 10) 수용 기준 (Definition of Done)
- 로그인 성공 시 `01_Lobby` 이동
- 실패 시 로그인 화면 유지 + 사유별 메시지 출력
- `errorCode`별 사용자 메시지가 표준 매핑대로 출력
- 요청 중 중복 제출 방지
- 네트워크 오류와 인증 오류를 구분 표시

## 11) 구현 시작 전 체크리스트
- [ ] URL 스키마 경로 미사용 확정(이번 범위)
- [ ] API 명세서 확정
- [ ] 오류코드-메시지 매핑표 확정
- [ ] 로그인 UI 상태 정의(Idle/Submitting/Failed)
- [ ] 테스트 계정(정상/잠김/비활성) 준비
- [ ] QA 시나리오 작성
