# BindPin Runtime Implementation Plan

## 1. 목적

이 문서는 [PinPlan.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/PinPlan.md:1>), [BindPinBlockDesign.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/BindPinBlockDesign.md:1>), [BindPinSchemaDraft.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/BindPinSchemaDraft.md:1>)를 바탕으로,
`bindPin`을 현재 코드베이스에 어떤 순서로 붙일지 구현 계획을 정리한다.

---

## 2. 현재 코드 기준 관련 지점

### 블록 저장/복원

- `Assets/BlocksEngine2/Scripts/Serialization/BE2_BlocksSerializer.cs`
- `Assets/BlocksEngine2/Scripts/Serialization/BE2_BlockXML.cs`

### XML -> Runtime JSON 변환

- `Assets/Scripts/Core/BE2XmlToRuntimeJson.cs`

### 런타임 실행

- `Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs`

### 핀 매핑 / 하드웨어 추상화

- `Assets/Scripts/Core/VirtualArduino/VirtualArduinoMicro.cs`

---

## 3. 구현 순서 개요

가장 안전한 구현 순서는 아래다.

1. 블록 정의/UI 추가
2. XML 저장/복원 검증
3. XML -> Runtime JSON 변환 추가
4. 런타임 JSON 파싱 추가
5. 런타임 실행 추가
6. `VirtualArduinoMicro` explicit mapping 우선 구조 반영
7. fallback/경고/검증 추가
8. 통합 테스트

이 순서를 권장하는 이유는,
UI 블록이 먼저 있어야 저장 구조를 실제로 확인할 수 있고,
JSON이 먼저 안정돼야 런타임 실행을 붙였을 때 디버깅이 쉬워지기 때문이다.

---

## 4. 1단계: 블록 자체 추가

## 작업 목표

- `BindPin` instruction block prefab 생성
- 역할 dropdown + pin input UI 구성
- init 영역에 배치 가능한 블록으로 노출

## 권장 산출물

- `BE2_Ins_BindPin.cs`
- `Block Ins BindPin.prefab`
- 필요 시 선택 패널 등록

## 권장 동작

이 instruction은 에디터 실행 엔진에서 특별한 계산을 하지 않아도 된다.
핵심은 저장/복원과 XML 생성이 가능해야 한다는 점이다.

1차에서는 editor-side execute logic이 거의 비어 있어도 된다.
실제 의미는 RC Car의 runtime JSON 경로에서 처리해도 충분하다.

---

## 5. 2단계: XML 저장/복원 검증

현재 serializer는 일반 블록의 header inputs를 자동 저장/복원한다.

따라서 `bindPin`은 아래 구조로 설계하면 serializer를 크게 건드리지 않아도 된다.

- input[0] = role dropdown
- input[1] = pin input

## 확인 포인트

1. `BlocksCodeToXML()`에서 role과 pin이 저장되는가
2. `XMLToBlocksCode()` 후 role dropdown과 pin 입력이 정상 복원되는가
3. pin input이 variable operation이어도 복원되는가

## 예상 작업량

- serializer 수정 없음 또는 최소화

이 단계에서 문제가 생기면 block UI 구성이 `Header.InputsArray`를 타지 않는지 먼저 봐야 한다.

---

## 6. 3단계: `BE2XmlToRuntimeJson` 반영

## 6.1 등록

`blockParsers`에 아래 키를 추가한다.

- `Block Ins BindPin`
- `Block Cst BindPin`

## 6.2 파서 추가

추가 함수:

```csharp
static LoopBlockNode ParseBindPinBlock(XElement block)
```

권장 읽기 규칙:

- input[0] -> `role`
- input[1] -> `pinVar` 또는 `pin`

## 6.3 노드 구조 확장

`LoopBlockNode`에 필요한 필드:

- `role`
- 기존 `pin`
- 기존 `pinVar`

## 6.4 JSON 출력 추가

`LoopBlockNodeToJson()`에 `bindPin` case를 추가한다.

예:

```json
{ "type": "bindPin", "role": "leftSensor", "pinVar": "sensorLeftPin" }
```

---

## 7. 4단계: init 순서 처리 확장

이 단계가 중요하다.

현재 init에는 사실상 아래 성격의 노드가 있다.

- `setVariable`
- `wait`

`bindPin`은 init에서 실행되어야 하므로,
init 순서 보존 로직에 함께 포함돼야 한다.

## 권장 작업

### 7.1 함수명 정리

현재 `ProcessInitWaitBlocks()`는 의미가 좁다.
권장 이름:

- `ProcessInitSequenceBlocks()`

### 7.2 수집 대상 확장

수집 대상:

- `setVariable`
- `bindPin`
- `wait`

### 7.3 이유

`bindPin`은 아래 순서를 보장해야 한다.

```text
setVariable sensorLeftPin = 3
bindPin leftSensor sensorLeftPin
```

따라서 JSON `init`에 실행 순서가 보존돼야 한다.

---

## 8. 5단계: `BlockCodeExecutor` JSON 파싱 추가

## 권장 변경

### `BlockNode` 확장

추가 또는 재사용 필드:

- `type`
- `role`
- `pin`
- `pinVar`

### `ParseNode()` 확장

읽어야 할 key:

- `role`
- `pin`
- `pinVar`

`pin/pinVar`는 이미 다른 노드에서 쓰고 있으므로,
사실상 `role`만 새로 읽으면 충분할 가능성이 높다.

---

## 9. 6단계: `BlockCodeExecutor.ExecuteNode()` 반영

새 case 추가:

```csharp
case "bindPin":
    ExecuteBindPin(node);
    break;
```

추가 함수:

```csharp
void ExecuteBindPin(BlockNode node)
```

권장 로직:

1. `role` 검증
2. `pinVar`가 있으면 `GetVariable(pinVar, pin)`으로 숫자 해석
3. 유효 범위 확인
4. `arduino.ConfigurePin(resolvedPin, role)` 호출
5. 로그 출력

---

## 10. 7단계: `VirtualArduinoMicro` 우선순위 조정

현재 가장 큰 구조 문제는 `UpdatePinMappingFromVariables()`가 값 기반 자동 매핑이라는 점이다.

`bindPin`이 들어오면 우선순위를 아래처럼 바꾸는 것이 맞다.

1. explicit bindPin / `ConfigurePin()`
2. inspector 기본 설정
3. legacy 값 기반 fallback

## 권장 작업

### 10.1 명시적 매핑 존재 여부 추적

예:

- `hasExplicitBinding`
- 또는 explicit role 집합 관리

### 10.2 `UpdatePinMappingFromVariables()` 축소

방향:

- explicit mapping이 있으면 값 기반 자동 매핑을 건너뜀
- 또는 warning-only 모드로 축소

### 10.3 이유

그렇지 않으면 `bindPin(leftSensor, 7)`을 했는데,
다른 일반 변수값이 `3`이라서 다시 기본 left sensor가 3으로 잡히는 식의 충돌이 생길 수 있다.

---

## 11. 8단계: init 변수 preload 리스크 점검

현재 `LoadProgramFromJson()`은 init 블록의 `setVariable`들을 미리 수집한다.

이 구조는 대부분 편하지만,
아래처럼 init 안에서 같은 변수를 재할당하면 의미가 꼬일 수 있다.

```text
set a = 3
bindPin(leftSensor, a)
set a = 4
```

미리 변수값을 수집하면 `bindPin` 시점에 `a=4`로 보일 위험이 있다.

## 권장 대응

### 1차 구현

문서 규칙으로 제한:

- pin 변수는 init에서 bind 후 재할당하지 않는다

### 2차 개선

`CollectVariablesFromInit()`를 bindPin 시점 semantics와 맞도록 재검토한다.

이건 `bindPin`만의 문제가 아니라 init action 전반의 순서 의미 문제다.
따라서 별도 정리 과제로 두는 것이 맞다.

---

## 12. 9단계: validation / warning 추가

권장 로그 포인트:

- unknown role
- missing pin source
- resolved pin out of range
- duplicate role binding
- same pin bound to different role
- bindPin used outside init

이 로그가 있어야 클라이언트가 설계를 이해하기 쉽다.

---

## 13. 테스트 순서

## 13.1 저장/복원 테스트

1. `bindPin(leftSensor, 3)` 저장
2. XML 확인
3. 다시 로드
4. 블록 UI가 동일한지 확인

## 13.2 JSON 테스트

1. `bindPin(leftSensor, 3)` -> `{"type":"bindPin","role":"leftSensor","pin":3}`
2. `bindPin(leftMotorF, lwf)` -> `{"type":"bindPin","role":"leftMotorF","pinVar":"lwf"}`

## 13.3 런타임 테스트

1. init에서 `bindPin` 실행
2. `VirtualArduinoMicro.ConfigurePin()`에 반영되는지 확인
3. `FunctionDigitalRead(leftSensor)`가 새 pin 기준으로 읽히는지 확인
4. `analogWrite`가 새 motor pin 기준으로 동작하는지 확인

## 13.4 충돌 테스트

1. 같은 role 두 번 bind
2. 같은 pin을 두 role에 bind
3. 범위 밖 pin
4. role 누락

---

## 14. 권장 마일스톤

### 마일스톤 1

- 블록 prefab + XML 저장/복원

### 마일스톤 2

- XML -> JSON `bindPin` 변환

### 마일스톤 3

- `BlockCodeExecutor` 실행

### 마일스톤 4

- `VirtualArduinoMicro` explicit mapping 우선화

### 마일스톤 5

- validation / warning / 회귀 테스트

---

## 15. 구현 우선순위 결론

가장 추천하는 실제 작업 순서는 아래다.

1. `BindPin` 블록 UI와 prefab 추가
2. XML 저장/복원 확인
3. `BE2XmlToRuntimeJson`에 `ParseBindPinBlock` 추가
4. init 순서 보존 로직에 `bindPin` 포함
5. `BlockCodeExecutor`에 `bindPin` 노드 파싱 및 `ExecuteBindPin()` 추가
6. `VirtualArduinoMicro`에서 explicit binding 우선 구조 반영
7. legacy 값 기반 자동 매핑을 fallback으로 축소
8. 테스트 및 warning 정리

이 순서가 현재 코드 구조를 가장 적게 흔들면서도 `PinPlan`의 방향을 실제 런타임에 안전하게 반영하는 순서다.
