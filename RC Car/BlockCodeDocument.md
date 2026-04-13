# BlockCodeDocument

## 1. 문서 목적
이 문서는 `BlocksEngine2` 기반 블록 코딩 시스템의 **흐름(Flow)** 과 **기능(Feature)** 을 중심으로 정리한 기술 문서입니다.
특히 "사용자 행동 → 어떤 코드가 호출되는지 → 결과가 어떻게 반영되는지"를 실제 코드 기준으로 설명합니다.

---

## 2. 분석 범위

### 2.1 BlocksEngine2 핵심
- 입력/이벤트: `BE2_InputManager`, `BE2_MainEventsManager`, `BE2_EventsManager`
- 드래그/드롭: `BE2_DragDropManager`, `BE2_Raycaster`, `BE2_DragSelectionBlock`, `BE2_DragBlock`, `BE2_DragOperation`, `BE2_DragTrigger`
- 블록 구조: `BE2_Block`, `BE2_BlockVerticalLayout`, `BE2_BlockSectionHeader`, `BE2_BlockSectionBody`, `BE2_OuterArea`
- 실행 코어: `BE2_ExecutionManager`, `BE2_BlocksStack`, `BE2_InstructionBase`, 각 `BE2_Ins_*`/`BE2_Op_*`
- 저장/복원: `BE2_UI_ContextMenuManager`, `BE2_BlocksSerializer`, `BE2_BlockXML`, `BE2_CodeStorageManager`
- 사용자 생산성: `BE2_KeyboardShortcutManager` (복사/붙여넣기/Undo/Delete)

### 2.2 프로젝트 커스텀 연동
- XML → Runtime JSON 변환: `Assets/Scripts/Core/BE2XmlToRuntimeJson.cs`
- 런타임 실행기: `Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs`
- 핀 매핑/가상 I/O: `Assets/Scripts/Core/VirtualArduino/VirtualArduinoMicro.cs`
- 차량 물리 반영: `Assets/Scripts/Core/VirtualArduino/VirtualCarPhysics.cs`

---

## 3. 전체 구조 한눈에 보기

```text
[사용자 입력]
  마우스/키보드
      ↓
BE2_InputManager (이벤트 발생)
      ↓
BE2_DragDropManager + BE2_Raycaster (드래그 대상/드롭 스팟 계산)
      ↓
블록 트리(UI 계층) 갱신
  - ProgrammingEnv
  - BlockSection Body/Input/OuterArea
      ↓
BE2_BlocksStack.PopulateStack() (실행 가능한 instruction 배열 재구성)
      ↓
BE2_ExecutionManager.OnUpdate → BlocksStack.Execute()
      ↓
각 BE2_Ins_*/BE2_Op_* 동작 실행
      ↓
(프로젝트 확장) JSON 런타임 실행기/가상 아두이노/차량 물리 반영
```

---

## 4. 사용자 행동 기반 상세 흐름

## 4.1 사용자가 블록 선택 패널에서 블록을 드래그해 배치할 때

요청하신 예시(드래그한 prefab 생성 후 배치)와 실제 코드 흐름은 아래와 같습니다.

1. 사용자가 선택 패널의 블록을 클릭/드래그 시작
- 입력 이벤트: `BE2_InputManager.OnUpdate()`
- 이벤트 발생: `OnPrimaryKeyDown`, `OnDrag`
- 드래그 관리자 진입: `BE2_DragDropManager.OnPointerDown()` → `Raycaster.GetDragAtPosition()`

2. 선택 패널 블록 드래그 처리
- 클래스: `BE2_DragSelectionBlock`
- `OnPointerDown()`에서 현재 보이는 ProgrammingEnv의 스케일(줌) 저장
- `OnDrag()`에서 실제로 `Instantiate(_uiSelectionBlock.prefabBlock)` 실행
- 새로 만든 블록을 `DraggedObjectsTransform` 하위로 이동
- 새 블록의 `I_BE2_Drag`를 현재 드래그 대상으로 전환
- 이어서 새 블록의 `OnPointerDown()`/`OnDrag()`를 즉시 호출

3. 드래그 중 연결 위치 탐색
- 새 블록은 `BE2_DragBlock.OnDrag()`로 넘어감
- 내부 `DetectSpot()`에서 `BE2_Raycaster.FindClosestConnectableSpot/Block` 호출
- 가장 가까운 스팟/블록을 `ConnectionPoint`에 저장
- 고스트 블록(`GhostBlockTransform`)을 해당 위치에 표시

4. 마우스 업(드롭) 시 실제 배치
- `BE2_DragBlock.OnPointerUp()`에서 분기
- 스팟이 있으면 해당 parent/sibling index로 배치
- 블록 위/OuterArea/Body/Input 위치 모두 케이스별 처리
- 유효한 ProgrammingEnv를 못 찾으면 블록 `Destroy`

5. 후처리
- `InstructionBase.UpdateTargetObject()`로 타겟 객체 참조 업데이트
- 새로 생성된 블록이면 Undo 스택에 `Create` 액션 저장
- `BE2_DragDropManager`가 `OnDrop*` 이벤트를 발생시켜 후속 처리 연계

결과: **선택 패널에서 드래그한 순간 prefab 인스턴스가 생성되고, 드롭 지점 규칙에 따라 스택/입력 슬롯/환경에 배치**됩니다.

---

## 4.2 사용자가 이미 배치된 블록을 이동할 때

1. 드래그 시작
- 클래스: `BE2_DragBlock.OnDragStart()`
- 기존 블록이면 이동 전 위치/부모/순서를 저장(Undo Move용)
- 그룹 드래그 모드면 하위 체인(outer area child)까지 함께 떼어 이동

2. 드래그 중
- `DetectSpot()`으로 실시간 연결 후보 재계산
- 고스트 위치 갱신

3. 드롭
- 스팟/블록/프로그래밍 환경 여부에 따라 parent 재배치
- 기존 위치 대비 실제 변경 발생 시 Undo 스택에 `Move` 저장

---

## 4.3 사용자가 연산(Operation) 블록을 입력 슬롯에 꽂을 때

1. 드래그 대상: `BE2_DragOperation`
2. `OnDrag()`에서 `FindClosestSpotOfType<BE2_SpotBlockInput>`만 탐색
3. 후보 슬롯 Outline 활성화
4. 드롭 시 `DropTo(spot)`로 슬롯 위치에 삽입, 슬롯 placeholder 비활성
5. 연결이 안 되면 ProgrammingEnv로 드롭 또는 삭제

핵심: 일반 블록과 달리 **입력 슬롯 타입만 허용**하는 전용 경로를 사용합니다.

---

## 4.4 사용자가 우클릭/길게 눌러 컨텍스트 메뉴를 열 때

- `BE2_DragBlock.OnRightPointerDownOrHold()` 등에서
- `BE2_UI_ContextMenuManager.OpenContextMenu(...)` 호출
- 블록/환경 대상 메뉴를 열고, 저장/불러오기/복제/삭제 같은 액션으로 연결

---

## 4.5 사용자가 단축키를 사용할 때 (Ctrl+C/V/Z, Delete)

- 담당: `BE2_KeyboardShortcutManager`

1. Ctrl+C
- 현재 선택 블록 참조를 clipboard에 저장

2. Ctrl+V
- `BlockToSerializable` → `SerializableToBlock`으로 깊은 복제
- 복제마다 오프셋을 주어 겹침 방지
- Undo에 `Paste` 저장

3. Delete
- 삭제 전 XML 직렬화 스냅샷 저장
- Undo에 `Delete` 저장 후 실제 제거

4. Ctrl+Z (Undo)
- `Delete` Undo: XML로 복원
- `Create/Paste` Undo: 생성 블록 제거
- `Move` Undo: 원래 parent/position/sibling 복구
- Input 값 변경 Undo도 지원

---

## 5. 실행(Play) 흐름

## 5.1 실행 시작

1. `BE2_ExecutionManager.Play()`
2. 글로벌 이벤트 `OnPlay` 발생
3. `BE2_Ins_WhenPlayClicked.OnButtonPlay()`가 스택 활성화

## 5.2 실행 스택 구성

- `BE2_BlocksStack.PopulateStack()`
- `PopulateStackRecursive()`가 트리형 블록을 선형 instruction 배열로 전개
- 각 instruction은 `LocationsArray`에 섹션 시작 위치를 기억

## 5.3 매 프레임 실행

1. `BE2_ExecutionManager.Update()`
2. 등록된 `BlocksStack.Execute()` 호출
3. 현재 `Pointer` instruction의 `Function()` 실행
4. instruction은 `ExecuteSection()`/`ExecuteNextInstruction()`로 다음 위치 제어

## 5.4 제어문/루프/대기 처리

- `BE2_Ins_If`, `BE2_Ins_IfElse`: 조건 true/false에 따라 section 분기
- `BE2_Ins_Repeat`: 카운터 기반 반복
- `BE2_Ins_Wait`: `ExecuteInUpdate = true`, 타이머 기반 진행

핵심: 실행은 “트리 직접 순회”가 아니라, **사전에 평탄화된 instruction 배열 + 포인터 이동 방식**으로 동작합니다.

---

## 6. 변수/함수 블록 동작 흐름

## 6.1 변수 생성

1. UI에서 변수명 입력 (`BE2_UI_NewVariablePanel`)
2. `BE2_VariablesManager.AddOrUpdateVariable()` 등록
3. 변수 뷰어와 선택 블록 갱신

## 6.2 함수 블록 생성

1. 함수 생성 UI (`BE2_UI_CreateFunctionBlockMenu`)에서 label/parameter 구성
2. `BE2_FunctionBlocksManager.CreateFunctionBlock()` 호출
3. DefineFunction 블록이 ProgrammingEnv에 생성
4. 동시에 함수 호출용 Selection Function 블록도 패널에 생성

## 6.3 함수 호출 시

- `BE2_Ins_FunctionBlock`이 define과 연결
- Define 본문을 noView 블록으로 미러링해 실행 전용 트리 구성
- `localValues`에 인자 바인딩 후 함수 body section 실행

---

## 7. 저장/불러오기 흐름

## 7.1 저장

1. `BE2_UI_CodeSavePanel`에서 파일명 입력 후 저장
2. `BE2_UI_ContextMenuManager.SaveCodeWithNameAsync(fileName)`
3. `BE2_CodeExporter.GenerateXmlFromAllEnvs()`로 XML 생성
4. `BE2XmlToRuntimeJson.ExportToString(xml)`로 JSON 생성
5. `LatestRuntimeJson` 메모리 캐시 갱신
6. `BE2_CodeStorageManager.SaveCodeAsync(...)` 저장(로컬/DB provider)
7. `OnCodeGenerated` 이벤트 발행

## 7.2 불러오기

1. `BE2_UI_CodeLoadPanel`에서 파일 선택
2. `LoadCodeFromFileAsync(fileName)`
3. XML 로드 후 `BE2_BlocksSerializer.XMLToBlocksCode(xml, env)`로 UI 블록 복원
4. XML에서 JSON 재생성 후 `LatestRuntimeJson` 갱신
5. `OnCodeGenerated` 이벤트 발행

## 7.3 복원 알고리즘 핵심

`XMLToBlocksCode`는 3-pass 구조:
1. 변수 선등록
2. `DefineFunction` 먼저 생성
3. 나머지 블록 생성

이 순서로 함수/변수 참조 깨짐을 줄입니다.

---

## 8. RC Car 프로젝트의 실제 런타임 연동

## 8.1 코드 생성 후

- `BE2_UI_ContextMenuManager.OnCodeGenerated`
  - `BlockCodeExecutor.ReloadProgram()`
  - `VirtualArduinoMicro.UpdatePinMappingFromVariables()`

## 8.2 물리 루프

1. `VirtualCarPhysics.FixedUpdate()` (isRunning=true일 때)
2. `BlockCodeExecutor.Tick()`
3. `analogWrite/if/callFunction/wait` 등 런타임 JSON 실행
4. `VirtualArduinoMicro.AnalogWrite/DigitalRead/FunctionDigitalRead` 전달
5. `VirtualMotorDriver`, 센서 peripheral 반영
6. Rigidbody 이동/회전 적용

즉, 편집기 블록 실행 코어와 별개로, 이 프로젝트는 **XML→JSON→전용 Executor** 경로를 추가로 사용해 차량 시뮬레이션을 구동합니다.

---

## 9. 대표 시나리오 요약

## 시나리오 A: 블록 드래그 배치
- 사용자 행동: 선택 패널 블록을 드래그해 프로그래밍 영역에 놓음
- 내부 흐름: `BE2_DragSelectionBlock.OnDrag()`에서 prefab 인스턴스 생성 → `BE2_DragBlock.DetectSpot()`으로 위치 계산 → `OnPointerUp()`에서 실제 parent/sibling 배치
- 결과: 해당 위치에 블록이 생성/연결되고 Undo(Create) 등록

## 시나리오 B: 조건 블록 실행
- 사용자 행동: If 블록 내부에 모터 제어 블록 구성 후 Play
- 내부 흐름: `OnPlay` → `BlocksStack.Execute()` → `BE2_Ins_If.Function()` 조건 평가 → true면 `ExecuteSection(0)`
- 결과: 조건 만족 시 하위 body 블록이 즉시 실행

## 시나리오 C: 저장 후 즉시 실행 반영
- 사용자 행동: Save 버튼 클릭
- 내부 흐름: XML 생성 → JSON 변환 → 저장소 저장 → `LatestRuntimeJson` 갱신 → `OnCodeGenerated`
- 결과: 런타임 실행기(`BlockCodeExecutor`)가 최신 코드로 즉시 reload

---

## 10. 기능 관점 핵심 포인트

1. 드래그/드롭은 이벤트 기반 + Raycast 기반 스팟 탐색 구조
2. 실행은 `BlocksStack` 평탄화 배열 방식으로 고정적이고 빠르게 동작
3. 저장 포맷은 XML(구조 보존), 실행 포맷은 JSON(런타임 최적화)
4. 함수/변수는 전용 매니저 + 다단계 복원 로직으로 참조 안정성 보장
5. 프로젝트 확장에서 JSON Executor/가상 아두이노/물리엔진과 결합되어 실제 차량 동작까지 연결
