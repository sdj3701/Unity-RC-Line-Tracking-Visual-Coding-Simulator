# Pin Plan

## 1. 목적

이 문서는 RC Car 프로젝트에서 `Pin`을 어떻게 정의하고, 일반 변수와 핀 변수를 어떻게 구분할지 정리하기 위한 설계 문서다.

이번 수정의 핵심은 하나다.

- 이전에는 `PWM`에 사용된 변수만 핀 변수처럼 보인다고 생각했다.
- 다시 확인해보면 센서도 `digitalRead` 계열의 pin 입력을 사용한다.
- 따라서 모터와 센서를 모두 포함해서, "핀 슬롯에 바인드된 변수"를 핀 변수로 볼 수 있다.

즉, 이제 기준은 `PWM인가 아닌가`가 아니라 `실제로 하드웨어 pin 슬롯에 들어갔는가`다.

---

## 2. 수정된 핵심 결론

이 문서에서 최종적으로 채택하는 판단 기준은 아래와 같다.

1. 값이 기본 핀 번호와 같다고 해서 핀 변수로 취급하지 않는다.
2. 변수명이 `leftSensor`, `rightSensor`처럼 고정되어 있다고 가정하지 않는다.
3. `analogWrite`, `digitalRead` 같은 블록의 pin 입력 슬롯에 실제로 사용된 변수만 핀 변수로 본다.
4. pin 슬롯에 한 번도 사용되지 않은 변수는 일반 변수로 본다.
5. 핀 변수 여부와 RC카에서의 역할(`leftSensor`, `rightMotorF` 등)은 분리해서 생각한다.

이렇게 보면 센서도 예외가 아니다.

- 모터는 `출력 pin 슬롯`을 사용한다.
- 센서는 `입력 pin 슬롯`을 사용한다.

둘 다 동일하게 `핀 바인드가 발생한 변수`로 분류할 수 있다.

---

## 3. 용어 정리

이 문서에서는 아래 용어를 사용한다.

### 3.1 일반 변수

일반 변수는 숫자 계산, 비교, 카운터, 속도값 저장, 상태 저장 등에 자유롭게 사용하는 변수다.

예:

- `speed = 150`
- `count = 3`
- `dir = 999`

이 변수들은 숫자값이 3, 4, 6, 9처럼 핀 번호와 우연히 같아도 핀 변수로 간주하지 않는다.

### 3.2 핀 변수

핀 변수는 하드웨어 pin 입력 슬롯에 실제로 연결되어 사용된 변수다.

예:

- `analogWrite(pinVar, value)`
- `digitalRead(pinVar)`
- 센서 읽기 블록의 pin 슬롯
- 조건 블록의 pin 입력 슬롯

즉, 핀 변수는 값으로 판별하는 것이 아니라 `사용 위치`로 판별한다.

### 3.3 출력 핀 변수

모터나 LED처럼 신호를 내보내는 쪽의 pin 슬롯에 사용된 변수다.

예:

- `analogWrite.pinVar`
- 향후 필요 시 `digitalWrite.pinVar`

### 3.4 입력 핀 변수

센서처럼 신호를 읽는 쪽의 pin 슬롯에 사용된 변수다.

예:

- `digitalRead.pinVar`
- 센서 조건 블록의 pin 입력

### 3.5 역할 바인드

이 변수/핀을 실제 RC카의 어떤 역할로 쓸지 정하는 과정이다.
이 역할 정보는 런타임이 추측하는 것이 아니라, 기본적으로 Client가 실제 배선과 의도를 보고 결정해서 저장하는 정보로 본다.

예:

- `leftSensor`
- `rightSensor`
- `leftMotorF`
- `leftMotorB`
- `rightMotorF`
- `rightMotorB`

핵심은 이것이다.

- 핀 변수 여부 판별
- 실제 역할 판별

이 둘은 같은 문제가 아니다.

---

## 4. 현재 방식의 문제

현재 `VirtualArduinoMicro.UpdatePinMappingFromVariables()`는 변수값이 기본 핀 번호와 같으면 해당 역할로 자동 매핑하는 구조를 사용한다.

예를 들어:

- `myVar = 3`
- 기본 `leftSensor` pin이 `3`

이면, `myVar`가 진짜 센서 핀 변수인지 아닌지와 무관하게 결과적으로 `Pin 3 -> leftSensor`처럼 해석될 수 있다.

이 방식의 문제는 명확하다.

- 일반 숫자 변수와 충돌한다.
- 값이 우연히 같기만 해도 핀처럼 취급된다.
- 사용자는 어떤 변수가 핀 변수로 잡히는지 직관적으로 알기 어렵다.
- 문서가 없으면 동작 규칙을 추론하기 어렵다.

즉, 값 기반 자동 매핑은 제거하는 방향이 맞다.

---

## 5. 새 기준: "핀 슬롯에 사용되었는가"

이제 핀 변수 판별 기준은 아래 하나로 정리한다.

**어떤 변수가 하드웨어 pin 슬롯에 실제로 사용되었으면 핀 변수다.**

이 기준은 모터와 센서를 모두 처리할 수 있다.

### 5.1 출력 쪽 핀 슬롯

아래 슬롯에 사용된 변수는 `출력 핀 변수` 후보로 본다.

- `analogWrite.pinVar`
- 향후 확장 시 `digitalWrite.pinVar`
- 향후 확장 시 `pinMode(pinVar, OUTPUT)` 같은 명시적 출력 설정

예:

- `lwf = 9`
- `analogWrite(lwf, go)`

위 경우 `lwf`는 일반 숫자 변수가 아니라 출력 핀 변수다.

### 5.2 입력 쪽 핀 슬롯

아래 슬롯에 사용된 변수는 `입력 핀 변수` 후보로 본다.

- `digitalRead(pinVar)`
- 센서 읽기 블록의 pin 입력
- 조건 블록에서 센서 입력 pin을 받는 슬롯
- 향후 확장 시 `pinMode(pinVar, INPUT)` 같은 명시적 입력 설정

예:

- `leftSensorPin = 3`
- `digitalRead(leftSensorPin)`

위 경우 `leftSensorPin`은 입력 핀 변수다.

### 5.3 핀 슬롯에 쓰이지 않으면 일반 변수

아래처럼 pin 슬롯에 한 번도 쓰이지 않은 변수는 일반 변수다.

- `speed = 9`
- `count = 3`
- `dir = 999`
- `turn = 150`

즉, 값이 3이나 9라는 이유만으로 핀 변수로 올라가지 않는다.

---

## 6. 이번 수정에서 가장 중요한 인식 변화

이전에는 이렇게 생각하기 쉬웠다.

- 모터는 `PWM`을 쓰니까 핀 변수 여부를 판별할 수 있다.
- 센서는 `PWM`을 안 쓰니까 일반 변수와 핀 변수를 가르기 어렵다.

하지만 이건 절반만 본 것이다.

센서는 `PWM`을 안 쓰더라도 `digitalRead`라는 입력 pin 슬롯을 사용한다.
따라서 센서도 아래처럼 동일한 방식으로 분류할 수 있다.

- `analogWrite` pin 슬롯에 쓰였다 -> 출력 핀 변수
- `digitalRead` pin 슬롯에 쓰였다 -> 입력 핀 변수

즉, 이제는 이렇게 정리하는 것이 맞다.

**모터와 센서 모두 "핀 바인드가 발생한 변수"로 분류할 수 있다.**

그래서 "일반 변수와 핀 변수의 구분" 자체는 해결 가능하다.

---

## 7. 모터와 센서를 어떻게 볼 것인가

모터와 센서는 더 이상 "핀 변수 여부를 구분할 수 있는가"라는 측면에서 다르게 볼 필요가 없다.

둘 다 핀 변수로 분류할 수 있다.

- 모터: 출력 핀 변수
- 센서: 입력 핀 변수

차이는 분류 기준이 아니라 `사용되는 슬롯 종류`다.

### 7.1 모터

모터는 주로 `analogWrite`에 연결된다.

예:

- `analogWrite(lwf, speed)`
- `analogWrite(rwf, speed)`

이 경우 `lwf`, `rwf`는 출력 핀 변수다.

### 7.2 센서

센서는 주로 `digitalRead` 또는 센서 조건 블록에 연결된다.

예:

- `digitalRead(sensorLeftPin)`
- `digitalRead(sensorRightPin)`

이 경우 `sensorLeftPin`, `sensorRightPin`은 입력 핀 변수다.

즉, 센서도 일반 변수와 구별 가능하다.

---

## 8. 역할 결정 주체는 Client다

중요한 점은 여기서 끝이 아니라는 것이다.

핀 변수와 일반 변수는 구별할 수 있게 되었지만, 아래 정보는 아직 별도 문제다.

- 이 입력 핀 변수가 `leftSensor`인가
- 이 입력 핀 변수가 `rightSensor`인가
- 이 출력 핀 변수가 `leftMotorF`인가
- 이 출력 핀 변수가 `rightMotorB`인가

즉, 아래 두 단계는 구분해야 한다.

### 8.1 1단계: 핀 변수 여부 판별

이건 이제 비교적 명확하다.

- pin 슬롯에 쓰였다 -> 핀 변수
- pin 슬롯에 안 쓰였다 -> 일반 변수

### 8.2 2단계: 역할 판별

이 단계는 런타임 자동 추론보다 Client 결정으로 처리하는 것이 맞다.

이유는 명확하다.

- 현실 아두이노와 실제 부품 연결 상태를 아는 쪽은 Client다.
- 사용자는 이미 "이 센서는 왼쪽", "이 모터는 오른쪽 뒤" 같은 의미를 알고 블록을 배치한다.
- 따라서 `leftSensor`, `rightSensor`, `leftMotorF`, `rightMotorB` 같은 역할은 작성 시점에 Client가 구분 가능하다.

즉, 문제는 "역할을 구분할 수 있는가"가 아니라 "그 구분 정보를 어디에 저장할 것인가"다.

권장 구조는 아래와 같다.

- 런타임은 pin 슬롯 사용 여부로 `일반 변수 / 핀 변수 / input / output`만 판별한다.
- Client는 실제 배선 기준으로 `leftSensor`, `rightSensor` 같은 역할을 결정한다.
- Client가 그 역할 정보를 XML/JSON에 저장한다.
- 런타임은 그 저장된 역할 정보를 그대로 신뢰하고 적용한다.

따라서 역할 바인드 수단은 필요하지만, 그 의미는 "런타임 추론 보완"이 아니라 "Client가 결정한 역할 저장"이다.

---

## 9. 권장 설계 원칙

### 원칙 1

값이 핀 번호와 같다고 해서 자동 매핑하지 않는다.

### 원칙 2

변수명 고정을 강제하지 않는다.

### 원칙 3

핀 변수는 오직 `pin 슬롯 사용 여부`로만 판별한다.

### 원칙 4

핀 변수는 입력/출력 방향을 함께 기록한다.

예:

- `lwf` -> pin variable, output
- `sensorLeftPin` -> pin variable, input

### 원칙 5

역할 바인드는 핀 변수 판별과 분리하고, 역할 결정 책임은 Client에 둔다.

---

## 10. 구체적 판별 규칙

### 10.1 핀 변수로 올리는 경우

아래 경우에 해당 변수를 핀 변수로 등록한다.

- `analogWrite.pinVar`에 사용됨
- `digitalRead(pinVar)`에 사용됨
- 센서 읽기 블록의 pin 슬롯에 사용됨
- 조건 블록의 센서 pin 슬롯에 사용됨

### 10.2 일반 변수로 유지하는 경우

아래는 핀 변수로 올리지 않는다.

- 산술 계산에만 사용됨
- 비교에만 사용됨
- 함수 인자값으로만 사용됨
- 상태값 저장에만 사용됨
- 숫자 리터럴과 같다는 이유만 있음

### 10.3 경고가 필요한 경우

아래는 warning 대상으로 본다.

- 같은 변수가 입력 핀과 출력 핀에 동시에 사용됨
- 핀 변수로 등록됐는데 실제 하드웨어 블록에서 더 이상 사용되지 않음
- 같은 역할에 여러 변수가 바인드됨
- 같은 pin 번호가 서로 다른 역할에 중복 바인드됨

---

## 11. 예시

### 예시 1: 일반 변수

```json
{ "type": "setVariable", "setVarName": "speed", "setVarValue": 9 }
```

`speed`는 숫자 9를 갖지만 pin 슬롯에 쓰이지 않으면 일반 변수다.

### 예시 2: 출력 핀 변수

```json
{ "type": "setVariable", "setVarName": "lwf", "setVarValue": 9 }
{ "type": "analogWrite", "pinVar": "lwf", "valueVar": "go" }
```

`lwf`는 `analogWrite.pinVar`에 사용되었으므로 출력 핀 변수다.

### 예시 3: 입력 핀 변수

```json
{ "type": "setVariable", "setVarName": "sensorLeftPin", "setVarValue": 3 }
{ "type": "if", "conditionSensorFunction": "digitalRead", "conditionPinVar": "sensorLeftPin" }
```

또는

```json
{ "type": "digitalRead", "pinVar": "sensorLeftPin" }
```

`sensorLeftPin`은 입력 pin 슬롯에 사용되었으므로 입력 핀 변수다.

### 예시 4: 잘못된 값 기반 판별이 없어야 하는 경우

```json
{ "type": "setVariable", "setVarName": "count", "setVarValue": 3 }
```

`count`는 3이지만 pin 슬롯에 쓰이지 않았으므로 일반 변수다.

---

## 12. 런타임에서 어떻게 적용할 것인가

런타임 적용 순서는 아래처럼 보는 것이 가장 안전하다.

1. XML/JSON 파싱 단계에서 pin 슬롯 사용 정보를 수집한다.
2. 각 변수에 대해 `general` 또는 `pin`을 분류한다.
3. 핀 변수라면 `input` 또는 `output` 방향을 기록한다.
4. Client가 저장한 역할 바인드 정보가 있다면 그 정보를 적용한다.
5. 값 기반 자동 매핑은 사용하지 않는다.

즉, 런타임은 변수값 자체보다 `어디에 연결되었는가`를 먼저 보고,
`leftSensor/rightSensor` 같은 역할은 Client가 넘긴 메타데이터를 기준으로 적용한다.

---

## 13. 현재 문서 기준 추천 방향

현재 시점에서 가장 합리적인 방향은 아래다.

### 13.1 바로 확정 가능한 것

- 일반 변수와 핀 변수는 구별 가능하다.
- 그 기준은 `PWM`만이 아니라 `digitalRead`까지 포함한 pin 슬롯 사용 여부다.
- 센서도 핀 변수로 분류할 수 있다.
- 역할 자체도 Client가 작성 시점에 판단 가능하다.

### 13.2 아직 별도 결정이 필요한 것

- `leftSensor`, `rightSensor` 역할 정보를 어떤 스키마로 저장할 것인가
- 역할 정보를 블록으로 둘 것인가, JSON 설정으로 둘 것인가
- Client가 저장한 역할 정보를 런타임에서 어떤 우선순위로 적용할 것인가

---

## 14. 최종 결론

이번 수정 이후의 결론은 아래와 같다.

### 결론 1

`PWM`만 핀 변수 판별의 근거가 되는 것은 아니다.

### 결론 2

센서는 `digitalRead` 계열의 pin 입력을 사용하므로, 센서 변수도 핀 변수로 구별 가능하다.

### 결론 3

따라서 일반 변수와 핀 변수의 구분은 아래 기준으로 통일할 수 있다.

- `analogWrite` pin 슬롯에 쓰였다 -> 출력 핀 변수
- `digitalRead` pin 슬롯에 쓰였다 -> 입력 핀 변수
- 어디에도 안 쓰였다 -> 일반 변수

### 결론 4

값이 기본 핀 번호와 같다는 이유로 자동 매핑하는 방식은 계속 사용하면 안 된다.

### 결론 5

핀 변수 여부는 pin 슬롯 사용으로 해결하고, RC카의 실제 역할 매핑은 Client가 결정해서 저장하는 구조로 가는 것이 가장 안정적이다.

---

## 15. 후속 문서와의 연결

이 문서를 바탕으로 다음 문서들이 이어진다.

- [BindPinBlockDesign.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/docs/documents/pin-binding/BindPinBlockDesign.md:1>)
- [BindPinSchemaDraft.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/docs/documents/pin-binding/BindPinSchemaDraft.md:1>)
- [BindPinRuntimeImplementationPlan.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/docs/plans/pin-binding/BindPinRuntimeImplementationPlan.md:1>)

이 문서들에서는 역할 바인드 방법을 더 구체적으로 다룬다.

---

## 16. 바로 적용 가능한 실무 방침

현재 바로 적용할 수 있는 방침은 아래다.

1. 새 설계에서는 변수값이 기본 핀 번호와 같다고 해서 핀으로 간주하지 않는다.
2. 새 설계에서는 변수명을 고정 문자열로 강제하지 않는다.
3. `analogWrite` pin 슬롯에 사용된 변수는 출력 핀 변수 후보로 기록한다.
4. `digitalRead` 또는 센서 조건 pin 슬롯에 사용된 변수는 입력 핀 변수 후보로 기록한다.
5. pin 슬롯에 쓰이지 않은 변수는 일반 변수로 유지한다.
6. 역할 매핑은 Client가 명시적으로 저장하고, 런타임은 그 정보를 신뢰해서 적용한다.
---

## 17. 2026-04-24 조건 비교 런타임 수정

### 17.0.1 추가 관찰: `dir = 0` 후 `999/888` 비교가 간헐적으로 놓치는 이유

추가 테스트에서 아래 패턴이 관찰되었다.

- `dir` 초기값을 `4/5`로 두면 비교가 안정적으로 동작함
- `dir` 초기값을 `0`으로 두고
- 센서 분기 안에서만 `dir = 999` 또는 `dir = 888`로 갱신한 뒤
- 이후 `dir == 999`처럼 비교하면
- 선 접촉 구간에서 간헐적으로 기대한 방향 기억이 적용되지 않는 경우가 있음

이 현상은 `999`, `888` 숫자 자체의 문제가 아니라, **초기값 `0`이 유효한 방향 기억값이 아니고, `dir`이 "센서 분기에서 먼저 갱신된 뒤 나중에 비교된다"는 구조에 의존하기 때문**으로 해석하는 것이 맞다.

현재 구조에서 `dir == 999` 비교는 센서를 직접 읽는 조건이 아니라, 이전 분기에서 저장된 상태 기억값(`dir`)을 읽는 조건이다.

따라서 아래 상황에서는 비교가 놓친 것처럼 보일 수 있다.

- 아직 어떤 센서 분기도 실행되지 않아 `dir`이 여전히 `0`임
- 그 tick에서 `dir`을 갱신하는 분기보다 비교 분기가 먼저 의미를 가지는 상황이 옴
- 선 접촉 구간에서 센서 상태가 빠르게 바뀜
- 양쪽 센서 동시 상태에서는 alternation 보정 로직 때문에 특정 분기가 그 tick에 건너뛰어질 수 있음

즉 `dir = 4/5`로 시작하면 시작 시점부터 이미 유효한 방향 기억값이 있어서 비교가 안정적이고, `dir = 0`으로 시작하면 첫 유효 갱신 전까지는 `dir == 999` / `dir == 888` 비교가 false가 되는 tick이 존재할 수 있다.

정리하면 이번 관찰은 다음처럼 문서화하는 것이 적절하다.

- 일반 변수와 센서 변수의 구별 실패 문제는 아님
- 비교 연산(`dir == 999`) 지원 실패 문제도 아님
- 핵심은 `dir`가 "memory variable"이고, 초기값 `0`은 유효한 방향 상태가 아니라는 점임
- 따라서 기억 변수는 비교에 사용되기 전에 유효한 초기 상태 또는 선행 갱신이 보장되어야 함

이번 작업에서 추가로 확인된 문제는 pin 분류와는 별개로, 런타임 조건식이 일반 변수와 센서값을 같은 방식으로 불리언화하고 있었다는 점이다.

대표적인 증상은 다음과 같았다.

- `dir = 5` 같은 일반 숫자 변수도 내부적으로 `1(true)`처럼 처리됨
- `"4"` 같은 비교 대상 리터럴도 내부적으로 `1(true)`처럼 처리됨
- 그 결과 `dir == 4`를 의도한 조건이 사실상 항상 참처럼 동작할 수 있었음
- 실제 주행에서는 차량이 한쪽 방향으로 계속 도는 현상으로 나타남

### 17.1 원인

기존 `BlockCodeExecutor`의 조건 평가에서는 좌항과 우항을 `TryReadSensorAsInt()`로 읽고 있었다.

이 방식은 다음처럼 값을 축약한다.

- 센서 `true/false` -> `1/0`
- 숫자 리터럴 -> `0보다 크면 1`
- 일반 변수값 -> `0보다 크면 1`

즉 `dir = 4`, `dir = 5`, `turn = 150`이 모두 같은 `true` 계열로 해석될 수 있었다.

이 문제는 pin 변수 분류의 오류가 아니라, **조건 피연산자 해석 계층에서 일반 수치와 센서값을 동일한 불리언 규칙으로 처리한 설계 문제**로 정리할 수 있다.

### 17.2 이번 수정 사항

이번 작업에서는 런타임 조건 평가를 아래처럼 변경했다.

1. 조건 피연산자를 읽을 때 실제 수치값을 유지한다.
2. 비교식 전용 필드 `conditionCompareOp`를 추가한다.
3. 현재 지원 비교 연산은 아래와 같다.
   - `eq`: 좌우 값이 같은지 비교
   - `gt`: 좌값이 우값보다 큰지 비교
4. 논리식 `and`, `or`는 실제 불리언 의미로만 평가한다.
5. 단일 조건(`conditionSensorFunction`)도 센서뿐 아니라 일반 변수/리터럴을 실제 값으로 읽어서 `conditionValue`와 비교한다.

### 17.3 JSON 스키마 반영

이제 비교 조건의 정상 표현은 아래 형태를 기준으로 한다.

```json
{
  "type": "ifElse",
  "conditionCompareOp": "eq",
  "conditionLeftSensorFunction": "dir",
  "conditionRightSensorFunction": "4",
  "body": [],
  "elseBody": []
}
```

여기서 의미는 다음과 같다.

- `conditionCompareOp`: 비교 연산 종류
- `conditionLeftSensorFunction`: 비교/논리 조건의 좌항 피연산자
- `conditionRightSensorFunction`: 비교/논리 조건의 우항 피연산자

필드명에 `SensorFunction`이 남아 있지만, 현재 의미는 센서 전용이 아니라 **센서/일반 변수/숫자 리터럴을 담는 조건 피연산자 슬롯**으로 보는 것이 맞다.

### 17.4 XML -> Runtime JSON 변환기 반영

`BE2XmlToRuntimeJson`에도 비교 조건 추출 로직을 추가했다.

- `Block Op Equal` -> `conditionCompareOp = "eq"`
- `Block Op BiggerThan` -> `conditionCompareOp = "gt"`

또한 비교식이나 논리식이 있는 경우에는 기존의 `conditionValue` 기반 단일 조건 추출이 덮어쓰지 않도록 정리했다.

즉 현재 조건 계층은 아래 세 갈래로 구분된다.

- 단일 조건: `conditionSensorFunction`
- 비교 조건: `conditionCompareOp`
- 논리 조건: `conditionLogicalOp`

### 17.5 구버전 JSON 호환 처리

이미 저장된 JSON 중에는 비교 의도인데도 `and`로 저장된 케이스가 있었다.

예:

```json
{
  "conditionLogicalOp": "and",
  "conditionLeftSensorFunction": "dir",
  "conditionRightSensorFunction": "4"
}
```

이 형태는 올바른 논리식이라기보다 사실상 `dir == 4`를 기대한 구버전 데이터에 가깝다.

이번 수정에서는 기존 저장 데이터를 즉시 모두 깨지지 않게 하기 위해 제한적인 legacy equality fallback도 추가했다.

적용 조건은 아래와 같다.

- 연산자가 `and`
- 한쪽은 `0/1`이 아닌 숫자 리터럴
- 다른 한쪽은 센서가 아닌 변수/값 토큰

이 경우 런타임은 제한적으로 `좌항 == 우항`처럼 해석한다.

단, 이것은 **구버전 호환용 보정**이며 앞으로의 정상 저장 포맷은 반드시 `conditionCompareOp`를 사용하는 방식이어야 한다.

### 17.6 Pin Plan 관점에서의 의미

이번 수정은 pin binding 규칙 자체를 바꾼 것은 아니지만, Pin Plan 이후 단계에서 반드시 필요한 보정이다.

Pin 분류가 올바르게 되어도:

- `dir`, `turn`, `go`, `stop` 같은 일반 제어 변수가
- 센서와 같은 방식으로 `0/1`로 축약되면
- 실제 주행 로직은 계속 오동작할 수 있다.

따라서 현재 구조에서는 다음 두 단계를 분리해서 봐야 한다.

1. pin 변수 분류
   - "어디에 연결되었는가"
2. 조건 피연산자 해석
   - "어떤 값으로 비교해야 하는가"

즉 Pin Plan의 결론에 다음 원칙을 보강한다.

**pin 변수 분류와 조건 피연산자 해석은 서로 다른 단계에서 독립적으로 정확해야 한다.**

정리하면:

- pin 변수는 연결 위치 기준으로 분류한다.
- 조건 피연산자는 실제 값 기준으로 평가한다.
- 센서값과 일반 수치 변수는 조건식에서 동일한 강제 불리언 규칙으로 처리하면 안 된다.

### 17.7 실제 반영 파일

이번 작업에서 수정한 주요 파일은 아래와 같다.

- `Assets/Scripts/Core/VirtualArduino/BlockCodeExecutor.cs`
- `Assets/Scripts/Core/BE2XmlToRuntimeJson.cs`

역할은 다음과 같다.

- `BlockCodeExecutor.cs`: 런타임 조건 평가 수정
- `BE2XmlToRuntimeJson.cs`: 비교식 JSON 생성 및 조건 피연산자 추출 수정

### 17.8 검증

수정 후 `dotnet build RC Car.sln` 기준으로 빌드는 통과했다.

기대 동작은 아래와 같다.

- `dir = 5`일 때 `dir == 4` 조건은 false
- `dir = 4`일 때만 true
- 실제 센서 조건(`leftSensor`, `rightSensor`)은 기존 의미 유지
- 구버전 `and` 비교 JSON도 제한적으로 호환
