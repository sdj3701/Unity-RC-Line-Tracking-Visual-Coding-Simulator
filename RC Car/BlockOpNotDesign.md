# Block Op Not 설계 문서

## 1. 목적
이 문서는 `Block Op Not` 블록을 바로 코드로 추가하지 않고, 현재 프로젝트의 런타임 파이프라인 기준으로:

- XML에서 어떻게 읽을지
- JSON에 어떤 형태로 담을지
- `BlockCodeExecutor`가 어떻게 해석할지
- 최종적으로 RC카 동작에 어떻게 반영할지

를 먼저 고정하기 위한 설계 문서다.

핵심 목표는 `Not` 블록의 런타임 의미를 코드의 `!`와 동일하게 맞추는 것이다.

---

## 2. 현재 구조 기준 사실

### 2.1 XML 직렬화 구조
현재 BlocksEngine2 저장 구조는 `BE2_BlocksSerializer`와 `BE2_BlockXML` 기준으로 다음 규칙을 사용한다.

- 각 top-level 블록은 XML 한 덩어리로 저장된다.
- 여러 블록은 `#` 구분자로 이어 붙는다.
- 입력 슬롯에 Operation 블록이 꽂히면 다음 구조로 저장된다.

```xml
<Input>
  <isOperation>true</isOperation>
  <value>...</value>
  <operation>
    <Block>
      <blockName>Block Op Not</blockName>
      ...
    </Block>
  </operation>
</Input>
```

즉, `Not` 블록은 XML 스키마를 새로 만들 필요 없이 기존 `Input -> operation -> Block` 구조 안에 자연스럽게 들어갈 수 있다.

### 2.2 XML -> Runtime JSON 변환 구조
현재 `BE2XmlToRuntimeJson`는 다음 방식으로 동작한다.

- `WhenPlayClicked`를 main trigger로 선택한다.
- `SetVariable`을 `init`으로 만든다.
- `Loop`, `If`, `IfElse`, `Block_Read`, `Wait`, `CallFunction`, `PWM` 등을 `loop/functions`용 JSON 노드로 만든다.
- `if/ifElse` 조건은 현재 평면 필드로 관리한다.

현재 조건 관련 필드는 사실상 아래 집합이다.

- `conditionLogicalOp`
- `conditionLeftSensorFunction`
- `conditionRightSensorFunction`
- `conditionSensorFunction`
- `conditionVar`
- `conditionPin`
- `conditionValue`

중요한 점은 현재 구조가 `And/Or`는 읽지만 `Not` 전용 필드는 없다는 것이다.

### 2.3 런타임 실행 구조
현재 `BlockCodeExecutor`는 JSON을 수동 파싱하고, `ExecuteIfBlock()`에서 조건을 평가한다.

평가 우선순위는 아래와 같다.

1. `conditionLogicalOp`가 있으면 `and/or` 평가
2. 아니면 `conditionVar` 또는 `conditionPin`으로 `DigitalRead(pin)` 평가
3. 아니면 `conditionSensorFunction`으로 `FunctionDigitalRead(sensorFunction)` 평가
4. 아무 조건 소스가 없으면 `conditionValue` 자체를 bool로 사용

즉, `Not`는 이 평가 결과를 뒤집는 위치에 들어가야 가장 안전하다.

---

## 3. `Block Op Not`의 런타임 의미

### 3.1 반드시 고정할 의미
RC카 런타임에서 `Block Op Not`은 아래 의미로만 사용한다.

- 의미: 논리 부정
- 코드 표현: `!`
- 피연산자 개수: 1개
- 결과 타입: bool

예시:

- `leftSensor` -> `leftSensor`
- `Not(leftSensor)` -> `!leftSensor`
- `Not(And(leftSensor, rightSensor))` -> `!(leftSensor && rightSensor)`

### 3.2 사용하지 말아야 하는 의미
현재 `BE2_Op_Not` 원본 구현은 불리언 외에 아래 동작도 포함한다.

- `"true"` / `"1"` -> `"false"` / `"0"`
- 문자열 -> 문자열 뒤집기
- 숫자 -> `-1` 곱하기

이 의미를 RC카 런타임에 그대로 들여오면 조건식 의미가 섞인다.
따라서 RC카용 XML/JSON/Executor 문맥에서는 `Not`를 불리언 부정으로만 제한하는 것이 맞다.

권장 범위:

- 센서 조건
- 핀 조건
- 불리언 변수 조건
- 논리식 전체 부정

비권장 범위:

- 문자열 처리용 `Not`
- 숫자 부호 반전용 `Not`
- `setVariable` 우변 일반 수식으로서의 `Not`

---

## 4. 권장 설계안

## 4.1 결론
1차 구현은 **XML 구조는 그대로 유지**하고, JSON과 런타임 평가에 `conditionNot` 플래그를 추가하는 방식이 가장 안전하다.

권장 JSON 확장:

```json
"conditionNot": true
```

이 플래그는 `if/ifElse`의 **최종 조건 결과를 뒤집는 의미**로 사용한다.

### 4.2 이 방식이 맞는 이유

- 기존 XML 저장 구조를 바꿀 필요가 없다.
- 기존 `BlockCodeExecutor`의 평면 조건 구조를 유지할 수 있다.
- `!sensor`, `!pin`, `!variable` 같은 단순 패턴을 바로 수용할 수 있다.
- `!(A && B)`, `!(A || B)`도 외곽 `Not`이면 같은 방식으로 표현 가능하다.
- 나중에 코드 문자열을 만들 때도 `conditionNot == true`면 앞에 `!`를 붙이면 된다.

---

## 5. XML 읽기 규칙

## 5.1 읽는 위치
`Not`는 일반 loop 블록이 아니라, 주로 `If` / `IfElse`의 조건 입력 슬롯 안에 들어오는 Operation 블록으로 읽는다.

즉, 읽는 시작점은 `ParseIfBlock()`가 찾는 `opBlock`이다.

### 5.2 읽기 순서
권장 읽기 순서는 아래와 같다.

1. `if/ifElse`의 조건 input에서 최상위 `opBlock`을 찾는다.
2. 최상위 `opBlock.blockName`이 `Block Op Not`인지 확인한다.
3. 맞으면 현재 조건 노드에 `conditionNot = true`를 기록한다.
4. `Not`의 첫 번째 입력 안에 있는 내부 operation 또는 값을 실제 피연산자로 본다.
5. 그 내부 피연산자에 대해 기존 규칙을 그대로 재사용한다.

즉, `Not`는 스스로 조건을 평가하지 않고, **내부 피연산자를 먼저 기존 규칙대로 해석한 뒤 최종 결과만 뒤집는 래퍼**로 취급한다.

### 5.3 XML 해석 예시 1: `if (!leftSensor)`

핵심 구조만 단순화하면 다음과 같다.

```xml
<Block>
  <blockName>Block Ins If</blockName>
  <sections>
    <Section>
      <childBlocks>
        ...
      </childBlocks>
      <inputs>
        <Input>
          <isOperation>true</isOperation>
          <value>true</value>
          <operation>
            <Block>
              <blockName>Block Op Not</blockName>
              <sections>
                <Section>
                  <childBlocks />
                  <inputs>
                    <Input>
                      <isOperation>true</isOperation>
                      <value>true</value>
                      <operation>
                        <Block>
                          <blockName>Block Cst Block_Read</blockName>
                          ...
                        </Block>
                      </operation>
                    </Input>
                  </inputs>
                </Section>
              </sections>
            </Block>
          </operation>
        </Input>
      </inputs>
    </Section>
  </sections>
</Block>
```

이 경우 읽기 결과는 아래처럼 정리하면 된다.

- 최상위 조건은 `Block Op Not`
- `conditionNot = true`
- 내부 피연산자는 `Block_Read(leftSensor)`
- 기존 센서 조건 파서를 재사용

### 5.4 XML 해석 예시 2: `if (!(leftSensor && rightSensor))`

이 경우도 원칙은 같다.

- 최상위는 `Block Op Not`
- 내부 피연산자는 `Block Op And`
- 내부 `And`는 기존 `TryParseLogicalCondition()` 규칙으로 읽음
- 마지막에 `conditionNot = true`

---

## 6. JSON 설계

## 6.1 1차 구현용 권장 스키마

`if/ifElse` 노드에 아래 필드를 추가한다.

```json
"conditionNot": true
```

나머지 조건 필드는 기존 구조를 그대로 사용한다.

예상되는 조건 관련 필드 집합:

- `conditionNot`
- `conditionLogicalOp`
- `conditionLeftSensorFunction`
- `conditionRightSensorFunction`
- `conditionSensorFunction`
- `conditionVar`
- `conditionPin`
- `conditionValue`

### 6.2 JSON 예시 1: `if (!leftSensor)`

```json
{
  "type": "if",
  "conditionNot": true,
  "conditionSensorFunction": "leftSensor",
  "conditionValue": 1,
  "body": [
    { "type": "analogWrite", "pinVar": "pin_wheel_left_forward", "valueVar": "go" }
  ]
}
```

해석 규칙:

1. base condition = `leftSensor == 1`
2. final condition = `!base condition`

주의:
현재 구조를 유지하기 위해 `conditionValue`는 그대로 남겨둔다.
센서의 흰색/검은색 비교 규칙을 유지한 뒤 마지막에만 반전한다.

### 6.3 JSON 예시 2: `if (!(leftSensor && rightSensor))`

```json
{
  "type": "if",
  "conditionNot": true,
  "conditionLogicalOp": "and",
  "conditionLeftSensorFunction": "leftSensor",
  "conditionRightSensorFunction": "rightSensor",
  "conditionValue": 1,
  "body": [
    { "type": "callFunction", "functionName": "turnLeft", "args": [150] }
  ]
}
```

해석 규칙:

1. base condition = `leftSensor && rightSensor`
2. final condition = `!base condition`

현재 logical 조건은 원래도 `conditionValue`를 실질적으로 쓰지 않으므로, 여기서도 `conditionNot`만 최종 반전에 사용하면 된다.

### 6.4 JSON 예시 3: `if (!digitalRead(pin_sensor_left))`

```json
{
  "type": "if",
  "conditionNot": true,
  "conditionVar": "pin_sensor_left",
  "conditionValue": 1,
  "body": [
    { "type": "analogWrite", "pinVar": "pin_wheel_right_forward", "valueVar": "go" }
  ]
}
```

해석 규칙:

1. base condition = `DigitalRead(pin_sensor_left) == 1`
2. final condition = `!base condition`

---

## 7. `BlockCodeExecutor` 읽기 규칙

## 7.1 JSON 파싱 규칙
런타임 JSON 로더는 `if/ifElse` 노드에서 `conditionNot`를 읽을 수 있어야 한다.

의미는 단순하다.

- `false` 또는 없음: 기존과 동일
- `true`: 기존 조건 평가 결과를 마지막에 한 번 반전

### 7.2 실행 규칙
권장 실행 순서는 아래와 같다.

1. 기존 우선순위대로 base condition 계산
2. `conditionNot == true`이면 `condition = !condition`
3. 최종 `condition`으로 `body/elseBody` 분기

즉, `Not`는 새 센서 읽기 방식이나 새 핀 읽기 방식을 만드는 것이 아니라, **기존 판단 결과를 뒤집는 후처리 단계**다.

### 7.3 bool 변환 기준
RC카 런타임에서는 아래 값을 bool 원천으로 허용하는 것이 좋다.

- 센서 함수 결과 (`FunctionDigitalRead`)
- 핀 읽기 결과 (`DigitalRead`)
- 이미 bool처럼 쓰는 변수 (`0/1`, `true/false`)

권장하지 않는 입력:

- 임의 문자열
- 일반 숫자 계산 결과

이유는 현재 RC카 런타임의 목적이 모터/센서 제어용 조건식이기 때문이다.

---

## 8. RC카 적용 흐름

`Not`가 실제 차량 동작에 반영되는 흐름은 아래와 같다.

```text
블록 저장
  -> XML 생성
  -> XML 안의 Block Op Not 확인
  -> Runtime JSON 생성 (conditionNot 포함)
  -> BlockCodeExecutor.LoadProgramFromJson()
  -> Tick()에서 if 조건 평가
  -> conditionNot가 최종 조건 반전
  -> true가 된 body가 analogWrite/callFunction 실행
  -> VirtualArduinoMicro에 전달
  -> VirtualMotorDriver 반영
  -> VirtualCarPhysics.FixedUpdate()에서 차량 이동
```

핵심은 `Not`가 직접 모터를 만지지 않는다는 점이다.
`Not`는 어디까지나 **분기 결과를 바꾸는 연산자**이고, 그 분기 안에 들어 있는 기존 모터 제어 블록이 실제 RC카 동작을 바꾼다.

---

## 9. 지원 범위 정의

## 9.1 1차 구현에서 지원 권장

- `if (!leftSensor)`
- `if (!rightSensor)`
- `if (!digitalRead(pin))`
- `if (!(leftSensor && rightSensor))`
- `if (!(leftSensor || rightSensor))`

### 9.2 1차 구현에서 제외 권장

- `if (leftSensor && !rightSensor)`
- `if (!leftSensor && rightSensor)`
- `if (!!leftSensor)`
- `setVariable x = !x`
- `analogWrite(pin, !value)`

제외 이유:
현재 JSON 구조가 평면형이라 **조건식 내부의 부분 트리**를 표현할 수 없기 때문이다.

---

## 10. 코드 문자열 변환 규칙

향후 코드 미리보기 또는 코드 생성 문자열을 만들 때는 아래 규칙을 권장한다.

- 단일 피연산자: `!operand`
- 논리식 피연산자: `!(left && right)` 또는 `!(left || right)`
- 괄호는 논리식일 때 반드시 붙인다.

예시:

- `leftSensor` -> `leftSensor`
- `Not(leftSensor)` -> `!leftSensor`
- `Not(And(leftSensor, rightSensor))` -> `!(leftSensor && rightSensor)`
- `Not(Or(leftSensor, rightSensor))` -> `!(leftSensor || rightSensor)`

---

## 11. 구현 체크리스트

코드 작업 전 체크해야 할 항목은 아래와 같다.

1. `Block Op Not`를 RC카 런타임에서는 bool 전용으로 제한할지 확정
2. `BE2XmlToRuntimeJson`의 `if/ifElse` 조건 파서에 `Not` 감지 규칙 추가
3. JSON 스키마에 `conditionNot` 추가
4. `BlockCodeExecutor` JSON 파서에 `conditionNot` 추가
5. `ExecuteIfBlock()`에서 base condition 계산 후 최종 반전 추가
6. Save/Load 후 XML -> JSON -> Executor 경로가 모두 유지되는지 확인
7. 센서 조건과 핀 조건에서 반전이 실제 차량 움직임에 맞게 동작하는지 확인

---

## 12. 테스트 케이스

최소 검증 케이스는 아래가 좋다.

### 12.1 저장/로드 검증

- `Not(leftSensor)` 블록을 저장 후 다시 로드했을 때 UI 구조가 유지되는지
- JSON에 `conditionNot: true`가 정상 포함되는지

### 12.2 실행 검증

- `if (!leftSensor)`일 때 기존 `if (leftSensor)`와 반대 분기가 실행되는지
- `if (!(leftSensor && rightSensor))`일 때 양쪽 센서가 모두 true가 아닐 때만 실행되는지
- `if (!digitalRead(pin_sensor_left))`일 때 pin 조건이 뒤집혀 실행되는지

### 12.3 차량 동작 검증

- `!leftSensor` 조건에서 좌회전/우회전 함수가 기대대로 반대로 호출되는지
- `!(leftSensor && rightSensor)` 조건에서 교차점/양쪽 감지 상황의 분기가 기대대로 바뀌는지
- `wait`, `callFunction`, `analogWrite`와 섞여도 기존 흐름이 깨지지 않는지

---

## 13. 향후 확장안

만약 나중에 아래 패턴까지 지원해야 한다면:

- `leftSensor && !rightSensor`
- `!leftSensor || rightSensor`
- `!(A && (!B || C))`

평면 필드 방식만으로는 부족하다.
그때는 `conditionExpr` 같은 **트리형 JSON 스키마**로 넘어가야 한다.

예시:

```json
{
  "type": "if",
  "conditionExpr": {
    "op": "and",
    "left": { "type": "sensor", "value": "leftSensor" },
    "right": {
      "op": "not",
      "arg": { "type": "sensor", "value": "rightSensor" }
    }
  },
  "body": []
}
```

하지만 현재 프로젝트 상태에서는 이 확장안보다, `conditionNot` 1개를 추가하는 1차 구현이 훨씬 리스크가 낮다.

---

## 14. 실무 권장 수정

실제 운영 중 확인된 문제를 기준으로, 아래 규칙을 추가로 권장한다.

### 14.1 JSON 키 이름 통일

- 공식 키는 `conditionNot`로 통일한다.
- 과거 저장 데이터나 수동 JSON에는 `conditionNegate`가 들어 있을 수 있다.
- 런타임은 하위 호환을 위해 `conditionNegate`도 읽을 수 있게 유지하되, 새로 저장하거나 문서화할 때는 `conditionNot`만 사용한다.

예시:

```json
{
  "type": "ifElse",
  "conditionSensorFunction": "rightSensor",
  "conditionNot": true,
  "conditionValue": 1,
  "body": [],
  "elseBody": []
}
```

### 14.2 센서 함수 이름은 예약어로 취급

아래 이름은 변수명이 아니라 센서 함수명으로 예약하는 것을 권장한다.

- `leftSensor`
- `rightSensor`
- `leftSensor2`
- `rightSensor2`

즉, 핀 번호나 설정값을 저장하는 변수는 아래처럼 별도 이름을 사용한다.

- `pin_left_sensor`
- `pin_right_sensor`
- `pin_left_sensor2`
- `pin_right_sensor2`

권장하지 않는 예:

```json
{ "type": "setVariable", "setVarName": "rightSensor", "setVarValue": 4 }
```

권장 예:

```json
{ "type": "setVariable", "setVarName": "pin_right_sensor", "setVarValue": 4 }
```

이 규칙을 두는 이유는 `conditionSensorFunction: "rightSensor"`가 항상 실제 센서를 읽는 의미로 고정되어야 하기 때문이다.

### 14.3 `and/or`는 비교식이 아니라 논리식

현재 `conditionLogicalOp`의 `and`, `or`는 아래 의미만 가진다.

- `and`: 좌항과 우항을 bool로 해석한 뒤 AND
- `or`: 좌항과 우항을 bool로 해석한 뒤 OR

따라서 아래와 같은 표현은 비교식이 아니다.

```json
{
  "type": "ifElse",
  "conditionLogicalOp": "and",
  "conditionLeftSensorFunction": "dir",
  "conditionRightSensorFunction": "999"
}
```

위 표현은 `dir == 999`가 아니라, 사실상 `bool(dir) && true`로 해석된다.

즉, `dir == 999` 같은 상태 비교는 현재 1차 스키마로는 정확히 표현할 수 없다.
이 경우는 향후 아래 같은 별도 비교 스키마로 확장하는 것이 맞다.

```json
{
  "type": "ifElse",
  "conditionCompareOp": "equal",
  "conditionLeftValue": "dir",
  "conditionRightValue": "999",
  "body": [],
  "elseBody": []
}
```

### 14.4 이번 코드 개선에 반영한 항목

이번 런타임 개선에서는 아래를 반영한다.

1. `conditionNegate`를 `conditionNot`의 하위 호환 alias로 허용
2. `leftSensor/rightSensor` 계열 예약 이름은 변수보다 실제 센서 해석을 우선

단, `dir == 999` 같은 비교식은 아직 별도 스키마가 없으므로 문서상 제한으로 유지한다.

---

## 15. 최종 권장안

현재 프로젝트 기준 최종 권장안은 아래 한 줄로 정리된다.

**`Block Op Not`는 XML에서는 기존 operation 중첩 구조로 읽고, Runtime JSON에서는 `conditionNot: true`로 보존하며, `BlockCodeExecutor`는 기존 조건을 먼저 계산한 뒤 마지막에 `!`를 적용한다.**

이 방식이면:

- XML 저장 포맷을 깨지 않는다.
- 현재 JSON 구조를 크게 흔들지 않는다.
- RC카 센서/핀 조건에 바로 적용 가능하다.
- 코드 문자열로 바꿀 때도 `!` 의미를 그대로 유지할 수 있다.
