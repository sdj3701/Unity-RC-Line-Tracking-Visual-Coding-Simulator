# ReSavelButtonPlan

## 0. Status
- 작성일: 2026-04-27
- 대상 씬: `03_NetworkCarTest`
- 대상 클래스: `Assets/Scripts/ChatRoom/HostBlockShareSaveToMyLevelButton.cs`
- 목적: `But_Save` 리팩토링 전에 현재 Save 버튼 클래스가 가진 불필요한 책임과 scene에서 실제로 비어 있는 필드를 정리한다.
- 이번 문서는 코드 수정 문서가 아니라 정리/축소 기준 문서다.

## 1. 왜 별도 문서가 필요한가
- 현재 `HostBlockShareSaveToMyLevelButton`은 이름보다 훨씬 많은 책임을 가진다.
- Save 1회 클릭에 필요한 최소 기능은 사실상 아래뿐이다.
  - Host 여부 확인
  - Host 공유 리스트에서 현재 선택된 share 1개 읽기
  - `save-to-my-level` 요청 보내기
  - 성공 시 Host 자신의 RC카 runtime 적용 완료 여부를 확인하기
- 그런데 현재 클래스 안에는 아래가 함께 섞여 있다.
  - verify 버튼 흐름
  - verify 결과 bool UI
  - 상태 text UI
  - 버튼 소유권 충돌 방어
  - 상세 fetch 후 verify
  - 저장 후 XML/JSON 디버그 덤프
  - red/blue/orange 디버그 로그
- 이 상태에서는 실제로 필요한 Save 경로와 디버그/보조 UI 경로를 구분하기가 어렵다.

## 2. 현재 scene 기준 확인 사실
- `03_NetworkCarTest.unity`의 `HostBlockShareSaveToMyLevelButton` 직렬화 상태는 현재 아래와 같다.
  - `_sourcePanel`: 연결됨
  - `_saveButton`: 연결됨
  - `_refreshVerifyButton`: `None`
  - `_statusText`: `None`
  - `_resultBoolText`: `None`
- 즉, scene 기준으로 보면 현재 Save 버튼은 사실상 "Save 버튼 1개 + source panel 1개"만 실제로 쓰고 있다.
- 따라서 `_refreshVerifyButton`, `_statusText`, `_resultBoolText`는 "코드에서는 참조 중이지만, 현재 scene에서는 미사용" 상태다.
- 이 차이를 분리해서 봐야 한다.
  - code-used
  - scene-unused

## 3. 현재 클래스 책임 분해

### 3.1 지금 꼭 필요한 책임
- Host 전용 버튼인지 확인
- Save 중복 클릭 방지
- `HostBlockShareAutoRefreshPanel.SelectedShareId` 읽기
- `ChatRoomManager.SaveBlockShareToMyLevel(...)` 호출
- save success / failed / canceled 이벤트 대기
- 성공 시 Host 자신의 RC카 적용 흐름 완료 여부 확인

### 3.2 지금 불필요하게 커진 책임
- verify 전용 버튼 흐름
- verify 전용 bool 결과 UI
- 상태 text UI 갱신
- verify button ownership guard
- verify fetch/detail 재검증
- save 버튼이 verify fallback 역할까지 하는 분기
- verify result 표시 문자열 조합

### 3.3 디버그는 useful 하지만 책임을 키우는 요소
- `TraceSaveEvent(...)`
- `LogDiagnostic(...)`
- `DebugLogSavedBlockCodeDataAsync(...)`
- `ChatUserLevelDebugApi`
- 이들은 바로 제거 대상이라고 단정할 수는 없지만, Save core와 같은 클래스 안에 남겨두면 읽기 비용이 커진다.

## 4. 필드별 정리 판단

### 4.1 `_refreshVerifyButton`
- 현재 scene에서는 `None`이다.
- 코드에서는 아래 흐름에 연결되어 있다.
  - `OnClickRefreshVerify()`
  - `BindButtons()`
  - `ResolveButtonOwnership()`
  - `UpdateButtonInteractable()`
- 하지만 현재 scene에서 버튼이 비어 있으므로, 실제 사용자 흐름에서는 verify 버튼 기능이 죽어 있다.
- 정리 판단:
  - 제품 요구사항상 verify 버튼이 더 이상 필요 없으면 제거 후보 1순위다.
  - 같이 제거될 수 있는 코드:
    - `_refreshVerifyButton`
    - `OnClickRefreshVerify()`
    - `_verifyButtonBlockedByOwnership`
    - `_useSaveButtonForVerify`
    - `_fallbackVerifyToSaveButton`
    - `RefreshSelectedShareVerificationAsync(...)`
    - detail fetch 대기 관련 일부 코드
    - verify result bool 관련 코드

### 4.2 `_statusText`
- 현재 scene에서는 `None`이다.
- 코드에서는 `SetStatus(...)`가 이 필드에 쓰기를 시도한다.
- 하지만 실제 동작은 `TMP_Text`가 없어도 `Debug.Log(...)`로 계속 남는다.
- 즉 현재 구조에서는 `_statusText`가 없어도 Save core는 동작 가능하다.
- 정리 판단:
  - UI 상태 표시가 필요 없다면 제거 가능하다.
  - 다만 `SetStatus(...)` 자체는 완전히 지우기보다 다음 중 하나로 바꾸는 편이 안전하다.
    - `LogStatus(...)`로 이름 변경 후 로그 전용 함수로 축소
    - 또는 optional sink를 허용하되 기본은 로그만 남기기

### 4.3 `_resultBoolText`
- 현재 scene에서는 `None`이다.
- 코드에서는 아래 상태를 묶어서 표시한다.
  - `HasSaveResult`
  - `LastSaveResult`
  - `HasVerifyResult`
  - `LastVerifyResult`
  - `UpdateResultBoolText()`
- 하지만 scene에 연결된 text가 없으므로 사용자에게 보이는 결과는 없다.
- 정리 판단:
  - Save 결과 bool UI를 실제로 쓰지 않는다면 제거 후보다.
  - verify 제거와 함께 가면 정리 폭이 더 커진다.

## 5. 핵심 문제 정의
- 지금 문제는 "field가 None이니까 그냥 삭제하면 된다"가 아니다.
- 정확한 문제는 아래다.
  - scene에서는 안 쓰는 UI 필드가 남아 있다.
  - 그런데 코드 안에서는 그 UI 필드 때문에 verify/status/result 흐름이 계속 살아 있다.
  - 결과적으로 Save core보다 보조 코드가 더 커 보인다.
- 따라서 refactor 목표는 "None 필드 삭제"가 아니라 "Save core만 남기고 나머지를 분리/제거"다.

## 6. 리팩토링 목표

### 6.1 최종 형태
- `HostBlockShareSaveToMyLevelButton`은 아래만 책임진다.
  - Host 전용 여부 확인
  - 단일 share 선택 확인
  - Save 요청
  - Save 결과 대기
  - Host 차량 적용 완료 확인
- 여기서 끝나야 한다.

### 6.2 제거 우선순위
1. verify UI 경로 제거
2. result bool UI 제거
3. status text optional 처리 또는 로그 전용 처리
4. ownership guard 단순화
5. debug helper 분리 여부 판단

## 7. 권장 리팩토링 단계

### Phase A. scene 미사용 필드 정리
- `_refreshVerifyButton`
- `_statusText`
- `_resultBoolText`
- 이 셋을 scene 기준 미사용 필드로 명시하고, inspector 의존성을 먼저 끊는다.

### Phase B. verify 기능 제거
- 아래 흐름이 현재 요구사항에 필요한지 먼저 확정한다.
  - `OnClickRefreshVerify()`
  - `RefreshSelectedShareVerificationAsync(...)`
  - verify result bool
  - verify button ownership guard
- 필요 없으면 Save 클래스에서 제거한다.

### Phase C. status/result UI 제거 또는 축소
- `SetStatus(...)`는 로그 전용 함수로 바꿀 수 있다.
- `UpdateResultBoolText()`와 관련 상태값은 실제 UI 연결이 없다면 제거한다.

### Phase D. Save core만 남기기
- 최종적으로 남겨야 하는 메서드 후보:
  - `OnClickSaveToMyLevel()`
  - `SaveSelectedShareAsync(...)`
  - save success/fail/cancel 이벤트 처리
  - selection resolve
  - host/user validation
  - runtime apply 성공 검증에 필요한 최소 helper

### Phase E. debug helper 분리
- `ChatUserLevelDebugApi` 같은 디버그 helper는 다음 중 하나로 정리한다.
  - 별도 debug utility 파일로 분리
  - 또는 editor/debug compile symbol 아래로 격리
- Save 버튼 핵심 흐름 파일에서 빼는 것이 읽기성이 좋다.

## 8. 제거 후보 상세 목록

### 8.1 즉시 제거 검토 후보
- `_refreshVerifyButton`
- `_statusText`
- `_resultBoolText`
- `OnClickRefreshVerify()`
- `HasVerifyResult`
- `LastVerifyResult`
- `_verifyButtonBlockedByOwnership`
- `_useSaveButtonForVerify`
- `_fallbackVerifyToSaveButton`

### 8.2 verify 제거 시 연쇄 정리 후보
- `RefreshSelectedShareVerificationAsync(...)`
- detail fetch await/result type 일부
- verify 관련 `SetStatus(...)` 문구
- verify result bool text 조합
- verify ownership conflict 로그

### 8.3 나중에 분리할 수 있는 디버그 후보
- `TraceSaveEvent(...)`
- `LogDiagnostic(...)`
- `DebugLogSavedBlockCodeDataAsync(...)`
- `ChatUserLevelDebugApi`

## 9. 주의점
- `_statusText`, `_resultBoolText`가 scene에서 `None`이라고 해서 `SetStatus(...)`, `SetSaveResult(...)`를 바로 무조건 삭제하면 안 된다.
- 그 함수들은 현재 로그 출력과 상태 정리에 같이 쓰이고 있다.
- 따라서 삭제 순서는 아래가 안전하다.
  - 1. UI sink 제거
  - 2. 함수 역할을 로그 전용으로 축소
  - 3. verify/result 상태값 삭제
- `_refreshVerifyButton`도 마찬가지다.
  - field 하나만 지우면 끝나지 않는다.
  - verify 전체 흐름과 ownership guard 분기까지 같이 줄여야 효과가 난다.

## 10. 완료 기준
- `HostBlockShareSaveToMyLevelButton` inspector에서 scene 미사용 필드가 정리된다.
- Save 버튼 클래스가 verify 버튼 기능 없이도 읽히는 구조가 된다.
- `But_Save`의 핵심 경로가 `OnClick -> Save request -> success/fail -> host apply`로 짧게 추적된다.
- `None`으로 남아 있는 UI 필드 때문에 불필요한 분기가 유지되지 않는다.
- Save core와 debug/helper 코드가 분리되거나 최소한 논리적으로 구획된다.

## 11. 이 문서의 결론
- 현재 `HostBlockShareSaveToMyLevelButton`은 "Save 버튼"이라기보다 "Save + Verify + Status UI + Result UI + Debug Tool" 묶음에 가깝다.
- scene 기준으로 실제 쓰는 것은 `_sourcePanel`과 `_saveButton`뿐이므로, 나머지 UI 필드는 리팩토링 1차 타깃으로 보는 것이 맞다.
- 특히 `_refreshVerifyButton`, `_statusText`, `_resultBoolText`는 "코드 참조는 남아 있지만 현재 scene에서는 미사용" 상태라는 점을 기준으로 정리해야 한다.
- 다음 리팩토링은 "None 필드 지우기"가 아니라 "Save core를 드러내기 위해 verify/UI/debug 책임을 걷어내는 작업"으로 진행하는 것이 맞다.
