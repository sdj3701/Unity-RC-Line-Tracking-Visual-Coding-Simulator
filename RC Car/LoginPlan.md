# Login Plan

## 1) 목적
- 로그인 구조를 명확히 분리한다.
- ID/PW 로그인 버튼은 `ID/PW 인증`만 담당한다.
- 딥링크 토큰(`rccar://...`)은 앱 시작 시 1회 처리한다.

## 2) 패치 반영 요약
- `AuthTokenReceiver`
  - 커맨드라인 토큰 파싱은 1회만 수행.
  - `AuthManager`를 직접 호출하지 않고 토큰 저장 + 이벤트 발행만 수행.
- `AuthManager`
  - 딥링크 토큰 처리 책임을 가져감.
  - 시작 시 우선순위: 에디터 테스트 토큰 -> 딥링크 토큰 -> 자동 로그인 토큰.
  - 딥링크 수신 이벤트를 구독해서 후속 토큰도 처리 가능.
- `AuthManualTokenFallbackUI`
  - 로그인 버튼은 `LoginWithCredentialsAsync(id, password)`만 호출.

## 3) 인증 시작점 우선순위
1. Editor Test Token (옵션)
2. Deep Link Token (옵션, 시작 시 1회)
3. Saved Token Auto Login (옵션)
4. Manual ID/PW Login (사용자 버튼 클릭)

정책:
- 상위 단계 성공 시 하위 단계는 실행하지 않는다.
- 실패 시 로그인 씬에서 수동 로그인으로 복귀한다.

## 4) 책임 분리 (최종 구조)
- `AuthTokenReceiver`
  - 프로토콜 등록
  - 커맨드라인 URL 파싱
  - access/refresh 토큰 보관
  - `OnTokenReceived(access, refresh)` 이벤트 발행
- `AuthManager`
  - 인증 오케스트레이션
  - 토큰 검증 API 호출
  - 인증 상태 저장/초기화
  - 로그인 성공 시 씬 전환
- `AuthApiClient`
  - ID/PW 로그인 API 호출 전담
- `AuthSessionStore`
  - 토큰 로컬 저장/조회/삭제
- `AuthManualTokenFallbackUI`
  - 로그인 UI 입력/요청/결과 표시

## 5) 씬/인스펙터 설정 규칙
- `00_Login`의 `AuthManager`
  - `Use Auto Login`: 운영 정책에 따라 설정
  - `Use Deep Link Login`: 딥링크 사용 시 `On`
  - `Use Test Token In Editor`: 기본 `Off` 권장
- `00_Login`의 `AuthTokenReceiver`
  - 딥링크 사용 시 `_enableDeepLinkLogin = true`
  - ID/PW 전용이면 `false`

권장 기본값(운영):
- `_useTestTokenInEditor = false`
- `_testToken = ""`
- `_useDeepLinkLogin = true` (딥링크 채택 시)

## 6) 표준 로그인 플로우
1. 앱 시작
2. `AuthManager`가 초기 인증 경로 판단
3. 토큰 인증 성공 시 `01_Lobby` 이동
4. 실패 또는 토큰 없음이면 `00_Login` 유지
5. 사용자가 ID/PW 입력 후 Login 버튼 클릭
6. 로그인 API 성공 -> 토큰 검증 성공 -> `01_Lobby`
7. 실패 시 사유 표시 후 재시도

## 7) 이번 패치로 해결한 문제
- 문제: 토큰 파싱/인증 호출 지점이 분산되어 흐름이 불명확함.
- 해결:
  - 토큰 수신(`AuthTokenReceiver`)과 인증 실행(`AuthManager`)을 분리.
  - 딥링크 토큰은 시작 시점 1회 처리 + 이벤트 처리로 일관화.
  - 로그인 버튼의 책임을 ID/PW 인증으로 고정.

## 8) 테스트 체크리스트
- [ ] `_useTestTokenInEditor=false`에서 즉시 에러 로그 없이 로그인 화면 진입
- [ ] 유효한 딥링크 토큰으로 앱 시작 시 자동 로그인 성공
- [ ] 만료 딥링크 토큰으로 시작 시 로그인 화면 유지
- [ ] 저장 토큰 유효 시 자동 로그인 성공
- [ ] 저장 토큰 만료 시 로그인 화면 유지
- [ ] ID/PW 정상 입력 시 로그인 성공
- [ ] ID/PW 오류 입력 시 정확한 에러 메시지 표시

## 9) 다음 개선(권장)
1. `AuthBootstrapConfig`(ScriptableObject)로 로그인 옵션 환경 분리(Dev/Stage/Prod)
2. 토큰 갱신(refresh) 경로 추가
3. 인증 로그 마스킹 정책 정리(토큰/비밀번호 로그 금지)
