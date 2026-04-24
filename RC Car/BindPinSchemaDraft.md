# BindPin Schema Draft

## 1. 목적

이 문서는 [PinPlan.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/PinPlan.md:1>)과 [BindPinBlockDesign.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/BindPinBlockDesign.md:1>)를 기준으로,
`bindPin` 블록이 저장/변환/실행 과정에서 어떤 XML/JSON 구조를 가져야 하는지 초안으로 정리한다.

---

## 2. 기본 원칙

### 원칙 1

기존 BlocksEngine2 직렬화 구조를 최대한 재사용한다.

### 원칙 2

role은 문자열 입력이 아니라 dropdown 입력이지만,
XML/JSON에는 안정적인 내부 role id로 저장한다.

### 원칙 3

pin 입력은 아래 둘 다 지원한다.

- 숫자 literal
- 변수 참조

---

## 3. 권장 블록 이름

권장 XML blockName:

- `Block Ins BindPin`

선택적 커스텀 이름:

- `Block Cst BindPin`

`BE2XmlToRuntimeJson.blockParsers`에는 둘 다 등록하는 것이 좋다.

---

## 4. XML 저장 초안

현재 `BE2_BlockXML` 구조상 일반 블록은 아래 필드를 가진다.

- `blockName`
- `position`
- `varManagerName`
- `varName`
- `defineID`
- `isLocalVar`
- `defineItems`
- `sections`
- `OuterArea`

`bindPin`도 이 틀을 그대로 따른다.

---

## 4.1 XML 예시: pin literal

```xml
<Block>
  <blockName>Block Ins BindPin</blockName>
  <position>(0,0,0)</position>
  <varManagerName></varManagerName>
  <varName></varName>
  <defineID></defineID>
  <isLocalVar></isLocalVar>
  <defineItems />
  <sections>
    <Section>
      <childBlocks />
      <inputs>
        <Input>
          <isOperation>false</isOperation>
          <value>leftSensor</value>
        </Input>
        <Input>
          <isOperation>false</isOperation>
          <value>3</value>
        </Input>
      </inputs>
    </Section>
  </sections>
  <OuterArea>
    <childBlocks />
  </OuterArea>
</Block>
```

의미:

- 첫 번째 input: role
- 두 번째 input: pin literal

---

## 4.2 XML 예시: pin variable

```xml
<Block>
  <blockName>Block Ins BindPin</blockName>
  <position>(0,0,0)</position>
  <varManagerName></varManagerName>
  <varName></varName>
  <defineID></defineID>
  <isLocalVar></isLocalVar>
  <defineItems />
  <sections>
    <Section>
      <childBlocks />
      <inputs>
        <Input>
          <isOperation>false</isOperation>
          <value>leftMotorF</value>
        </Input>
        <Input>
          <isOperation>true</isOperation>
          <value>lwf</value>
          <operation>
            <Block>
              <blockName>Block Op Variable</blockName>
              <position>(0,0,0)</position>
              <varManagerName></varManagerName>
              <varName>lwf</varName>
              <defineID></defineID>
              <isLocalVar></isLocalVar>
              <defineItems />
              <sections />
              <OuterArea>
                <childBlocks />
              </OuterArea>
            </Block>
          </operation>
        </Input>
      </inputs>
    </Section>
  </sections>
  <OuterArea>
    <childBlocks />
  </OuterArea>
</Block>
```

의미:

- role은 `leftMotorF`
- pin은 변수 `lwf`

---

## 5. XML 파싱 규칙 초안

`BE2XmlToRuntimeJson`에서 `bindPin`을 읽을 때 권장 규칙은 아래다.

### 첫 번째 input

- 역할(role)
- 무조건 string으로 읽는다

### 두 번째 input

- `isOperation == true` 이고 operation 블록이 variable 계열이면 `pinVar`
- 아니면 숫자로 parse 가능하면 `pin`
- 둘 다 아니면 validation warning

---

## 6. Runtime JSON 노드 초안

권장 최소 JSON 구조:

```json
{
  "type": "bindPin",
  "role": "leftSensor",
  "pinVar": "sensorLeftPin"
}
```

또는

```json
{
  "type": "bindPin",
  "role": "rightMotorF",
  "pin": 6
}
```

### 필드 정의

- `type`
  - 고정값: `bindPin`
- `role`
  - 고정 role id 문자열
- `pin`
  - 숫자 literal 핀 번호
- `pinVar`
  - 변수 참조 핀 이름

규칙:

- `pinVar`가 있으면 `pinVar` 우선
- 없으면 `pin` 사용

---

## 7. role 허용값 초안

1차 구현에서 권장하는 role 집합:

- `leftSensor`
- `rightSensor`
- `leftSensor2`
- `rightSensor2`
- `leftMotorF`
- `leftMotorB`
- `rightMotorF`
- `rightMotorB`

이 role 값은 JSON/런타임 내부 식별자이고,
UI에서는 한글 dropdown label을 써도 된다.

---

## 8. init 배치 초안

`bindPin`은 `loop`가 아니라 `init`에 들어가는 것이 맞다.

권장 JSON 예시:

```json
{
  "init": [
    { "type": "setVariable", "setVarName": "sensorLeftPin", "setVarValue": 3 },
    { "type": "setVariable", "setVarName": "sensorRightPin", "setVarValue": 4 },
    { "type": "setVariable", "setVarName": "lwf", "setVarValue": 9 },
    { "type": "setVariable", "setVarName": "lwb", "setVarValue": 11 },
    { "type": "setVariable", "setVarName": "rwf", "setVarValue": 6 },
    { "type": "setVariable", "setVarName": "rwb", "setVarValue": 10 },

    { "type": "bindPin", "role": "leftSensor", "pinVar": "sensorLeftPin" },
    { "type": "bindPin", "role": "rightSensor", "pinVar": "sensorRightPin" },
    { "type": "bindPin", "role": "leftMotorF", "pinVar": "lwf" },
    { "type": "bindPin", "role": "leftMotorB", "pinVar": "lwb" },
    { "type": "bindPin", "role": "rightMotorF", "pinVar": "rwf" },
    { "type": "bindPin", "role": "rightMotorB", "pinVar": "rwb" }
  ],
  "loop": [],
  "functions": []
}
```

---

## 9. `BE2XmlToRuntimeJson` 초안 반영 방향

권장 변경 포인트:

- `blockParsers`에 `BindPin` 등록
- `ParseBindPinBlock(XElement block)` 추가
- `LoopBlockNode`에 아래 필드 추가
  - `role`
  - `pin`
  - `pinVar`
- `LoopBlockNodeToJson()`에 `bindPin` case 추가

주의:

현재 `analogWrite`도 `pin/pinVar`를 쓰므로,
`bindPin`에서 같은 필드명을 재사용하는 건 괜찮다.
다만 의미가 다르므로 `role` 필드가 반드시 있어야 한다.

---

## 10. `init` 순서 보존 초안

기존 `BuildJson(initBlocks, loopBlocks, functions, initActionBlocks)` 구조를 보면,
init에서 실행 순서를 보존하는 노드 리스트가 따로 있다.

따라서 `bindPin`도 아래 그룹에 포함되는 것이 맞다.

- `setVariable`
- `bindPin`
- `wait`

권장 방향:

- `ProcessInitWaitBlocks`를 `ProcessInitSequenceBlocks` 같은 이름으로 확장
- `bindPin`을 init 실행 순서 안에 넣기

---

## 11. `BlockCodeExecutor` JSON 파싱 초안

`BlockNode`에 아래 필드를 추가하는 안을 권장한다.

```csharp
public string role;
public int pin;
public string pinVar;
```

이미 `pin/pinVar`는 존재하므로,
실제로는 `role`만 추가하고 `bindPin`에서도 기존 `pin/pinVar`를 재사용해도 된다.

`ParseNode()`에서 필요한 key:

- `role`
- `pin`
- `pinVar`

---

## 12. backward compatibility

`bindPin`은 새 기능이므로 기존 저장본과 충돌하지 않아야 한다.

권장 원칙:

- 기존 JSON에 `bindPin`이 없어도 정상 동작
- `bindPin`이 있으면 explicit mapping을 우선
- explicit mapping이 없을 때만 legacy fallback 고려

즉, 새 스키마는 additive change여야 한다.

---

## 13. validation 초안

JSON 로드 시 아래 검증을 권장한다.

- `role`이 허용 목록에 있는가
- `pinVar` 또는 `pin` 중 하나는 있는가
- `pin`이 있으면 정수 범위인가

warning 예시:

- `bindPin node missing role`
- `bindPin node missing pin source`
- `bindPin role is duplicated`
- `bindPin pin is out of range`

---

## 14. 최종 권장 스키마

1차 구현용으로는 아래 형태가 가장 단순하고 충분하다.

```json
{ "type": "bindPin", "role": "leftSensor", "pinVar": "sensorLeftPin" }
{ "type": "bindPin", "role": "rightMotorF", "pin": 6 }
```

이 스키마는:

- 기존 serializer 구조에 맞고
- literal/variable 둘 다 지원하고
- 역할을 명시적으로 담을 수 있고
- runtime parser도 단순하게 유지할 수 있다.
