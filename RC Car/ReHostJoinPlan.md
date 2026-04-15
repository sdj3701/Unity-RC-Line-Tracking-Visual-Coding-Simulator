# ReHostJoinPlan

## 1) 목적
- 기존 `HostJoinRequestMonitorGUI`가 `OnGUI/GUILayout`로 그리던 Host 입장 요청 관리 화면을 Unity UI 기반 화면으로 변경한다.
- 이번 문서는 코드 작업 전에 요구사항과 작업 순서를 고정하기 위한 계획서다.
- 실제 구현은 다음 단계에서 진행하며, 이 문서 작성 시점에는 C# 스크립트와 Scene/Prefab은 수정하지 않는다.

---

## 2) 사용자가 원하는 기능 정리

### 2-1. 리롤 버튼
- Host가 직접 눌러서 최신 방 참여 요청 목록을 다시 가져오는 버튼이다.
- 기존 GUI의 `Fetch Now` 버튼과 같은 역할이다.
- 자동 갱신도 유지한다.
- 자동 갱신 주기는 5초로 맞춘다.
- 리롤 버튼을 눌렀을 때도 자동 갱신과 같은 API 호출 흐름을 사용한다.
- 이미 요청 처리 중이면 중복 호출을 막거나, 다음 갱신 때 처리되도록 상태를 유지한다.

### 2-2. 현재 방 인원 텍스트
- 방 안에 현재 몇 명이 있는지 표시하는 Text 오브젝트다.
- 인원이 변경될 때마다 Text 값을 갱신한다.
- 기존 `HostJoinRequestMonitorGUI`에는 `GetRoomMemberCount()`가 있으며, 현재 로직은 `host 1명 + 승인된 요청자 수`로 인원을 추정한다.
- 구현 단계에서는 이 추정 로직을 그대로 옮길지, 서버가 실제 방 인원 수를 내려주는 API가 있는지 확인해야 한다.
- 우선 목표는 기존 GUI와 같은 기준으로 UI Text를 갱신하는 것이다.

### 2-3. 방 참여 원하는 인원 텍스트
- 현재 방 참여를 기다리는 대기 인원이 몇 명인지 표시하는 Text 오브젝트다.
- 기존 GUI의 `Pending Join Requests`와 같은 역할이다.
- `REQUESTED` 또는 `PENDING` 상태의 요청만 대기 인원으로 계산한다.
- 요청 목록을 새로 가져오거나 Host가 수락/거절한 뒤 목록이 바뀌면 Text 값을 다시 갱신한다.

### 2-4. 스크롤 뷰 요청 목록
- Scroll View 안에 방 참여를 원하는 사용자 목록을 표시한다.
- 각 항목은 요청자 User ID를 Text에 출력한다.
- 각 항목에는 버튼 2개가 있다.
- 첫 번째 버튼은 Host가 참여를 허락하는 버튼이다.
- 두 번째 버튼은 Host가 참여를 거절하는 버튼이다.
- 버튼을 누르면 기존 `DecideJoinRequest(roomId, requestId, approve, token)` 흐름을 사용한다.
- 처리 성공 후에는 목록을 즉시 다시 갱신해서 화면 상태와 서버 상태를 맞춘다.

---

## 3) 현재 코드 기준 진단

### 3-1. 대상 스크립트
- 현재 기능은 `Assets/Scripts/ChatRoom/HostJoinRequestMonitorGUI.cs`에 있다.
- 이 스크립트는 Host 전용 참여 요청 모니터다.
- `OnGUI()`와 `DrawWindowContents()`에서 IMGUI 창을 직접 그리고 있다.
- 자동 폴링, 수동 갱신, 요청 목록 캐싱, 신규 요청 감지, 수락/거절 처리는 이미 포함되어 있다.

### 3-2. 현재 주요 흐름
- `OnEnable()`
  - `ChatRoomManager`를 찾거나 필요하면 런타임 생성한다.
  - 이벤트를 바인딩한다.
  - 자동 폴링을 시작한다.
- `StartPolling()`
  - 코루틴으로 반복 갱신한다.
- `FetchNow()`
  - 즉시 참여 요청 목록을 가져온다.
- `HandleJoinRequestsFetchSucceeded()`
  - 서버에서 받은 요청 목록을 `_latestRequests`에 저장한다.
  - 신규 요청 여부를 추적한다.
- `TryDecideJoinRequest()`
  - 수락/거절 요청을 서버에 보낸다.
- `HandleJoinRequestDecisionSucceeded()`
  - 처리 성공 후 `FetchNow()`를 호출해서 목록을 다시 갱신한다.

### 3-3. 유지할 기존 로직
- Host 여부 확인:
  - `RoomSessionContext.CurrentRoom.HostUserId`
  - `AuthManager.Instance.CurrentUser.userId`
- 대상 방 ID 확인:
  - `_roomIdOverride`
  - `RoomSessionContext.CurrentRoom.RoomId`
- 요청 목록 조회:
  - `ChatRoomManager.FetchJoinRequests(roomId, token)`
- 요청 수락/거절:
  - `ChatRoomManager.DecideJoinRequest(roomId, requestId, approve, token)`
- 대기 상태 판단:
  - `REQUESTED`
  - `PENDING`
- 승인 상태 판단:
  - `APPROVED`
  - `ACCEPTED`

---

## 4) 목표 UI 구조

### 4-1. Scene 계층 예시
```text
HostJoinRequestPanel
  Header
    CurrentMemberCountText
    PendingJoinRequestCountText
    RefreshButton
  RequestScrollView
    Viewport
      Content
        JoinRequestItemPrefab instances...
  EmptyStateText
  StatusText optional
```

### 4-2. 요청 항목 프리팹 예시
```text
JoinRequestItemPrefab
  UserIdText
  AcceptButton
  RejectButton
```

### 4-3. 필요한 Inspector 참조
- 패널 루트:
  - `GameObject mainPanel` 또는 현재 오브젝트 자체 사용
- 카운트 Text:
  - `currentMemberCountText`
  - `pendingRequestCountText`
- 리롤 버튼:
  - `refreshButton`
- Scroll View:
  - `Transform requestListContent`
  - `JoinRequestItemUI requestItemPrefab`
- 빈 상태:
  - `emptyStateObject` 또는 `emptyStateText`
- 상태/오류 표시:
  - `statusText`는 선택 사항

### 4-4. Text 컴포넌트 기준
- 사용자가 말한 Text 오브젝트에 인원 수와 User ID를 출력한다.
- 프로젝트 안에는 `TMP_Text`를 쓰는 UI 코드가 이미 많다.
- 구현 시 실제 Scene 오브젝트가 `TMP_Text`인지 `UnityEngine.UI.Text`인지 확인하고 맞춘다.
- 새로 만드는 UI라면 `TMP_Text`를 우선 사용한다.

---

## 5) 스크립트 설계 방향

### 5-1. 권장 이름
- 기존 이름을 그대로 유지하며 내부를 UI 방식으로 바꾸는 방법:
  - `HostJoinRequestMonitorGUI.cs`의 IMGUI 부분을 제거하고 UI 필드/바인딩을 추가한다.
- 더 명확한 이름으로 새로 분리하는 방법:
  - `HostJoinRequestMonitorUI.cs`를 새로 만들고 기존 GUI 스크립트는 비활성화 또는 제거한다.

권장 방향은 `HostJoinRequestMonitorUI.cs`로 새로 분리하는 것이다.
이유는 기존 IMGUI 부트스트랩과 새 UI 바인딩 책임이 섞이면 Scene에서 중복 화면이 생기거나 Inspector 설정이 헷갈릴 수 있기 때문이다.

### 5-2. 주의할 기존 자동 생성 코드
- `HostJoinRequestMonitorGUI`는 `RuntimeInitializeOnLoadMethod`로 `03_NetworkCarTest` Scene 진입 시 자기 자신을 런타임 생성한다.
- UI 방식으로 바꾸면 이 자동 생성 방식은 맞지 않는다.
- 새 UI는 Scene에 배치된 Canvas 오브젝트와 Inspector 참조가 필요하다.
- 구현 단계에서 기존 GUI 자동 생성은 제거하거나, 새 UI가 없는 디버그 상황에서만 동작하도록 제한해야 한다.

### 5-3. 데이터와 UI 책임 분리
- 요청 목록 데이터:
  - `List<ChatRoomJoinRequestInfo> latestRequests`
- UI 항목 오브젝트:
  - `List<GameObject> spawnedItems`
- 요청 처리 중 상태:
  - `activeDecisionRequestKey`
  - 또는 `activeDecisionRequestId`
- UI 갱신 함수:
  - `RefreshCountTexts()`
  - `RebuildRequestListUi()`
  - `SetButtonsInteractable()`
  - `SetStatus()`

---

## 6) 상세 동작 흐름

### 6-1. 초기화
1. `OnEnable()`에서 Button 이벤트를 바인딩한다.
2. `ChatRoomManager.Instance` 이벤트를 바인딩한다.
3. Host가 아니면 패널을 숨기거나 버튼을 비활성화한다.
4. 자동 갱신 옵션이 켜져 있으면 5초 주기 코루틴을 시작한다.
5. 첫 진입 시 즉시 한 번 `FetchJoinRequests`를 호출한다.

### 6-2. 5초 자동 갱신
1. 5초마다 현재 방 ID를 확인한다.
2. Host 권한을 확인한다.
3. `ChatRoomManager`가 Busy 상태인지 확인한다.
4. Busy가 아니면 `FetchJoinRequests`를 호출한다.
5. 성공 이벤트에서 목록과 Text를 갱신한다.

### 6-3. 리롤 버튼 클릭
1. Host가 `RefreshButton`을 누른다.
2. 자동 갱신과 같은 `FetchNow()` 흐름을 호출한다.
3. 요청 중이면 버튼을 잠시 비활성화하거나 상태 Text에 갱신 대기 상태를 표시한다.
4. 성공/실패/취소 이벤트 후 버튼 상태를 복구한다.

### 6-4. 목록 갱신 성공
1. 서버 응답 배열에서 null 항목을 제거한다.
2. `_latestRequests`를 최신 목록으로 교체한다.
3. 현재 방 인원 수 Text를 갱신한다.
4. 대기 요청 수 Text를 갱신한다.
5. Scroll View Content 아래 기존 항목들을 제거한다.
6. `REQUESTED` 또는 `PENDING` 상태의 요청만 항목으로 다시 생성한다.
7. 대기 요청이 0개면 Empty State를 표시한다.

### 6-5. 수락 버튼 클릭
1. 항목의 `requestId`와 `roomId`를 확인한다.
2. 이미 다른 요청을 처리 중이면 중복 클릭을 막는다.
3. `DecideJoinRequest(roomId, requestId, true, token)`을 호출한다.
4. 성공하면 즉시 `FetchNow()`로 목록을 다시 가져온다.
5. 승인된 사용자 기준으로 현재 방 인원 Text가 증가하도록 갱신한다.

### 6-6. 거절 버튼 클릭
1. 항목의 `requestId`와 `roomId`를 확인한다.
2. 이미 다른 요청을 처리 중이면 중복 클릭을 막는다.
3. `DecideJoinRequest(roomId, requestId, false, token)`을 호출한다.
4. 성공하면 즉시 `FetchNow()`로 목록을 다시 가져온다.
5. 거절된 요청은 대기 목록에서 사라져야 한다.

---

## 7) 카운트 갱신 기준

### 7-1. 현재 방 인원
- 기본 기준:
  - Host 1명
  - 승인 상태인 요청자 수
- 계산 예시:
  - Host만 있으면 `1`
  - User A가 승인되면 `2`
  - User A, User B가 승인되면 `3`
- 중복 User ID가 있을 수 있으므로 `RequestUserId` 기준으로 중복 제거한다.
- `RequestUserId`가 비어 있으면 `RequestId` 기반 key를 보조로 사용한다.

### 7-2. 참여 대기 인원
- `Status`가 비어 있으면 대기 상태로 볼지 여부는 기존 코드와 맞춘다.
- 기존 코드는 빈 상태를 pending으로 취급한다.
- 명확한 대기 상태:
  - `REQUESTED`
  - `PENDING`
- 대기 요청 수는 Scroll View에 실제 표시되는 항목 수와 같아야 한다.

---

## 8) UI 항목 생성 규칙

### 8-1. 표시 텍스트
- 기본 표시:
  - `RequestUserId`
- `RequestUserId`가 비어 있으면 대체 표시:
  - `Unknown User`
  - 또는 `requestId` 일부
- 디버그가 필요하면 StatusText에 `requestId`를 남기고, 사용자가 보는 항목에는 User ID만 보여준다.

### 8-2. 버튼 상태
- 요청 처리 중에는 모든 항목의 수락/거절 버튼을 잠시 비활성화한다.
- 현재 처리 중인 항목만 비활성화할 수도 있지만, `ChatRoomManager.IsBusy`가 전역 Busy이므로 전체 비활성화가 더 안전하다.
- 처리 실패 시 버튼을 다시 활성화하고 오류 상태를 표시한다.

### 8-3. 항목 제거
- 수락/거절 버튼을 누른 직후 바로 UI에서 제거할 수도 있지만, 서버 성공 응답 후 다시 Fetch해서 제거하는 방식이 더 정확하다.
- 따라서 목표 동작은 "결정 성공 -> FetchNow -> 목록 재생성"이다.

---

## 9) 기존 GUI에서 제거 또는 대체할 부분

### 9-1. 제거 대상
- `OnGUI()`
- `DrawWindowContents()`
- `GUIStyle` 관련 필드와 메서드
- `_windowPosition`
- `_windowSize`
- `_windowTitleFontSize`
- `_labelFontSize`
- `_buttonFontSize`
- `_textFieldFontSize`
- IMGUI 전용 Scroll Position과 Window Rect 처리

### 9-2. 유지 대상
- `StartPolling()`
- `StopPolling()`
- `FetchNow()`
- `TryFetchJoinRequests()`
- `TryEnsureAndBindManager()`
- `UnbindManagerEvents()`
- `HandleJoinRequestsFetchSucceeded()`
- `HandleJoinRequestDecisionSucceeded()`
- `TryDecideJoinRequest()`
- Host 검증 함수
- Room ID resolve 함수
- Status 판정 함수
- Count 계산 함수

### 9-3. 새로 필요한 부분
- Button 이벤트 바인딩/해제
- Text 갱신 함수
- Scroll View Content 항목 생성/삭제
- 요청 항목 프리팹 컴포넌트
- Empty State 표시 제어
- Scene에 배치된 UI 참조 검증

---

## 10) 예상 파일 작업

### 10-1. 새 파일 후보
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorUI.cs`
  - Host Join Request 전체 UI 패널 컨트롤러
- `Assets/Scripts/ChatRoom/HostJoinRequestItemUI.cs`
  - Scroll View 항목 1개를 담당하는 컴포넌트

### 10-2. 수정 파일 후보
- `Assets/Scripts/ChatRoom/HostJoinRequestMonitorGUI.cs`
  - 새 UI가 완성되면 자동 부트스트랩을 끄거나 스크립트를 더 이상 Scene에서 사용하지 않도록 정리
- `Assets/Scenes/03_NetworkCarTest.unity`
  - Host UI Canvas 안에 새 패널 배치
  - Text/Button/ScrollView/Prefab 참조 연결
- Prefab 후보:
  - `Assets/Resources/Prefabs/HostJoinRequestItem.prefab`
  - 또는 Scene 내부 프리팹 없이 Hierarchy 오브젝트를 Prefab화

---

## 11) 구현 순서 제안

### Phase 1. UI 구조 확정
- `03_NetworkCarTest` Scene의 Host UI 영역을 확인한다.
- 기존 `NetworkUIManager`가 Host UI와 Client UI를 어떻게 켜고 끄는지 확인한다.
- Host UI 아래에 참여 요청 패널을 둘 위치를 정한다.
- Text, Button, Scroll View, Content, Item Prefab 구조를 만든다.

### Phase 2. 항목 프리팹 스크립트 작성
- `HostJoinRequestItemUI`를 만든다.
- User ID Text를 갱신하는 메서드를 둔다.
- Accept/Reject Button 클릭 콜백을 외부에서 주입할 수 있게 한다.
- 버튼 활성/비활성 상태를 제어하는 메서드를 둔다.

### Phase 3. 패널 컨트롤러 작성
- `HostJoinRequestMonitorUI`를 만든다.
- 기존 `HostJoinRequestMonitorGUI`의 네트워크/폴링/상태 계산 로직을 UI 방식으로 옮긴다.
- `RefreshButton` 클릭 시 `FetchNow()`가 호출되도록 연결한다.
- 자동 갱신 기본 주기를 5초로 설정한다.

### Phase 4. 목록 재생성 연결
- Fetch 성공 시 `_latestRequests`를 갱신한다.
- Current Member Count Text를 갱신한다.
- Pending Request Count Text를 갱신한다.
- Scroll View Content 아래에 요청 항목을 생성한다.
- 대기 요청이 없으면 Empty State를 표시한다.

### Phase 5. 수락/거절 연결
- 항목의 Accept Button은 `TryDecideJoinRequest(request, true)`로 연결한다.
- 항목의 Reject Button은 `TryDecideJoinRequest(request, false)`로 연결한다.
- 결정 요청 중에는 버튼 중복 클릭을 막는다.
- 결정 성공 후 `FetchNow()`를 호출해서 UI를 최신화한다.

### Phase 6. 기존 GUI 정리
- 새 UI가 정상 동작하면 기존 IMGUI 창이 뜨지 않도록 한다.
- `RuntimeInitializeOnLoadMethod` 자동 생성 로직을 제거하거나 비활성화한다.
- Scene에 새 UI가 배치되어 있지 않은 테스트 상황까지 고려할지 결정한다.

---

## 12) 예외 처리 기준

### 12-1. Host가 아닌 경우
- Host 전용 UI이므로 Client에게는 보이지 않아야 한다.
- `NetworkUIManager`의 Host UI 영역 안에 배치하면 자연스럽게 분리된다.
- 추가로 스크립트 내부에서도 Host 검사를 유지한다.

### 12-2. Room ID가 없는 경우
- Text는 `0` 또는 `-`로 표시한다.
- StatusText가 있다면 `RoomId is empty`를 표시한다.
- API 호출은 하지 않는다.

### 12-3. ChatRoomManager가 없는 경우
- 기존 GUI처럼 런타임 생성할 수 있지만, UI 방식에서는 Scene 구성 누락을 발견하기 어렵게 만들 수 있다.
- 권장 방식은 `ChatRoomManager.Instance`가 없으면 StatusText/DebugLog로 오류를 표시하는 것이다.
- 기존 동작 호환이 필요하면 옵션으로 런타임 생성을 유지한다.

### 12-4. 요청 처리 실패
- 해당 요청은 목록에서 바로 제거하지 않는다.
- StatusText에 실패 메시지를 표시한다.
- 버튼을 다시 활성화한다.
- 다음 자동 갱신 때 서버 상태와 다시 맞춘다.

---

## 13) 완료 기준
- Host 화면에서 IMGUI 창 대신 Unity UI 패널로 참여 요청 관리가 가능하다.
- 리롤 버튼을 누르면 즉시 참여 요청 목록이 갱신된다.
- 5초마다 자동으로 참여 요청 목록이 갱신된다.
- 현재 방 인원 Text가 인원 변경 시 갱신된다.
- 방 참여 대기 인원 Text가 대기 요청 수 변경 시 갱신된다.
- Scroll View에 대기 중인 User ID가 표시된다.
- 각 User ID 항목에서 수락 버튼을 누르면 해당 요청이 승인된다.
- 각 User ID 항목에서 거절 버튼을 누르면 해당 요청이 거절된다.
- 수락/거절 성공 후 목록과 카운트가 다시 갱신된다.
- Host가 아닌 사용자는 해당 UI를 조작할 수 없다.
- 기존 `HostJoinRequestMonitorGUI`의 IMGUI 창이 중복으로 뜨지 않는다.

---

## 14) 다음 작업 때 먼저 확인할 질문
- 현재 Scene에 이미 만들어둔 Text/Button/Scroll View 오브젝트가 있는지 확인한다.
- Text 컴포넌트가 `TMP_Text`인지 `UnityEngine.UI.Text`인지 확인한다.
- 새 요청 항목을 Prefab으로 만들지, Scene 안의 Template 오브젝트를 복제할지 결정한다.
- 현재 방 인원은 기존 추정 방식으로 충분한지, 서버의 실제 방 인원 필드를 사용해야 하는지 확인한다.
- 기존 `HostJoinRequestMonitorGUI`를 완전히 대체할지, 디버그용으로 남겨둘지 결정한다.
