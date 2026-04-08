# BlockCode Logic Document

## 1) 목적
이 문서는 **블록 코드가 이미 만들어져 있고 저장/불러오기가 가능한 상태**를 전제로, 아래 흐름을 한 번에 이해하도록 정리한 문서다.

1. 블록 UI 코드가 XML로 직렬화되는 과정
2. XML이 런타임 JSON으로 변환되는 과정
3. JSON이 `BlockCodeExecutor`에서 실행되는 과정
4. 실행 중 핀 매핑과 센서/모터 입출력이 연결되는 과정

---

## 2) 핵심 컴포넌트

- `BE2_UI_ContextMenuManager`
- `BE2_CodeExporter`
- `BE2_BlocksSerializer`
- `BE2XmlToRuntimeJson`
- `BlockCodeExecutor`
- `VirtualArduinoMicro`
- `VirtualCarPhysics`
- (저장소) `BE2_CodeStorageManager`, `LocalStorageProvider`/`DatabaseStorageProvider`

---

## 3) 전체 흐름 한눈에 보기

```text
[사용자 Save/Load]
      |
      v
BE2_UI_ContextMenuManager
  - XML 생성 (BE2_CodeExporter)
  - XML -> JSON 변환 (BE2XmlToRuntimeJson)
  - LatestRuntimeJson 갱신
  - 저장소 저장 (XML/JSON)
  - OnCodeGenerated 이벤트 발행
      |
      +--> BlockCodeExecutor.ReloadProgram()
      |      - LatestRuntimeJson 우선 로드
      |      - init/loop/functions 파싱
      |
      +--> VirtualArduinoMicro.UpdatePinMappingFromVariables()
             - 변수값(핀 번호) 기반 핀-기능 매핑 갱신

[실행 시작]
VirtualCarPhysics.FixedUpdate()
  -> BlockCodeExecutor.Tick()
  -> VirtualArduinoMicro.AnalogWrite/DigitalRead/FunctionDigitalRead
  -> VirtualMotorDriver / VirtualLineSensor 반영
```

---

## 4) 저장/불러오기 상세 시퀀스

## 4.1 Save (권장 경로: Async)

1. `BE2_UI_ContextMenuManager.SaveCodeWithNameAsync(fileName)` 호출
2. `BE2_CodeExporter.GenerateXmlFromAllEnvs()`로 XML 문자열 생성
3. `BE2XmlToRuntimeJson.ExportToString(xml)`로 JSON 문자열 생성
4. `BE2_UI_ContextMenuManager.LatestRuntimeJson = json` 갱신
5. `BE2_CodeStorageManager.SaveCodeAsync(...)` 저장
6. 저장 성공 시 `OnCodeGenerated` 이벤트 발행
7. 이벤트 구독자 실행
8. `BlockCodeExecutor`는 프로그램 리로드
9. `VirtualArduinoMicro`는 변수 기반 핀 매핑 리빌드

## 4.2 Load (권장 경로: Async)

1. `BE2_UI_ContextMenuManager.LoadCodeFromFileAsync(fileName)` 호출
2. 저장소에서 XML 로드
3. `BE2_BlocksSerializer.XMLToBlocksCode(xml, env)`로 UI 블록 복원
4. XML로부터 JSON을 다시 생성 (`ExportToString`)
5. `LatestRuntimeJson` 갱신
6. `OnCodeGenerated` 이벤트 발행
7. 실행기/아두이노가 동시에 갱신

## 4.3 Legacy 경로

- `CodeGenerated()`는 `persistentDataPath/BlocksRuntime.xml`과 `BlocksRuntime.json` 파일을 직접 갱신한다.
- 신경로(Async)도 `LatestRuntimeJson`을 우선 사용하므로, 런타임은 파일 의존을 줄인 구조다.

## 4.4 저장 위치

- 사용자 코드 저장본:
- `persistentDataPath/SavedCodes/<fileName>.xml`
- `persistentDataPath/SavedCodes/<fileName>.json`
- 런타임 동기화 파일:
- `persistentDataPath/BlocksRuntime.xml`
- `persistentDataPath/BlocksRuntime.json`
- 메모리 캐시:
- `BE2_UI_ContextMenuManager.LatestRuntimeJson`

---

## 5) XML 포맷 정리

## 5.1 블록 단위 직렬화 + `#` 구분자

- `BE2_BlocksSerializer.BlocksCodeToXML()`는 **top-level 블록마다 XML 조각**을 만들고
- 각 조각 끝에 `\n#\n`를 붙인다.
- 즉, 전체 XML 문자열은 `#`로 split 가능한 다중 블록 스트림이다.

예시:

```xml
<Block>
  <blockName>Block Ins WhenPlayClicked</blockName>
  <sections>...</sections>
  <OuterArea>...</OuterArea>
</Block>
#
<Block>
  <blockName>Block Ins DefineFunction</blockName>
  <defineID>TurnLeft</defineID>
  <defineItems>...</defineItems>
  <sections>...</sections>
</Block>
#
```

## 5.2 XML 역직렬화 (`XMLToBlocksCode`)

`BE2_BlocksSerializer.XMLToBlocksCode`는 3패스 동작:

1. 변수 등록 패스
2. DefineFunction 블록 선 생성
3. 나머지 블록 생성

이 순서 덕분에 함수/변수 참조 블록이 복원될 때 의존성이 먼저 준비된다.

---

## 6) XML -> JSON 변환 로직 (`BE2XmlToRuntimeJson`)

## 6.1 변환 개요

`ExportToString(xmlText)`는 아래 순서로 JSON을 만든다.

1. XML을 `#` 기준으로 청크 분리
2. 함수 정의 후보 수집
3. `WhenPlayClicked` 후보 중 child block 수가 가장 큰 블록을 main trigger로 선택
4. init 변수 수집 (`setVariable`)
5. init action 수집 (`setVariable`, `wait`)
6. loop 수집 (`Loop` 내부 블록만)
7. 실제 호출된 함수만 파싱 후 `functions` 생성
8. `init/loop/functions`를 합쳐 JSON 출력

## 6.2 지원 블록 -> 런타임 노드 매핑

- `Block *_SetVariable` -> `type: "setVariable"`
- `Block *_Block_pWM` -> `type: "analogWrite"`
- `Block *_Block_Read` -> `type: "analogRead"` (센서 함수명 정규화 포함)
- `Block *_If` -> `type: "if"`
- `Block *_IfElse` -> `type: "ifElse"`
- `Block *_CallFunction`/`FunctionBlock` -> `type: "callFunction"`
- `Block *_Wait` -> `type: "wait"`

## 6.3 조건식 처리 핵심

- `if/ifElse`는 우선순위가 있다.
- 논리식(`and/or`)이면 `conditionLogicalOp + 좌/우 피연산자`를 JSON으로 기록
- 단일 센서식이면 `conditionSensorFunction` 기록
- 핀 기반 digitalRead 식이면 `conditionVar` 또는 `conditionPin` 기록

즉, 현재 구조는 센서 함수 문자열 방식과 핀 기반 방식을 모두 지원한다.

---

## 7) JSON 스키마 정리

루트 구조:

```json
{
  "init": [ ... ],
  "loop": [ ... ],
  "functions": [ ... ]
}
```

주요 노드 예시:

```json
{ "type": "setVariable", "setVarName": "pin_sensor_left", "setVarValue": 3 }
{ "type": "analogWrite", "pinVar": "pin_wheel_left_forward", "valueVar": "go" }
{ "type": "analogRead", "sensorFunction": "leftSensor", "targetVar": "lineLeft" }
{
  "type": "if",
  "conditionVar": "pin_sensor_left",
  "conditionValue": 1,
  "body": [ ... ]
}
{
  "type": "ifElse",
  "conditionSensorFunction": "rightSensor2",
  "conditionValue": 0,
  "body": [ ... ],
  "elseBody": [ ... ]
}
{
  "type": "if",
  "conditionLogicalOp": "and",
  "conditionLeftSensorFunction": "leftSensor",
  "conditionRightSensorFunction": "rightSensor",
  "conditionValue": 1,
  "body": [ ... ]
}
{ "type": "callFunction", "functionName": "turnLeft", "args": [150], "argVars": [null] }
{ "type": "wait", "number": 0.2 }
```

---

## 8) 런타임 실행 로직 (`BlockCodeExecutor`)

## 8.1 로딩 우선순위

1. `LatestRuntimeJson` 메모리 캐시 우선
2. 메모리가 비어 있으면 파일 fallback (`allowFileFallbackWhenMemoryEmpty=true`일 때만)
3. 파싱 성공 후 `init`에서 변수 사전 구축

## 8.2 Tick 실행 순서

1. wait 중이면 남은 시간까지 skip
2. 첫 진입 시 `init`를 순차 실행
3. 매 tick마다 필요 시 모터 출력을 stop으로 초기화
4. `loop`를 순서대로 실행
5. `wait`를 만나면 다음 tick부터 이어서 실행

## 8.3 조건 평가 우선순위

1. `conditionLogicalOp` 존재 시: 좌/우 읽어서 논리식 평가
2. `conditionVar`/`conditionPin` 존재 시: `arduino.DigitalRead(pin)` 평가
3. `conditionSensorFunction` 존재 시: `arduino.FunctionDigitalRead(function)` 평가
4. 아무 것도 없으면 `conditionValue` 자체를 bool 처리

## 8.4 입출력 명령

- `analogWrite`: `pinVar` 우선, 없으면 `pin`
- `analogRead`: `sensorFunction`으로 읽고 `targetVar` 저장
- `callFunction`: args/argVars로 파라미터 바인딩
- `wait`: 초 단위 정지

---

## 9) 핀 매핑과 하드웨어 추상화 (`VirtualArduinoMicro`)

## 9.1 기본 매핑

기본 기능 맵은 다음 형태로 생성된다.

- 센서: `leftSensor`, `rightSensor`, `leftSensor2`, `rightSensor2`
- 모터: `leftMotorF`, `leftMotorB`, `rightMotorF`, `rightMotorB`
- 기본 핀은 인스펙터 값(`default*Pin`) 기준

## 9.2 변수 기반 동적 매핑

`UpdatePinMappingFromVariables()`는:

1. `BlockCodeExecutor.Variables`를 순회
2. 변수 값(숫자)을 핀 번호로 간주
3. 기본 핀 목록과 일치하면 해당 기능으로 매핑
4. 매핑 안 된 기본 기능은 fallback으로 기본값 매핑

즉, 변수명은 자유롭게 써도 되고, 값(핀 번호)이 기본 핀과 맞으면 자동 연결된다.

## 9.3 읽기/쓰기 경로

- `DigitalRead(pin)`:
- `pin -> function` 매핑이 있으면 해당 peripheral `OnFunctionRead`
- 없으면 내부 디지털 배열 값 반환

- `AnalogWrite(pin, value)`:
- 핀 상태 저장 후 `pin -> function`으로 peripheral `OnFunctionWrite` 전달

- `FunctionDigitalRead(function)`/`FunctionAnalogRead(function)`:
- function 이름으로 peripheral 직접 호출

---

## 10) NetworkCar 확장 경로

- Host가 저장된 코드(seq)를 조회
- JSON이 비어 있고 XML만 있으면 Host에서 `ExportToString(xml)`로 즉시 변환
- `HostRuntimeBinder.TryApplyJson()` -> `executor.LoadProgramFromJson()` 주입

즉, 네트워크 경로도 결국 동일한 런타임 JSON 포맷으로 귀결된다.

---

## 11) 디버깅 체크리스트

1. Save/Load 후 `OnCodeGenerated`가 호출되는지 확인
2. `LatestRuntimeJson` 길이가 0이 아닌지 확인
3. `BlockCodeExecutor.IsLoaded`가 true인지 확인
4. `VirtualCarPhysics`가 `StartRunning()` 상태인지 확인
5. 핀 기반 조건이면 JSON에 `conditionVar`/`conditionPin`이 실제로 들어갔는지 확인
6. 센서 기반 조건이면 `conditionSensorFunction`이 `leftSensor/rightSensor...`로 정규화됐는지 확인
7. 핀 매핑 UI 색상(`PinMappingVisualizer`)이 매핑 성공(초록)으로 바뀌는지 확인

---

## 12) 요약

- XML은 블록 원본 보존용 포맷
- JSON은 런타임 실행용 포맷
- 저장/불러오기 모두 최종적으로 `LatestRuntimeJson`을 갱신
- 실행기는 JSON만 보고 deterministic하게 init/loop를 수행
- 아두이노 추상화 계층이 핀 번호/기능 이름을 주변장치로 연결

