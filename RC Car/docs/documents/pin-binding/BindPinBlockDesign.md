# BindPin Block Design

## 1. 목적

이 문서는 [PinPlan.md](</D:/Unity/Unity RC Line/Unity-RC-Line-Tracking-Visual-Coding-Simulator/RC Car/docs/plans/pin-binding/PinPlan.md:1>)의 결론을 실제 블록 설계로 내리는 문서다.

핵심 목표는 아래와 같다.

- 변수 이름을 강제하지 않는다.
- 숫자값이 기본 핀 번호와 같다고 해서 자동 매핑하지 않는다.
- 핀의 실제 역할은 `bindPin` 블록으로 명시한다.
- 모터/센서 역할 매핑을 XML/JSON/런타임에서 안정적으로 유지한다.

---

## 2. 블록 개념

`bindPin`은 "이 핀 또는 핀 변수는 어떤 하드웨어 역할을 담당한다"를 명시하는 초기화 블록이다.

이 블록은 계산 블록이 아니고, 제어문 블록도 아니다.
역할은 오직 하나다.

- 런타임 핀 매핑 선언

즉, 이 블록은 `setVariable`처럼 `init` 단계에서 한 번 실행되고,
이후 `analogWrite`, `digitalRead`, `FunctionDigitalRead`가 이 선언을 기반으로 동작하게 만든다.

---

## 3. 블록 타입

권장 타입은 아래와 같다.

- 분류: `Instruction Block`
- 권장 이름: `Block Ins BindPin`
- 선택적 커스텀 이름: `Block Cst BindPin`

권장 이유:

- `Operation` 블록이 아니다.
- 값을 반환하지 않는다.
- `WhenPlayClicked` 내부 초기화 영역에서 순서대로 실행되는 의미가 맞다.

---

## 4. 블록 UI 설계

## 4.1 권장 헤더 구조

가장 추천하는 UI는 아래 한 줄 구조다.

```text
핀 역할 설정 [역할 dropdown] [pin input]
```

예시:

- `핀 역할 설정 [왼쪽 센서] [sensorLeftPin]`
- `핀 역할 설정 [오른쪽 센서] [4]`
- `핀 역할 설정 [왼쪽 전진] [lwf]`

### 헤더 입력 구성

권장 입력은 2개다.

1. 역할 입력
2. 핀 입력

이 두 입력 모두 `section.Header.InputsArray`에 들어가도록 설계하는 것이 좋다.

이유:

- 현재 `BE2_BlocksSerializer`는 일반 블록에서 헤더 입력(`Header.InputsArray`)만 기본 직렬화한다.
- 역할 dropdown을 단순 label/custom item으로 만들면 저장/복원이 까다로워질 수 있다.
- 입력으로 만들면 XML 저장/로드 경로를 최대한 재사용할 수 있다.

---

## 4.2 역할 dropdown 값

사용자는 UI에서 한글 label을 보되,
내부 값은 안정적인 role id를 써야 한다.

권장 매핑:

- UI `왼쪽 센서` -> 내부 `leftSensor`
- UI `오른쪽 센서` -> 내부 `rightSensor`
- UI `왼쪽 센서2` -> 내부 `leftSensor2`
- UI `오른쪽 센서2` -> 내부 `rightSensor2`
- UI `왼쪽 전진` -> 내부 `leftMotorF`
- UI `왼쪽 후진` -> 내부 `leftMotorB`
- UI `오른쪽 전진` -> 내부 `rightMotorF`
- UI `오른쪽 후진` -> 내부 `rightMotorB`

중요한 점:

- 사용자는 문자열을 직접 타이핑하지 않는다.
- 내부 role id는 런타임 호환성과 안정성을 위해 고정한다.
- 이것은 "변수 이름 강제"와는 다르다.

---

## 4.3 핀 입력

핀 입력은 아래 두 형태를 모두 지원하는 것이 좋다.

1. 숫자 literal
2. 변수 참조

예시:

- `핀 역할 설정 [왼쪽 센서] [3]`
- `핀 역할 설정 [왼쪽 센서] [sensorLeftPin]`

권장 의미:

- 숫자 literal은 바로 핀 번호 사용
- 변수 참조는 init 시점 변수값을 읽어 핀 번호로 해석

---

## 5. 블록 사용 규칙

## 5.1 배치 위치

권장 배치 위치는 `WhenPlayClicked`의 init 구간이다.

즉:

- `Loop` 내부 사용 금지 권장
- 함수 body 내부 사용 금지 권장
- 조건문 내부 사용 금지 권장

이유:

- 핀 매핑은 하드웨어 초기화 의미가 강하다.
- loop 중간에 핀 역할이 바뀌면 디버깅이 매우 어려워진다.

권장 규칙:

- `bindPin`은 init 전용 블록으로 간주

---

## 5.2 순서 규칙

권장 순서는 아래다.

1. `setVariable`
2. `bindPin`
3. 나머지 init 액션
4. `loop`

예시:

```text
set sensorLeftPin = 3
set sensorRightPin = 4
set lwf = 9
set lwb = 11
set rwf = 6
set rwb = 10

bindPin(leftSensor, sensorLeftPin)
bindPin(rightSensor, sensorRightPin)
bindPin(leftMotorF, lwf)
bindPin(leftMotorB, lwb)
bindPin(rightMotorF, rwf)
bindPin(rightMotorB, rwb)
```

---

## 6. 런타임 의미

`bindPin`은 실행 시 아래 의미를 가진다.

```text
role <- pin
```

정확히는:

- `pin` 숫자를 구한다.
- 해당 숫자를 `role`에 연결한다.
- 이후 해당 `role`을 사용하는 입출력이 이 핀을 통해 동작한다.

예:

- `bindPin(leftSensor, 3)` -> `Pin 3`을 `leftSensor`에 연결
- `bindPin(rightMotorF, rwf)` + `rwf = 6` -> `Pin 6`을 `rightMotorF`에 연결

---

## 7. 중복/충돌 규칙

명확한 운영 규칙이 필요하다.

## 7.1 같은 role을 여러 번 bind하는 경우

권장 규칙:

- 마지막 선언이 우선
- warning 로그 출력

예:

```text
bindPin(leftSensor, 3)
bindPin(leftSensor, 7)
```

결과:

- 최종 매핑은 `leftSensor -> 7`
- warning: `leftSensor` role이 재정의되었음

## 7.2 같은 pin을 서로 다른 role에 bind하는 경우

권장 규칙:

- warning 로그 출력
- 마지막 선언 우선

예:

```text
bindPin(leftSensor, 3)
bindPin(rightMotorF, 3)
```

이 경우는 정상 사용으로 보기 어렵다.
그래도 런타임이 완전히 죽기보다는 마지막 선언으로 정리하는 것이 실용적이다.

---

## 8. 검증 규칙

`bindPin`은 아래 검증이 필요하다.

### 필수 검증

- role이 비어 있지 않은가
- pin 입력이 숫자로 해석 가능한가
- pin이 허용 범위(`0 <= pin < totalPins`) 안인가

### 권장 검증

- 같은 role이 중복 선언되었는가
- 같은 pin이 여러 role로 쓰였는가
- init 바깥에서 사용되었는가

---

## 9. 왜 이 블록이 필요한가

이 블록이 있으면 아래 두 가지를 동시에 만족할 수 있다.

1. 변수 이름 자유도 유지
2. 런타임 역할 명확성 확보

예를 들어 사용자는 아래처럼 자유롭게 쓸 수 있다.

```text
set a = 3
set b = 4
set c = 9
set d = 11

bindPin(leftSensor, a)
bindPin(rightSensor, b)
bindPin(leftMotorF, c)
bindPin(leftMotorB, d)
```

여기서 `a`, `b`, `c`, `d`는 이름만 봐서는 센서/모터 핀인지 알 수 없지만,
`bindPin`이 역할을 명확히 선언해준다.

---

## 10. 기존 구조와의 관계

현재 구조에서 `VirtualArduinoMicro`는 다음 기능을 이미 가지고 있다.

- `ConfigurePin(pin, function)`
- `ConfigureSensorPins(...)`
- `ConfigureMotorPins(...)`

즉, 런타임 하드웨어 매핑 API 자체는 이미 있다.
`bindPin`은 이 API를 블록/JSON 경로에서 직접 호출할 수 있게 만드는 블록이라고 보면 된다.

따라서 `bindPin`의 역할은 새 하드웨어 시스템을 만드는 것이 아니라,
이미 있는 `ConfigurePin()`을 블록 언어 수준으로 끌어올리는 것이다.

---

## 11. 권장 최소 기능 범위

1차 구현에서 추천하는 최소 범위는 아래다.

- 역할 dropdown 8종 지원
- pin literal/variable 입력 지원
- init에서만 사용
- JSON `bindPin` 노드 출력
- `BlockCodeExecutor`에서 `VirtualArduinoMicro.ConfigurePin()` 호출

1차에서는 아래는 제외해도 된다.

- 런타임 중 동적 재바인딩
- 복합 조건 기반 bind
- 자동 추론만으로 역할 결정

---

## 12. 최종 권장안

`bindPin`은 "변수 이름"이나 "숫자값"이 아니라, **역할을 명시적으로 선언하는 init 전용 instruction block**으로 설계하는 것이 가장 좋다.

정리하면:

- 변수는 자유롭게 이름 짓는다.
- pin 여부는 사용 위치로 판단할 수 있다.
- 실제 역할은 `bindPin(role, pin)`으로 확정한다.

이 방식이 `PinPlan.md`의 결론을 가장 일관되게 실현한다.
